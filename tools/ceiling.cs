// Works out the memory-bandwidth ceiling on decode speed for every model in models/models.csv.
//
// Usage:
//   dotnet run tools/ceiling.cs -- --experiment <name> [--out <ceilings.csv>] [--vram-gbps 448] [--ram-gbps 64]
//
// Run it from the repo root, with Docker running and the pinned image available locally. Decode reads every
// weight it uses once per token, so a token can't take less time than reading those bytes:
// GPU bytes / VRAM bandwidth + CPU bytes / RAM bandwidth. The ceiling is one over that time.
//
// The bytes come from each model's own tensor table. The GGUF header is read from the llm-models volume
// through the pinned image, so the model files never leave the volume. Each tensor's size is the gap between
// its data offset and the next one's.
//
// Placement follows llama.cpp:
//   - -ngl N puts the output layer and the last N - 1 repeating layers on the GPU. N above the layer count
//     means all of them, and is written as layers + 1.
//   - The token embedding table always stays in system RAM, and decode reads only one row of it per token,
//     so it counts as 0 bytes. A model with tied embeddings has no output tensor and uses the embedding table
//     as its output, which is read in full, so that one counts as the output layer.
//   - --n-cpu-moe M moves the expert tensors of the first M layers to system RAM.
//   - An MoE model reads only expert_used_count of its expert_count experts per token.
//
// It writes one row per model and -ngl value, from 0 (CPU only) to layers + 1 (all of them). MoE models also
// get one row per --n-cpu-moe value with every layer on the GPU.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

string[] columns =
[
    "model_file",
    "quant",
    "role",
    "layers",
    "n_gpu_layers",
    "n_cpu_moe",
    "gpu_weights_bytes",
    "gpu_bytes_per_token",
    "cpu_bytes_per_token",
    "vram_gbps",
    "ram_gbps",
    "ceiling_tokens_per_s",
];

string? experiment = null;
string? outPath = null;
// RTX 3060 Ti: 14 Gbps GDDR6 on a 256-bit bus.
double vramGbps = 448;
// Theoretical for dual-channel DDR4 at 4000 MT/s: 2 channels x 8 bytes x 4000 MT/s. Not measured.
double ramGbps = 64;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

    switch (args[i])
    {
        case "--experiment": experiment = Next(); break;
        case "--out": outPath = Next(); break;
        case "--vram-gbps": vramGbps = double.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--ram-gbps": ramGbps = double.Parse(Next(), CultureInfo.InvariantCulture); break;
        default: throw new ArgumentException($"Unknown argument: {args[i]}");
    }
}

if (string.IsNullOrWhiteSpace(experiment))
{
    Console.Error.WriteLine("Usage: dotnet run tools/ceiling.cs -- --experiment <name> [--out <ceilings.csv>] [--vram-gbps 448] [--ram-gbps 64]");
    return 1;
}

outPath ??= Path.Combine("experiments", experiment, "results", "ceilings.csv");

// The image is always started by digest. docker/images.md is the one place the digest is written down.
string image = Regex.Match(File.ReadAllText("docker/images.md"), @"ghcr\.io/ggml-org/llama\.cpp@sha256:[0-9a-f]{64}").Value;
if (image.Length == 0)
{
    Console.Error.WriteLine("No llama.cpp image digest found in docker/images.md.");
    return 1;
}

var lines = File.ReadAllLines(Path.Combine("models", "models.csv")).Where(l => l.Length > 0).ToList();
string[] header = lines[0].Split(',');
int Col(string name) => Array.IndexOf(header, name) is var c and >= 0 ? c : throw new InvalidDataException($"models.csv has no {name} column");

var rows = new List<string>();
foreach (string line in lines.Skip(1))
{
    string[] f = line.Split(',');
    string fileName = f[Col("file_name")];
    long sizeBytes = long.Parse(f[Col("size_bytes")], CultureInfo.InvariantCulture);
    int layers = int.Parse(f[Col("layers")], CultureInfo.InvariantCulture);

    Console.Error.WriteLine($"Reading the tensor table of {fileName}");
    var gguf = ReadGgufHeader(fileName);
    string arch = (string)gguf.Metadata["general.architecture"];
    int blockCount = Convert.ToInt32(gguf.Metadata[$"{arch}.block_count"], CultureInfo.InvariantCulture);
    if (blockCount != layers)
    {
        throw new InvalidDataException($"{fileName}: models.csv says {layers} layers, the GGUF says {blockCount}");
    }
    // Share of each expert tensor that one token reads. 1 for a dense model.
    double expertShare = gguf.Metadata.TryGetValue($"{arch}.expert_count", out object? expertCount)
        ? Convert.ToDouble(gguf.Metadata[$"{arch}.expert_used_count"], CultureInfo.InvariantCulture) / Convert.ToDouble(expertCount, CultureInfo.InvariantCulture)
        : 1;

    var tensors = TensorSizes(gguf, sizeBytes);
    bool tied = !tensors.Any(t => t.Name == "output.weight");

    // Bytes a tensor takes in memory, and bytes one token reads from it.
    var parts = tensors.Select(t =>
    {
        bool expert = t.Name.Contains("_exps.", StringComparison.Ordinal);
        Match block = Regex.Match(t.Name, @"^blk\.(\d+)\.");
        // Layer number: 0..layers-1 for repeating layers, layers for the output layer, -1 for the embedding table.
        int layer = block.Success ? int.Parse(block.Groups[1].Value, CultureInfo.InvariantCulture)
            : t.Name == "token_embd.weight" && !tied ? -1
            : layers;
        double perToken = layer == -1 ? 0 : expert ? t.Bytes * expertShare : t.Bytes;
        return (Layer: layer, Expert: expert, Bytes: (double)t.Bytes, PerToken: perToken);
    }).ToList();

    var settings = Enumerable.Range(0, layers + 2).Select(n => (Ngl: n, CpuMoe: 0)).ToList();
    if (expertShare < 1)
    {
        settings.AddRange(Enumerable.Range(1, layers).Select(m => (Ngl: layers + 1, CpuMoe: m)));
    }

    foreach (var (ngl, cpuMoe) in settings)
    {
        // Same rule as llama.cpp: layers from i_gpu_start upward are on the GPU, and the output layer counts as one.
        int gpuStart = Math.Max(layers + 1 - ngl, 0);
        bool OnGpu((int Layer, bool Expert, double Bytes, double PerToken) p) =>
            p.Layer >= gpuStart && !(p.Expert && p.Layer < cpuMoe);

        double gpuWeights = parts.Where(OnGpu).Sum(p => p.Bytes);
        double gpuPerToken = parts.Where(OnGpu).Sum(p => p.PerToken);
        double cpuPerToken = parts.Where(p => !OnGpu(p)).Sum(p => p.PerToken);
        double secondsPerToken = gpuPerToken / (vramGbps * 1e9) + cpuPerToken / (ramGbps * 1e9);

        rows.Add(string.Join(',',
            fileName,
            f[Col("quant")],
            f[Col("role")],
            Text(layers),
            Text(ngl),
            Text(cpuMoe),
            Text(Math.Round(gpuWeights)),
            Text(Math.Round(gpuPerToken)),
            Text(Math.Round(cpuPerToken)),
            Text(vramGbps),
            Text(ramGbps),
            Text(Math.Round(1 / secondsPerToken, 1))));
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllLines(outPath, rows.Prepend(string.Join(',', columns)));
Console.Error.WriteLine($"Wrote {rows.Count} rows to {outPath}");
return 0;

// Reads the first part of a model file from the volume: enough for the GGUF header and tensor table.
(Dictionary<string, object> Metadata, List<(string Name, long Offset)> Tensors, long DataStart) ReadGgufHeader(string fileName)
{
    const int HeaderBytes = 64 * 1024 * 1024;
    var info = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true };
    foreach (string argument in new[]
    {
        "run", "--rm", "--pull", "never", "-v", "llm-models:/models:ro", "--entrypoint", "head",
        image, "-c", Text(HeaderBytes), $"/models/{fileName}",
    })
    {
        info.ArgumentList.Add(argument);
    }
    using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start docker.");
    var buffer = new MemoryStream();
    process.StandardOutput.BaseStream.CopyTo(buffer);
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Reading {fileName} from the volume failed with exit code {process.ExitCode}");
    }

    buffer.Position = 0;
    using var reader = new BinaryReader(buffer, Encoding.UTF8);
    if (reader.ReadUInt32() != 0x46554747) // "GGUF", little-endian
    {
        throw new InvalidDataException($"{fileName} isn't a GGUF file");
    }
    uint version = reader.ReadUInt32();
    if (version != 3)
    {
        throw new InvalidDataException($"{fileName}: GGUF version {version}, only 3 is supported");
    }
    long tensorCount = (long)reader.ReadUInt64();
    long metadataCount = (long)reader.ReadUInt64();

    var metadata = new Dictionary<string, object>();
    for (long k = 0; k < metadataCount; k++)
    {
        string key = ReadString(reader);
        metadata[key] = ReadValue(reader, reader.ReadUInt32());
    }

    var tensors = new List<(string Name, long Offset)>();
    for (long t = 0; t < tensorCount; t++)
    {
        string name = ReadString(reader);
        uint dimensions = reader.ReadUInt32();
        for (uint d = 0; d < dimensions; d++)
        {
            reader.ReadUInt64();
        }
        reader.ReadUInt32(); // tensor type; the size comes from the offsets instead
        tensors.Add((name, (long)reader.ReadUInt64()));
    }

    long alignment = metadata.TryGetValue("general.alignment", out object? a) ? Convert.ToInt64(a, CultureInfo.InvariantCulture) : 32;
    long dataStart = (buffer.Position + alignment - 1) / alignment * alignment;
    return (metadata, tensors, dataStart);
}

// Each tensor runs from its offset to the next tensor's offset; the last one runs to the end of the file.
// That includes up to alignment - 1 bytes of padding per tensor, which is negligible.
static List<(string Name, long Bytes)> TensorSizes(
    (Dictionary<string, object> Metadata, List<(string Name, long Offset)> Tensors, long DataStart) gguf, long fileSize)
{
    var sorted = gguf.Tensors.OrderBy(t => t.Offset).ToList();
    long dataSize = fileSize - gguf.DataStart;
    return sorted
        .Select((t, i) => (t.Name, (i + 1 < sorted.Count ? sorted[i + 1].Offset : dataSize) - t.Offset))
        .ToList();
}

static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(checked((int)reader.ReadUInt64())));

// GGUF value types. Arrays are read and dropped; only scalars are kept.
static object ReadValue(BinaryReader reader, uint type) => type switch
{
    0 => reader.ReadByte(),
    1 => reader.ReadSByte(),
    2 => reader.ReadUInt16(),
    3 => reader.ReadInt16(),
    4 => reader.ReadUInt32(),
    5 => reader.ReadInt32(),
    6 => reader.ReadSingle(),
    7 => reader.ReadBoolean(),
    8 => ReadString(reader),
    9 => ReadArray(reader),
    10 => reader.ReadUInt64(),
    11 => reader.ReadInt64(),
    12 => reader.ReadDouble(),
    _ => throw new InvalidDataException($"Unknown GGUF value type {type}"),
};

static object ReadArray(BinaryReader reader)
{
    uint itemType = reader.ReadUInt32();
    ulong count = reader.ReadUInt64();
    for (ulong n = 0; n < count; n++)
    {
        ReadValue(reader, itemType);
    }
    return "(array)";
}

static string Text(IFormattable value) => value.ToString(null, CultureInfo.InvariantCulture);

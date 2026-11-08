// Works out the memory-bandwidth ceiling on decode speed for every model in models/models.csv.
//
// Usage:
//   dotnet run tools/ceiling.cs -- --experiment <name> [--out <ceilings.csv>] [--vram-gbps 448] [--ram-gbps 64]
//
// Run it from the repo root. Decode reads every weight it uses once per token, so a token can't take less
// time than reading those bytes: GPU bytes / VRAM bandwidth + CPU bytes / RAM bandwidth. The ceiling is
// one over that time. It writes one row per model and number of GPU layers, from 0 (CPU only) to all of them.

using System.Globalization;

string[] columns =
[
    "model_file",
    "quant",
    "role",
    "model_size_bytes",
    "total_params",
    "active_params",
    "bytes_per_token",
    "layers",
    "n_gpu_layers",
    "gpu_bytes",
    "cpu_bytes",
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

var lines = File.ReadAllLines(Path.Combine("models", "models.csv")).Where(l => l.Length > 0).ToList();
string[] header = lines[0].Split(',');
int Col(string name) => Array.IndexOf(header, name) is var c and >= 0 ? c : throw new InvalidDataException($"models.csv has no {name} column");

var rows = new List<string>();
foreach (string line in lines.Skip(1))
{
    string[] f = line.Split(',');
    long sizeBytes = long.Parse(f[Col("size_bytes")], CultureInfo.InvariantCulture);
    double totalParams = Params(f[Col("total_params")]);
    double activeParams = Params(f[Col("active_params")]);
    int layers = int.Parse(f[Col("layers")], CultureInfo.InvariantCulture);

    // The file size stands in for the weights. It includes the GGUF header and metadata, so it overstates
    // the bytes by a little. An MoE model reads only its active parameters, so it reads that share of the file.
    double bytesPerToken = sizeBytes * activeParams / totalParams;

    // Split evenly across layers. The embedding and output tensors aren't really spread that way.
    for (int gpuLayers = 0; gpuLayers <= layers; gpuLayers++)
    {
        double gpuBytes = bytesPerToken * gpuLayers / layers;
        double cpuBytes = bytesPerToken - gpuBytes;
        double secondsPerToken = gpuBytes / (vramGbps * 1e9) + cpuBytes / (ramGbps * 1e9);

        rows.Add(string.Join(',',
            f[Col("file_name")],
            f[Col("quant")],
            f[Col("role")],
            Text(sizeBytes),
            f[Col("total_params")],
            f[Col("active_params")],
            Text(Math.Round(bytesPerToken)),
            Text(layers),
            Text(gpuLayers),
            Text(Math.Round(gpuBytes)),
            Text(Math.Round(cpuBytes)),
            Text(vramGbps),
            Text(ramGbps),
            Text(Math.Round(1 / secondsPerToken, 1))));
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllLines(outPath, rows.Prepend(string.Join(',', columns)));
Console.Error.WriteLine($"Wrote {rows.Count} rows to {outPath}");
return 0;

// "4.0B" -> 4.0e9
static double Params(string value) =>
    value.EndsWith('B')
        ? double.Parse(value[..^1], CultureInfo.InvariantCulture) * 1e9
        : throw new InvalidDataException($"Expected a parameter count like 4.0B, got {value}");

static string Text(IFormattable value) => value.ToString(null, CultureInfo.InvariantCulture);

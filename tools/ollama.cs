// Measures what Ollama picks, and how fast it generates, for models already imported into the ollama-data volume.
//
// Usage:
//   dotnet run tools/ollama.cs -- --experiment <name> --models <name,name,...> [--out <ollama.csv>]
//       [--repetitions 5] [--num-predict 128] [--port 11435] [--cool-to-c 45] [--cool-timeout-s 900]
//
// Run it from the repo root. It starts the pinned Ollama image on 127.0.0.1 only, with cloud features off. For each
// model it waits for the GPU to cool, loads the model with a warm-up request, reads what Ollama chose (context, GPU
// layers, flash attention, threads, KV cache) from /api/ps and from the llama-server log, then sends the same prompt
// --repetitions times and times generation from Ollama's own eval_count and eval_duration. Only num_predict is set in
// the requests; everything else is Ollama's default. The container is removed at the end.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string[] resultColumns =
[
    "run_id",
    "started_at",
    "experiment",
    "image_digest",
    "ollama_version",
    "llama_commit",
    "ollama_model",
    "model_file",
    "context_length",
    "gpu_layers",
    "flash_attn",
    "cache_type_k",
    "cache_type_v",
    "threads",
    "batch",
    "ollama_size_bytes",
    "ollama_size_vram_bytes",
    "vram_used_mib",
    "prompt_tokens",
    "gen_tokens",
    "repetitions",
    "tokens_per_s_mean",
    "tokens_per_s_stddev",
    "notes",
];

const string ContainerName = "ollama-run";
const string Prompt        = "Write a long story about a lighthouse keeper.";

string? experiment = null;
string? modelList = null;
string? outPath = null;
int repetitions = 5;
int numPredict = 128;
int port = 11435;
int coolToC = 45;
int coolTimeoutS = 900;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

    switch (args[i])
    {
        case "--experiment": experiment = Next(); break;
        case "--models": modelList = Next(); break;
        case "--out": outPath = Next(); break;
        case "--repetitions": repetitions = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--num-predict": numPredict = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--port": port = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--cool-to-c": coolToC = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--cool-timeout-s": coolTimeoutS = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        default:
            Console.Error.WriteLine($"Unknown argument {args[i]}");
            return 1;
    }
}

if (string.IsNullOrWhiteSpace(experiment) || string.IsNullOrWhiteSpace(modelList))
{
    Console.Error.WriteLine("Usage: dotnet run tools/ollama.cs -- --experiment <name> --models <name,name,...> [--out <ollama.csv>] [--repetitions 5] [--num-predict 128] [--port 11435] [--cool-to-c 45] [--cool-timeout-s 900]");
    return 1;
}

string[] models = modelList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
outPath ??= Path.Combine("experiments", experiment, "results", "ollama.csv");
string rawDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath))!, "raw");
Directory.CreateDirectory(rawDirectory);

// The image is always started by digest. docker/images.md is the one place the digest is written down.
string image = Regex.Match(File.ReadAllText("docker/images.md"), @"ollama/ollama@sha256:[0-9a-f]{64}").Value;
if (image.Length == 0)
{
    Console.Error.WriteLine("No Ollama image digest found in docker/images.md.");
    return 1;
}
string imageDigest = image[(image.IndexOf('@') + 1)..];

// Ollama stores each imported GGUF as a blob named after its SHA-256, so the blob says which file it is.
var filesBySha = ReadCsv("models/models.csv").ToDictionary(row => row["sha256"], row => row["file_name"]);

Docker(["rm", "-f", ContainerName]);
var (startCode, startOutput) = Docker(
[
    "run", "-d", "--name", ContainerName, "--pull", "never", "--gpus", "all",
    "-p", $"127.0.0.1:{port}:11434",
    "-v", "ollama-data:/root/.ollama",
    "-v", "llm-models:/models:ro",
    "-e", "OLLAMA_NO_CLOUD=1",
    image,
]);
if (startCode != 0)
{
    Console.Error.WriteLine($"Couldn't start Ollama: {startOutput}");
    return 1;
}

using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromMinutes(10) };

try
{
    string? version = null;
    for (int waited = 0; waited < 60 && version is null; waited++)
    {
        try
        {
            using var versionJson = JsonDocument.Parse(http.GetStringAsync("/api/version").Result);
            version = versionJson.RootElement.GetProperty("version").GetString();
        }
        catch (AggregateException)
        {
            Thread.Sleep(1000);
        }
    }
    if (version is null)
    {
        Console.Error.WriteLine("Ollama didn't answer within a minute.");
        return 1;
    }

    bool writeHeader = !File.Exists(outPath) || new FileInfo(outPath).Length == 0;
    using var results = new StreamWriter(outPath, append: true) { AutoFlush = true, NewLine = "\n" };
    if (writeHeader)
    {
        results.WriteLine(string.Join(',', resultColumns));
    }

    string stamp = DateTime.Now.ToString("yyyy-MM-ddTHHmmss", CultureInfo.InvariantCulture);
    foreach (string model in models)
    {
        string runId = $"{stamp}-ollama-{model}";
        var notes = new List<string>();

        Console.Error.WriteLine($"{runId}: waiting for {coolToC} °C");
        var coolStart = Stopwatch.StartNew();
        int temperature;
        while ((temperature = GpuTemperature()) > coolToC && coolStart.Elapsed.TotalSeconds < coolTimeoutS)
        {
            Thread.Sleep(1000);
        }
        if (temperature > coolToC)
        {
            notes.Add($"cool-down timed out at {temperature} C");
        }

        string startedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        Console.Error.WriteLine($"{runId}: loading at {temperature} °C");
        var replies = new List<string> { Generate(model, 16) };

        using var psJson = JsonDocument.Parse(http.GetStringAsync("/api/ps").Result);
        var loaded = psJson.RootElement.GetProperty("models").EnumerateArray()
            .First(m => m.GetProperty("model").GetString()!.StartsWith(model + ":", StringComparison.Ordinal));
        int vramUsedMib = GpuMemoryUsedMib();

        var speeds = new List<double>();
        var promptTokens = new List<int>();
        var genTokens = new List<int>();
        for (int r = 0; r < repetitions; r++)
        {
            string reply = Generate(model, numPredict);
            replies.Add(reply);
            using var replyJson = JsonDocument.Parse(reply);
            var root = replyJson.RootElement;
            int evalCount = root.GetProperty("eval_count").GetInt32();
            long evalNs = root.GetProperty("eval_duration").GetInt64();
            speeds.Add(evalCount / (evalNs / 1e9));
            promptTokens.Add(root.GetProperty("prompt_eval_count").GetInt32());
            genTokens.Add(evalCount);
            Console.Error.WriteLine($"  run {r + 1}/{repetitions}: {evalCount} tokens at {speeds[^1]:F2} tokens/s");
        }

        // Unload, so the next model starts from an empty GPU.
        Generate(model, 0, keepAliveZero: true);

        // The llama-server log of this load starts at Ollama's last "starting llama-server" line.
        string log = Docker(["logs", ContainerName]).Output;
        string serverLog = log[log.LastIndexOf("starting llama-server", StringComparison.Ordinal)..];
        File.WriteAllText(Path.Combine(rawDirectory, $"{runId}.log"), serverLog);
        File.WriteAllText(Path.Combine(rawDirectory, $"{runId}.jsonl"), string.Join('\n', replies) + "\n");

        string command = Regex.Match(serverLog, "cmd=\"([^\"]*)\"").Groups[1].Value;
        string blobSha = Regex.Match(command, @"sha256-([0-9a-f]{64})").Groups[1].Value;
        string gpuLayers = Regex.Match(serverLog, @"offloaded (\d+/\d+) layers to GPU").Groups[1].Value;
        string flashAttn = serverLog.Contains("Flash Attention enabled", StringComparison.Ordinal) ? "on" : Regex.Match(command, @"--flash-attn (\S+)").Groups[1].Value;
        string typeK = Regex.Match(command, @"(?:-ctk|--cache-type-k) (\S+)") is { Success: true } k ? k.Groups[1].Value : "f16";
        string typeV = Regex.Match(command, @"(?:-ctv|--cache-type-v) (\S+)") is { Success: true } v ? v.Groups[1].Value : "f16";
        string threads = Regex.Match(serverLog, @"n_threads = (\d+)").Groups[1].Value;
        string batch = Regex.Match(command, @" -b (\d+)").Groups[1].Value;
        string llamaCommit = Regex.Match(serverLog, @"build \d+ \(([0-9a-f]+)\)").Groups[1].Value;
        string cpuMoe = Regex.Match(command, @"(?:--n-cpu-moe|-ncmoe) (\d+)").Groups[1].Value;
        if (cpuMoe.Length > 0)
        {
            notes.Add($"n-cpu-moe {cpuMoe}");
        }

        // For a MoE model, fit can keep every layer on the GPU and move the expert tensors of some of them to the CPU.
        string overflowing = Regex.Match(serverLog, @"\d+ layers \((\d+) overflowing\)").Groups[1].Value;
        if (overflowing.Length > 0)
        {
            notes.Add($"fit put the experts of {overflowing} layers on the CPU");
        }
        if (Regex.Match(command, @"(?:-ngl|--n-gpu-layers) (\S+)") is { Success: true } ngl)
        {
            notes.Add($"ngl {ngl.Groups[1].Value} passed");
        }

        double mean = speeds.Average();
        double stddev = Math.Sqrt(speeds.Sum(s => (s - mean) * (s - mean)) / Math.Max(1, speeds.Count - 1));

        string[] row =
        [
            runId,
            startedAt,
            experiment,
            imageDigest,
            version,
            llamaCommit,
            model,
            filesBySha.GetValueOrDefault(blobSha, ""),
            loaded.GetProperty("context_length").GetRawText(),
            gpuLayers,
            flashAttn,
            typeK,
            typeV,
            threads,
            batch,
            loaded.GetProperty("size").GetRawText(),
            loaded.GetProperty("size_vram").GetRawText(),
            vramUsedMib.ToString(CultureInfo.InvariantCulture),
            Text(promptTokens.Average()),
            Text(genTokens.Average()),
            repetitions.ToString(CultureInfo.InvariantCulture),
            Text(Math.Round(mean, 6)),
            Text(Math.Round(stddev, 6)),
            string.Join("; ", notes).Replace(",", ""),
        ];
        results.WriteLine(string.Join(',', row));
        Console.Error.WriteLine($"  {model}: ctx {row[8]}, {gpuLayers} layers on the GPU, {row[21]} tokens/s");
    }
}
finally
{
    Docker(["rm", "-f", ContainerName]);
}

Console.Error.WriteLine($"Results in {outPath}");
return 0;

// One non-streamed /api/generate request. Only num_predict (and keep_alive, to unload) differ from Ollama's defaults.
string Generate(string model, int tokens, bool keepAliveZero = false)
{
    // A request with keep_alive 0 and no prompt only unloads the model.
    var request = new MemoryStream();
    using (var writer = new Utf8JsonWriter(request))
    {
        writer.WriteStartObject();
        writer.WriteString("model", model);
        if (keepAliveZero)
        {
            writer.WriteNumber("keep_alive", 0);
        }
        else
        {
            writer.WriteString("prompt", Prompt);
            writer.WriteBoolean("stream", false);
            writer.WriteStartObject("options");
            writer.WriteNumber("num_predict", tokens);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
    using var content = new ByteArrayContent(request.ToArray());
    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    using var response = http.PostAsync("/api/generate", content).Result;
    string text = response.Content.ReadAsStringAsync().Result;
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Ollama returned {(int)response.StatusCode}: {text}");
    }

    // The context field is the token ids of the whole exchange; it's large and says nothing the counts don't.
    using var json = JsonDocument.Parse(text);
    var reply = new MemoryStream();
    using (var writer = new Utf8JsonWriter(reply))
    {
        writer.WriteStartObject();
        foreach (var property in json.RootElement.EnumerateObject().Where(static p => p.Name != "context"))
        {
            property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
    return Encoding.UTF8.GetString(reply.ToArray());
}

static int GpuTemperature() => int.Parse(NvidiaSmi("temperature.gpu"), CultureInfo.InvariantCulture);

static int GpuMemoryUsedMib() => int.Parse(NvidiaSmi("memory.used"), CultureInfo.InvariantCulture);

static string NvidiaSmi(string query)
{
    var info = new ProcessStartInfo("nvidia-smi") { UseShellExecute = false, RedirectStandardOutput = true };
    foreach (string argument in new[] { $"--query-gpu={query}", "--format=csv,noheader,nounits" })
    {
        info.ArgumentList.Add(argument);
    }
    using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start nvidia-smi.");
    string output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    return output;
}

static (int ExitCode, string Output) Docker(string[] arguments)
{
    var info = new ProcessStartInfo("docker")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (string argument in arguments)
    {
        info.ArgumentList.Add(argument);
    }
    using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start docker.");
    var error = process.StandardError.ReadToEndAsync();
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output + error.Result);
}

static List<Dictionary<string, string>> ReadCsv(string path)
{
    // Plain CSV: no quoted fields and no commas inside values.
    var lines = File.ReadAllLines(path).Where(line => line.Trim().Length > 0).ToList();
    string[] header = lines[0].Split(',', StringSplitOptions.TrimEntries);
    return lines
        .Skip(1)
        .Select(line => line.Split(',', StringSplitOptions.TrimEntries))
        .Select(values => header.Select((column, i) => (column, value: i < values.Length ? values[i] : "")).ToDictionary(p => p.column, p => p.value))
        .ToList();
}

static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);

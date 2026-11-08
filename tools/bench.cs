// Runs a llama-bench sweep in the pinned llama.cpp image and appends one results row per test.
//
// Usage:
//   dotnet run tools/bench.cs -- --sweep <sweep.csv> --experiment <name> [--out <results.csv>] [--cool-to-c 45] [--cool-timeout-s 900]
//
// Run it from the repo root. For each configuration in the sweep file it waits for the GPU to cool down,
// runs llama-bench once (pp and tg as separate tests), keeps the raw JSON and log, and joins the GPU samples
// taken during each test. The GPU sampler (tools/gpu-sample.cs) runs for the whole sweep.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

string[] sweepColumns =
[
    "name",
    "model_file",
    "n_gpu_layers",
    "flash_attn",
    "cache_type_k",
    "cache_type_v",
    "n_cpu_moe",
    "threads",
    "n_prompt",
    "n_gen",
    "depth_tokens",
    "repetitions",
    "notes",
];

string[] resultColumns =
[
    "run_id",
    "started_at",
    "ended_at",
    "experiment",
    "notes",
    "image_digest",
    "llama_build",
    "llama_commit",
    "model_file",
    "n_gpu_layers",
    "flash_attn",
    "cache_type_k",
    "cache_type_v",
    "n_cpu_moe",
    "threads",
    "test",
    "n_tokens",
    "depth_tokens",
    "repetitions",
    "tokens_per_s_mean",
    "tokens_per_s_stddev",
    "model_sha256_short",
    "quant",
    "model_size_bytes",
    "vram_peak_mib",
    "power_mean_w",
    "temperature_start_c",
    "temperature_end_c",
    "sm_clock_mean_mhz",
    "clock_event_reasons_seen",
    "bandwidth_ceiling_tokens_per_s",
];

const int SamplerIntervalMs = 250;

string? sweepPath = null;
string? experiment = null;
string? outPath = null;
// Fixed so it can't drift with the temperature a sweep starts at. True idle (about 41 °C) takes about
// 10 minutes to reach because the fans stop at low temperatures; 45 °C takes about 3.
int coolToC = 45;
int coolTimeoutS = 900;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

    switch (args[i])
    {
        case "--sweep": sweepPath = Next(); break;
        case "--experiment": experiment = Next(); break;
        case "--out": outPath = Next(); break;
        case "--cool-to-c": coolToC = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--cool-timeout-s": coolTimeoutS = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        default: throw new ArgumentException($"Unknown argument: {args[i]}");
    }
}

if (string.IsNullOrWhiteSpace(sweepPath) || string.IsNullOrWhiteSpace(experiment))
{
    Console.Error.WriteLine("Usage: dotnet run tools/bench.cs -- --sweep <sweep.csv> --experiment <name> [--out <results.csv>] [--cool-to-c 45] [--cool-timeout-s 900]");
    return 1;
}

outPath ??= Path.Combine("experiments", experiment, "results", "bench.csv");
string rawDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath))!, "raw");
Directory.CreateDirectory(rawDirectory);

// The image is always started by digest. docker/images.md is the one place the digest is written down.
string image = Regex.Match(File.ReadAllText("docker/images.md"), @"ghcr\.io/ggml-org/llama\.cpp@sha256:[0-9a-f]{64}").Value;
if (image.Length == 0)
{
    Console.Error.WriteLine("No llama.cpp image digest found in docker/images.md.");
    return 1;
}
string imageDigest = image[(image.IndexOf('@') + 1)..];

// The runner never pulls. Stop here if the pinned image isn't on this machine.
var inspectInfo = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (string argument in new[] { "image", "inspect", image })
{
    inspectInfo.ArgumentList.Add(argument);
}
using (var inspect = Process.Start(inspectInfo) ?? throw new InvalidOperationException("Couldn't start docker."))
{
    inspect.StandardOutput.ReadToEnd();
    inspect.StandardError.ReadToEnd();
    inspect.WaitForExit();
    if (inspect.ExitCode != 0)
    {
        Console.Error.WriteLine($"The pinned image isn't available locally: {image}");
        return 1;
    }
}

var manifest = ReadCsv("models/models.csv").ToDictionary(row => row["file_name"]);

// Written by tools/ceiling.cs, next to the results file. Keyed by model file and number of GPU layers.
string ceilingsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath))!, "ceilings.csv");
var ceilings = File.Exists(ceilingsPath)
    ? ReadCsv(ceilingsPath).ToDictionary(
        row => (row["model_file"], int.Parse(row["n_gpu_layers"], CultureInfo.InvariantCulture)),
        row => row["ceiling_tokens_per_s"])
    : null;
if (ceilings is null)
{
    Console.Error.WriteLine($"No {ceilingsPath}, so bandwidth_ceiling_tokens_per_s stays empty. Run tools/ceiling.cs first to fill it.");
}

var configs = ReadCsv(sweepPath);
foreach (var config in configs)
{
    if (sweepColumns.Any(column => !config.ContainsKey(column)))
    {
        Console.Error.WriteLine($"The sweep file needs these columns: {string.Join(',', sweepColumns)}");
        return 1;
    }
    if (!Regex.IsMatch(config["name"], @"^[A-Za-z0-9._-]+$"))
    {
        Console.Error.WriteLine($"Configuration name '{config["name"]}' can only use letters, digits, '.', '_' and '-'.");
        return 1;
    }
    if (!manifest.ContainsKey(config["model_file"]))
    {
        Console.Error.WriteLine($"{config["model_file"]} isn't in models/models.csv.");
        return 1;
    }
}

string sweepName = Path.GetFileNameWithoutExtension(sweepPath);
string stamp = DateTime.Now.ToString("yyyy-MM-ddTHHmmss", CultureInfo.InvariantCulture);
string samplesPath = Path.Combine(rawDirectory, $"{stamp}-{sweepName}-gpu.csv");

// The sampler runs for the whole sweep. Each row's sampler columns come from the samples inside its test.
var samplerInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardError = true };
foreach (string argument in new[] { "run", "tools/gpu-sample.cs", "--", "--label", $"{stamp}-{sweepName}", "--out", samplesPath, "--interval-ms", $"{SamplerIntervalMs}" })
{
    samplerInfo.ArgumentList.Add(argument);
}
using var sampler = Process.Start(samplerInfo) ?? throw new InvalidOperationException("Couldn't start the GPU sampler.");
sampler.ErrorDataReceived += (_, _) => { };
sampler.BeginErrorReadLine();

try
{
    Console.Error.WriteLine($"Sampling the GPU into {samplesPath}");
    int? firstTemperature = null;
    for (int waited = 0; waited < 120 && (firstTemperature = LatestTemperature(samplesPath)) is null; waited++)
    {
        Thread.Sleep(1000);
    }
    if (firstTemperature is null)
    {
        Console.Error.WriteLine("The GPU sampler wrote no samples within 2 minutes.");
        return 1;
    }

    int targetC = coolToC;
    Console.Error.WriteLine($"Cool-down target: {targetC} °C or lower before each configuration.");

    bool writeHeader = !File.Exists(outPath) || new FileInfo(outPath).Length == 0;
    using var results = new StreamWriter(outPath, append: true) { AutoFlush = true, NewLine = "\n" };
    if (writeHeader)
    {
        results.WriteLine(string.Join(',', resultColumns));
    }

    for (int c = 0; c < configs.Count; c++)
    {
        var config = configs[c];
        string runId = $"{stamp}-{config["name"]}";
        var notes = new List<string>();
        if (config["notes"].Length > 0)
        {
            notes.Add(config["notes"]);
        }

        Console.Error.WriteLine($"[{c + 1}/{configs.Count}] {runId}: waiting for {targetC} °C");
        var coolStart = Stopwatch.StartNew();
        int? temperature;
        while ((temperature = LatestTemperature(samplesPath)) > targetC && coolStart.Elapsed.TotalSeconds < coolTimeoutS)
        {
            Thread.Sleep(1000);
        }
        if (temperature > targetC)
        {
            notes.Add($"cool-down timed out at {temperature} C");
        }

        Console.Error.WriteLine($"[{c + 1}/{configs.Count}] {runId}: running llama-bench at {temperature} °C");
        var run = RunLlamaBench(config);
        File.WriteAllText(Path.Combine(rawDirectory, $"{runId}.log"), string.Join('\n', run.Log.Select(line => $"{line.Time:O} {line.Text}")) + "\n");
        if (run.ExitCode != 0)
        {
            Console.Error.WriteLine($"llama-bench failed with exit code {run.ExitCode}. Log: {Path.Combine(rawDirectory, $"{runId}.log")}");
            continue;
        }
        File.WriteAllText(Path.Combine(rawDirectory, $"{runId}.json"), run.Json);

        // Give the sampler time to write the samples that cover the end of the last test.
        Thread.Sleep(SamplerIntervalMs * 4);
        var samples = ReadSamples(samplesPath);

        using var json = JsonDocument.Parse(run.Json);
        var tests = json.RootElement.EnumerateArray().ToList();
        for (int t = 0; t < tests.Count; t++)
        {
            var test = tests[t];
            string Raw(string name) => test.GetProperty(name).GetRawText().Trim('"');

            // The timed runs of test t start at its "prompt run k/R" or "generation run k/R" progress lines,
            // and each lasts as long as llama-bench measured it (samples_ns). Warm-up and depth runs are left out.
            var starts = run.Log
                .Where(line => Regex.IsMatch(line.Text, $@"llama-bench: benchmark {t + 1}/\d+: (prompt|generation) run \d+/\d+$"))
                .Select(line => line.Time)
                .ToList();
            var durations = test.GetProperty("samples_ns").EnumerateArray().Select(ns => TimeSpan.FromTicks(ns.GetInt64() / 100)).ToList();
            var windows = starts.Zip(durations, (start, duration) => (Start: start, End: start + duration)).ToList();
            var inside = samples.Where(sample => windows.Any(w => sample.Time >= w.Start && sample.Time <= w.End)).ToList();

            var rowNotes = new List<string>(notes);
            if (windows.Count != durations.Count)
            {
                rowNotes.Add($"found {windows.Count} of {durations.Count} timed runs in the log");
            }
            if (inside.Count == 0)
            {
                rowNotes.Add("no GPU samples inside the test");
            }

            int nPrompt = test.GetProperty("n_prompt").GetInt32();
            var manifestRow = manifest[config["model_file"]];

            string[] row =
            [
                runId,
                windows.Count > 0 ? Iso(windows[0].Start) : "",
                windows.Count > 0 ? Iso(windows[^1].End) : "",
                experiment,
                string.Join("; ", rowNotes).Replace(",", ""),
                imageDigest,
                Raw("build_number"),
                Raw("build_commit"),
                Path.GetFileName(Raw("model_filename")),
                Raw("n_gpu_layers"),
                Raw("flash_attn"),
                Raw("type_k"),
                Raw("type_v"),
                Raw("n_cpu_moe"),
                Raw("n_threads"),
                nPrompt > 0 ? "pp" : "tg",
                nPrompt > 0 ? Raw("n_prompt") : Raw("n_gen"),
                Raw("n_depth"),
                durations.Count.ToString(CultureInfo.InvariantCulture),
                Raw("avg_ts"),
                Raw("stddev_ts"),
                manifestRow["sha256"][..8],
                manifestRow["quant"],
                manifestRow["size_bytes"],
                inside.Count > 0 ? Text(inside.Max(s => s.MemoryUsedMib)) : "",
                inside.Count > 0 ? Text(Math.Round(inside.Average(s => s.PowerAverageW), 1)) : "",
                inside.Count > 0 ? Text(inside[0].TemperatureC) : "",
                inside.Count > 0 ? Text(inside[^1].TemperatureC) : "",
                inside.Count > 0 ? Text(Math.Round(inside.Average(s => s.SmClockMhz))) : "",
                string.Join(';', inside.Select(s => s.ClockEventReasons).Distinct()),
                nPrompt > 0 ? "" : Ceiling(Path.GetFileName(Raw("model_filename")), int.Parse(Raw("n_gpu_layers"), CultureInfo.InvariantCulture), int.Parse(Raw("n_cpu_moe"), CultureInfo.InvariantCulture)),
            ];
            results.WriteLine(string.Join(',', row));
            Console.Error.WriteLine($"  {row[15]}{row[16]} @ d{row[17]}: {row[19]} tokens/s, {inside.Count} GPU samples");
        }
    }
}
finally
{
    if (!sampler.HasExited)
    {
        sampler.Kill(entireProcessTree: true);
    }
}

Console.Error.WriteLine($"Results in {outPath}");
return 0;

(int ExitCode, string Json, List<(DateTimeOffset Time, string Text)> Log) RunLlamaBench(Dictionary<string, string> config)
{
    // Every flag is checked against docker/help/b12030/llama-bench.txt.
    // llama-bench does its own warm-up run before each test, so the runner doesn't add one.
    var info = new ProcessStartInfo("docker")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    string[] arguments =
    [
        "run", "--rm", "--pull", "never", "--gpus", "all",
        "-v", "llm-models:/models:ro",
        "--entrypoint", "/app/llama-bench",
        image,
        "-m", $"/models/{config["model_file"]}",
        "-ngl", config["n_gpu_layers"],
        "-fa", config["flash_attn"],
        "-ctk", config["cache_type_k"],
        "-ctv", config["cache_type_v"],
        "-ncmoe", config["n_cpu_moe"],
        "-t", config["threads"],
        "-p", config["n_prompt"],
        "-n", config["n_gen"],
        "-d", config["depth_tokens"],
        "-r", config["repetitions"],
        "-o", "json",
        "--progress",
    ];
    foreach (string argument in arguments)
    {
        info.ArgumentList.Add(argument);
    }

    using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start docker.");

    // Progress lines go to stderr. Each one is timestamped as it arrives; that's how the test windows are found.
    var log = new List<(DateTimeOffset Time, string Text)>();
    process.ErrorDataReceived += (_, e) =>
    {
        if (e.Data is null)
        {
            return;
        }
        lock (log)
        {
            log.Add((DateTimeOffset.Now, e.Data));
        }
        if (e.Data.StartsWith("llama-bench:", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"  {e.Data}");
        }
    };
    process.BeginErrorReadLine();
    string json = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    lock (log)
    {
        return (process.ExitCode, json, log.ToList());
    }
}

// The decode ceiling for a tg row. Empty for pp rows, because prefill isn't limited by memory bandwidth the
// same way, and for --n-cpu-moe runs, which tools/ceiling.cs doesn't cover. -ngl 99 means "all layers", so
// anything above the model's layer count is looked up as all of them.
string Ceiling(string modelFile, int gpuLayers, int cpuMoe)
{
    if (ceilings is null || cpuMoe > 0)
    {
        return "";
    }
    int layers = int.Parse(manifest[modelFile]["layers"], CultureInfo.InvariantCulture);
    return ceilings.TryGetValue((modelFile, Math.Min(gpuLayers, layers)), out string? ceiling) ? ceiling : "";
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

static string[] ReadLinesShared(string path)
{
    // The sampler keeps the file open for writing, so it has to be opened with FileShare.ReadWrite.
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
}

static int? LatestTemperature(string samplesPath)
{
    if (!File.Exists(samplesPath))
    {
        return null;
    }
    string[] lines = ReadLinesShared(samplesPath);
    if (lines.Length < 2)
    {
        return null;
    }
    // temperature_c is the 7th column of the sampler's CSV.
    return int.Parse(lines[^1].Split(',')[6], CultureInfo.InvariantCulture);
}

static List<(DateTimeOffset Time, int MemoryUsedMib, double PowerAverageW, int TemperatureC, int SmClockMhz, string ClockEventReasons)> ReadSamples(string samplesPath)
{
    // Columns: timestamp,label,memory_used_mib,memory_total_mib,power_instant_w,power_average_w,
    //          temperature_c,sm_clock_mhz,mem_clock_mhz,pstate,clock_event_reasons,gpu_utilization_pct
    return ReadLinesShared(samplesPath)
        .Skip(1)
        .Select(line => line.Split(','))
        .Where(values => values.Length == 12)
        .Select(values => (
            DateTimeOffset.Parse(values[0], CultureInfo.InvariantCulture),
            int.Parse(values[2], CultureInfo.InvariantCulture),
            double.Parse(values[5], CultureInfo.InvariantCulture),
            int.Parse(values[6], CultureInfo.InvariantCulture),
            int.Parse(values[7], CultureInfo.InvariantCulture),
            values[10]))
        .ToList();
}

static string Iso(DateTimeOffset time) => time.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);

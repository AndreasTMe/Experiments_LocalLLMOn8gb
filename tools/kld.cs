// Measures how far each quantisation drifts from Q8_0, with llama-perplexity's KL divergence, and appends one
// row per quant to a results CSV.
//
// For each model it first runs Q8_0 once to save its logits as the base, then runs Q8_0 again (a self-check that
// should give a KL divergence of about 0) and every other quant against that base.
//
// Usage: dotnet run tools/kld.cs -- --experiment <name> [--out <kld.csv>] [--ctx 512] [--chunks 100]
//        dotnet run tools/kld.cs -- --experiment <name> --parse <run_id> [--out <kld.csv>]
//
// --parse runs nothing: it rebuilds the rows of an earlier run from its logs in raw/.
//
// Needs the llm-models volume and the text at /scratch/text/wiki.test.raw in the llm-scratch volume.
// Run it from the repo root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

const string TextPath = "/scratch/text/wiki.test.raw";
const string TextName = "wikitext-2 test";
const string TextSha256 = "173c87a53759e0201f33e0ccf978e510c2042d7f2cb78229d9a50d79b9e7dd08";

// The base for each model is its Q8_0. The 8B Q8_0 doesn't fit in 8 GB with all its layers, so it runs with 30.
(string Name, string Short, string Q8GpuLayers)[] models =
[
    ("Qwen3-4B", "4b", "99"),
    ("Qwen3-8B", "8b", "30"),
];
string[] quants = ["Q8_0", "Q6_K", "Q5_K_M", "Q4_K_M"];

string[] resultColumns =
[
    "run_id", "model_file", "quant", "base_file", "text", "text_sha256_short", "ctx", "chunks",
    "ppl", "ppl_err", "ppl_base", "ppl_ratio",
    "kld_mean", "kld_mean_err", "kld_median", "kld_p99", "kld_max",
    "same_top_pct", "same_top_err", "rms_dp_pct", "n_gpu_layers", "image_digest_short",
];

string? experiment = null;
string? outPath = null;
string? parseRunId = null;
string ctx = "512";
string chunks = "100";

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
    switch (args[i])
    {
        case "--experiment": experiment = Next(); break;
        case "--out": outPath = Next(); break;
        case "--parse": parseRunId = Next(); break;
        case "--ctx": ctx = Next(); break;
        case "--chunks": chunks = Next(); break;
        default:
            Console.Error.WriteLine($"Unknown argument {args[i]}");
            return 1;
    }
}

if (experiment is null)
{
    Console.Error.WriteLine("Usage: dotnet run tools/kld.cs -- --experiment <name> [--out <kld.csv>] [--ctx 512] [--chunks 100]");
    Console.Error.WriteLine("       dotnet run tools/kld.cs -- --experiment <name> --parse <run_id> [--out <kld.csv>]");
    return 1;
}

outPath ??= Path.Combine("experiments", experiment, "results", "kld.csv");
string rawDirectory = Path.Combine(Path.GetDirectoryName(outPath) ?? ".", "raw");
Directory.CreateDirectory(rawDirectory);

var imageMatch = Regex.Match(File.ReadAllText("docker/images.md"), @"ghcr\.io/ggml-org/llama\.cpp@sha256:([0-9a-f]{64})");
if (!imageMatch.Success)
{
    Console.Error.WriteLine("No pinned llama.cpp image in docker/images.md.");
    return 1;
}
string image = imageMatch.Value;
string imageDigestShort = imageMatch.Groups[1].Value[..8];

var manifest = ReadCsv("models/models.csv").ToDictionary(row => row["file_name"]);
foreach (var model in models)
{
    foreach (string quant in quants)
    {
        if (!manifest.ContainsKey(ModelFile(model.Name, quant)))
        {
            Console.Error.WriteLine($"{ModelFile(model.Name, quant)} isn't in models/models.csv.");
            return 1;
        }
    }
}

string runId = parseRunId ?? DateTime.Now.ToString("yyyy-MM-ddTHHmmss", CultureInfo.InvariantCulture);

if (parseRunId is null)
{
    // Never pull: the image is pinned by digest and has to be there already.
    if (Docker(["image", "inspect", image]).ExitCode != 0)
    {
        Console.Error.WriteLine($"The pinned image isn't here. Pull it first: docker pull {image}");
        return 1;
    }

    var check = Docker(["run", "--rm", "--pull", "never", "-v", "llm-scratch:/scratch", "--entrypoint", "sha256sum", image, TextPath]);
    if (check.ExitCode != 0 || !check.Output.StartsWith(TextSha256, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"{TextPath} in llm-scratch is missing or isn't the expected text: {check.Output.Trim()}");
        return 1;
    }

    if (Docker(["run", "--rm", "--pull", "never", "-v", "llm-scratch:/scratch", "--entrypoint", "mkdir", image, "-p", "/scratch/logits"]).ExitCode != 0)
    {
        Console.Error.WriteLine("Couldn't create /scratch/logits in llm-scratch.");
        return 1;
    }

    var total = Stopwatch.StartNew();
    Console.Error.WriteLine($"Run {runId}");
    foreach (var model in models)
    {
        string baseFile = BaseLogits(model.Name);
        string q8File = $"/models/{ModelFile(model.Name, "Q8_0")}";

        // The base run saves the logits and prints the Q8_0 perplexity that the ratios are computed against.
        if (!RunPerplexity($"{model.Short}-q8_0-base", model.Q8GpuLayers, ["-m", q8File, "--kl-divergence-base", baseFile]))
        {
            return 1;
        }
        foreach (string quant in quants)
        {
            string gpuLayers = quant == "Q8_0" ? model.Q8GpuLayers : "99";
            string[] modelArguments = ["-m", $"/models/{ModelFile(model.Name, quant)}", "--kl-divergence-base", baseFile, "--kl-divergence"];
            if (!RunPerplexity($"{model.Short}-{quant.ToLowerInvariant()}", gpuLayers, modelArguments))
            {
                return 1;
            }
        }
    }
    Console.Error.WriteLine($"All runs took {total.Elapsed:h\\:mm\\:ss}");
}

string? missing = models
    .SelectMany(model => quants.Select(quant => $"{model.Short}-{quant.ToLowerInvariant()}").Prepend($"{model.Short}-q8_0-base"))
    .Select(LogPath)
    .FirstOrDefault(path => !File.Exists(path));
if (missing is not null)
{
    Console.Error.WriteLine($"No log at {missing}.");
    return 1;
}

bool writeHeader = !File.Exists(outPath) || new FileInfo(outPath).Length == 0;
using var results = new StreamWriter(outPath, append: true) { NewLine = "\n" };
if (writeHeader)
{
    results.WriteLine(string.Join(',', resultColumns));
}

foreach (var model in models)
{
    string baseLog = File.ReadAllText(LogPath($"{model.Short}-q8_0-base"), Encoding.UTF8);
    var final = Regex.Match(baseLog, @"Final estimate: PPL = ([0-9.]+) \+/- ([0-9.]+)");
    if (!final.Success)
    {
        Console.Error.WriteLine($"No final perplexity in {LogPath($"{model.Short}-q8_0-base")}.");
        return 1;
    }
    // The base run's own perplexity, not the "Mean PPL(base)" of the KL runs: that one is computed from the saved
    // logits, which are stored at reduced precision, and reads about 1% low.
    double pplBase = Number(final.Groups[1].Value);

    foreach (string quant in quants)
    {
        string logPath = LogPath($"{model.Short}-{quant.ToLowerInvariant()}");
        string log = File.ReadAllText(logPath, Encoding.UTF8);
        var stats = ReadStatistics(log);
        var size = Regex.Match(log, @"over (\d+) chunks, n_ctx=(\d+)");
        string[] needed = ["Mean PPL(Q)", "Mean KLD", "Median KLD", "99.0% KLD", "Maximum KLD", "Same top p", "RMS Δp"];
        if (!size.Success || needed.Any(label => !stats.ContainsKey(label)))
        {
            Console.Error.WriteLine($"{logPath} doesn't have the full statistics. Did the run finish?");
            return 1;
        }

        double ppl = Number(stats["Mean PPL(Q)"].Value);
        string[] row =
        [
            runId,
            ModelFile(model.Name, quant),
            quant,
            ModelFile(model.Name, "Q8_0"),
            TextName,
            TextSha256[..8],
            size.Groups[2].Value,
            size.Groups[1].Value,
            Round(ppl, 4),
            Round(Number(stats["Mean PPL(Q)"].Error), 4),
            final.Groups[1].Value,
            Round(ppl / pplBase, 4),
            stats["Mean KLD"].Value,
            stats["Mean KLD"].Error,
            stats["Median KLD"].Value,
            stats["99.0% KLD"].Value,
            stats["Maximum KLD"].Value,
            stats["Same top p"].Value,
            stats["Same top p"].Error,
            stats["RMS Δp"].Value,
            quant == "Q8_0" ? model.Q8GpuLayers : "99",
            imageDigestShort,
        ];
        results.WriteLine(string.Join(',', row));
        Console.Error.WriteLine($"  {row[1]}: KLD {row[12]}, same top {row[17]} %, PPL ratio {row[11]}");
    }
}

Console.Error.WriteLine($"Results in {outPath}");
return 0;

string LogPath(string name) =>
    Path.Combine(rawDirectory, $"{runId}-kld-{name}.log");

bool RunPerplexity(string name, string gpuLayers, string[] modelArguments)
{
    // Every flag is checked against docker/help/b12030/llama-perplexity.txt.
    string[] arguments =
    [
        "run", "--rm", "--pull", "never", "--gpus", "all",
        "-v", "llm-models:/models:ro",
        "-v", "llm-scratch:/scratch",
        "--entrypoint", "/app/llama-perplexity",
        image,
        "-f", TextPath,
        "-c", ctx,
        "--chunks", chunks,
        "-ngl", gpuLayers,
        "-fa", "on",
        "-t", "10",
        .. modelArguments,
    ];
    Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss} {name}");
    var run = Docker(arguments);
    File.WriteAllText(LogPath(name), run.Output, new UTF8Encoding(false));
    if (run.ExitCode != 0)
    {
        Console.Error.WriteLine($"llama-perplexity failed with exit code {run.ExitCode}. Log: {LogPath(name)}");
        return false;
    }
    return true;
}

static (int ExitCode, string Output) Docker(string[] arguments)
{
    var info = new ProcessStartInfo("docker")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };
    foreach (string argument in arguments)
    {
        info.ArgumentList.Add(argument);
    }

    using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start docker.");

    // stdout and stderr go into one log, in the order they arrive, like 2>&1.
    var output = new StringBuilder();
    void Append(object _, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            lock (output)
            {
                output.Append(e.Data).Append('\n');
            }
        }
    }
    process.OutputDataReceived += Append;
    process.ErrorDataReceived += Append;
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();

    lock (output)
    {
        return (process.ExitCode, output.ToString());
    }
}

// The lines after "====== Perplexity statistics ======" look like "Mean    KLD:   0.134937 ±   0.002469" or
// "Same top p: 86.118 ± 0.217 %". The label is everything before the first colon, with its spaces squeezed, so
// "Mean    KLD" becomes "Mean KLD". Labels are matched exactly: "Mean PPL(Q)" mustn't pick up "Mean PPL(Q)/PPL(base)".
static Dictionary<string, (string Value, string Error)> ReadStatistics(string log)
{
    var stats = new Dictionary<string, (string Value, string Error)>();
    int start = log.IndexOf("====== Perplexity statistics ======", StringComparison.Ordinal);
    if (start < 0)
    {
        return stats;
    }
    foreach (string line in log[start..].Split('\n'))
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
        {
            continue;
        }
        string label = Regex.Replace(line[..colon].Trim(), @"\s+", " ");
        string[] parts = line[(colon + 1)..].Replace("%", "").Split('±', StringSplitOptions.TrimEntries);
        stats.TryAdd(label, (parts[0], parts.Length > 1 ? parts[1] : ""));
    }
    return stats;
}

static string ModelFile(string model, string quant) =>
    $"{model}-{quant}.gguf";

string BaseLogits(string model) =>
    $"/scratch/logits/{model}-Q8_0-wiki-test-c{ctx}-{chunks}.bin";

static double Number(string text) =>
    double.Parse(text, CultureInfo.InvariantCulture);

static string Round(double value, int decimals) =>
    Math.Round(value, decimals).ToString($"F{decimals}", CultureInfo.InvariantCulture);

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

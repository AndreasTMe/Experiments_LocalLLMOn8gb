// Checks the task set, and scores a file of replies against it.
//
// validate checks that every line of the tasks file follows the format in tasks/README.md, and that the files it
// points to exist.
//
// check scores an answers file: JSON Lines with one {"id": "...", "reply": "..."} per task, where reply is the raw
// text of the model's reply. Other fields on a line are ignored. A task with no line in the file counts as wrong.
// Code replies are built and tested in the pinned .NET SDK image, never on the host.
//
// run starts llama-server in the pinned llama.cpp image with one configuration, asks it every task, stops it, and
// scores the replies like check. It saves the answers, a per-task CSV and the server log in raw/ next to the results
// CSV, and appends one summary row to the results CSV.
//
// Usage: dotnet run tools/tasks.cs -- validate [--tasks tasks/tasks.jsonl]
//        dotnet run tools/tasks.cs -- check --answers <answers.jsonl> [--tasks tasks/tasks.jsonl]
//        dotnet run tools/tasks.cs -- run --experiment <name> --model <file.gguf> [--out <tasks.csv>]
//            [--gpu-layers 99] [--ctx 4096] [--flash-attn on] [--cache-type-k f16] [--cache-type-v f16]
//            [--n-cpu-moe 0] [--threads 10] [--port 8090] [--tasks tasks/tasks.jsonl]
//
// Needs Docker and the pinned .NET SDK image for check and run, and also the pinned llama.cpp image and the
// llm-models volume for run. Run it from the repo root.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

const int SandboxTimeoutSeconds = 60;
const int BuildFailedExitCode = 100;

// The generation settings in tasks/README.md. They're the same for every task and every configuration.
const int MaxTokens = 1024;
const int Seed = 42;
const int HealthTimeoutSeconds = 120;
const int RequestTimeoutSeconds = 300;
const string SystemPrompt = "You are a careful assistant. Follow the reply format exactly.";
var formatLines = new Dictionary<string, string>
{
    ["csharp-tests"] = "Reply with one csharp code block that contains a public static class Solution with the method above. No namespace, no Main method, no top-level statements. Use only the .NET standard library.",
    ["json"]         = "Reply with only the JSON object, in one json code block.",
    ["number"]       = "You can show your working. End with a last line of the form \"Answer: <number>\", with the number only: no units, currency signs or thousands separators.",
    ["exact"]        = "You can show your working. End with a last line of the form \"Answer: <answer>\".",
};

string[] categories = ["code", "extraction", "arithmetic", "logic"];
string[] checkers = ["csharp-tests", "json", "number", "exact"];
string[] fields = ["id", "category", "checker", "prompt", "expected", "reference"];

// The project every code reply is built in. It has no package references, so it builds without a network.
const string ProjectFile = """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>disable</Nullable>
      </PropertyGroup>
    </Project>
    """;

Console.OutputEncoding = Encoding.UTF8;

string mode = args.Length > 0 ? args[0] : "";
string tasksPath = Path.Combine("tasks", "tasks.jsonl");
string? answersPath = null;
string? experiment = null;
string? modelFile = null;
string? outPath = null;
string gpuLayers = "99";
string ctx = "4096";
string flashAttn = "on";
string cacheTypeK = "f16";
string cacheTypeV = "f16";
string nCpuMoe = "0";
string threads = "10";
string port = "8090";

for (int i = 1; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
    switch (args[i])
    {
        case "--tasks": tasksPath = Next(); break;
        case "--answers": answersPath = Next(); break;
        case "--experiment": experiment = Next(); break;
        case "--model": modelFile = Next(); break;
        case "--out": outPath = Next(); break;
        case "--gpu-layers": gpuLayers = Next(); break;
        case "--ctx": ctx = Next(); break;
        case "--flash-attn": flashAttn = Next(); break;
        case "--cache-type-k": cacheTypeK = Next(); break;
        case "--cache-type-v": cacheTypeV = Next(); break;
        case "--n-cpu-moe": nCpuMoe = Next(); break;
        case "--threads": threads = Next(); break;
        case "--port": port = Next(); break;
        default:
            Console.Error.WriteLine($"Unknown argument {args[i]}");
            return 1;
    }
}

if (mode is not ("validate" or "check" or "run")
    || (mode == "check" && answersPath is null)
    || (mode == "run" && (experiment is null || modelFile is null)))
{
    Console.Error.WriteLine("Usage: dotnet run tools/tasks.cs -- validate [--tasks tasks/tasks.jsonl]");
    Console.Error.WriteLine("       dotnet run tools/tasks.cs -- check --answers <answers.jsonl> [--tasks tasks/tasks.jsonl]");
    Console.Error.WriteLine("       dotnet run tools/tasks.cs -- run --experiment <name> --model <file.gguf> [--out <tasks.csv>]");
    Console.Error.WriteLine("           [--gpu-layers 99] [--ctx 4096] [--flash-attn on] [--cache-type-k f16] [--cache-type-v f16]");
    Console.Error.WriteLine("           [--n-cpu-moe 0] [--threads 10] [--port 8090] [--tasks tasks/tasks.jsonl]");
    return 1;
}

// Paths inside a task are relative to the folder the tasks file is in.
string tasksDirectory = Path.GetDirectoryName(Path.GetFullPath(tasksPath)) ?? ".";

// ---- validate ----

var tasks = new List<JsonElement>();
var errors = new List<string>();
var ids = new HashSet<string>();
string[] taskLines = File.ReadAllLines(tasksPath, Encoding.UTF8);

for (int n = 0; n < taskLines.Length; n++)
{
    string where = $"line {n + 1}";
    if (taskLines[n].Trim().Length == 0)
    {
        errors.Add($"{where}: blank line");
        continue;
    }

    JsonElement task;
    try
    {
        task = JsonDocument.Parse(taskLines[n]).RootElement;
    }
    catch (JsonException e)
    {
        errors.Add($"{where}: not valid JSON ({e.Message})");
        continue;
    }
    if (task.ValueKind != JsonValueKind.Object)
    {
        errors.Add($"{where}: not a JSON object");
        continue;
    }

    string? id = StringField(task, "id");
    if (id is null || !Regex.IsMatch(id, "^[a-z0-9-]+$"))
    {
        errors.Add($"{where}: id must be lowercase letters, digits and -");
    }
    else
    {
        where = $"line {n + 1} ({id})";
        if (!ids.Add(id))
        {
            errors.Add($"{where}: duplicate id");
        }
    }

    foreach (var property in task.EnumerateObject())
    {
        if (!fields.Contains(property.Name))
        {
            errors.Add($"{where}: unknown field {property.Name}");
        }
    }

    string? category = StringField(task, "category");
    if (category is null || !categories.Contains(category))
    {
        errors.Add($"{where}: category must be one of {string.Join(", ", categories)}");
    }

    string? prompt = StringField(task, "prompt");
    if (prompt is null || prompt.Trim().Length == 0)
    {
        errors.Add($"{where}: prompt must be a non-empty string");
    }

    string? checker = StringField(task, "checker");
    if (checker is null || !checkers.Contains(checker))
    {
        errors.Add($"{where}: checker must be one of {string.Join(", ", checkers)}");
        continue;
    }

    if (!task.TryGetProperty("expected", out var expected))
    {
        errors.Add($"{where}: expected is missing");
        continue;
    }

    bool expectedOk = checker switch
    {
        "csharp-tests" => expected.ValueKind == JsonValueKind.String,
        "json" => expected.ValueKind == JsonValueKind.Object,
        "number" => expected.ValueKind == JsonValueKind.Number && expected.TryGetDecimal(out _),
        "exact" => IsAnswerText(expected)
            || (expected.ValueKind == JsonValueKind.Array
                && expected.GetArrayLength() > 0
                && expected.EnumerateArray().All(IsAnswerText)),
        _ => false,
    };
    if (!expectedOk)
    {
        string wanted = checker switch
        {
            "csharp-tests" => "the path of the tests file",
            "json" => "a JSON object",
            "number" => "a number",
            _ => "a non-empty string, or a non-empty list of them",
        };
        errors.Add($"{where}: expected must be {wanted} for the {checker} checker");
    }

    bool hasReference = task.TryGetProperty("reference", out var reference);
    if (checker == "csharp-tests")
    {
        if (expectedOk && !File.Exists(Path.Combine(tasksDirectory, expected.GetString()!)))
        {
            errors.Add($"{where}: tests file {expected.GetString()} not found");
        }
        if (!hasReference || reference.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{where}: reference must be the path of a correct answer");
        }
        else if (!File.Exists(Path.Combine(tasksDirectory, reference.GetString()!)))
        {
            errors.Add($"{where}: reference file {reference.GetString()} not found");
        }
    }
    else if (hasReference)
    {
        errors.Add($"{where}: reference is only for csharp-tests tasks");
    }

    tasks.Add(task);
}

if (errors.Count > 0)
{
    foreach (string error in errors)
    {
        Console.Error.WriteLine(error);
    }
    Console.Error.WriteLine($"{tasksPath}: {errors.Count} problem(s).");
    return 1;
}

if (mode == "validate")
{
    var counts = tasks
        .GroupBy(t => t.GetProperty("category").GetString())
        .Select(g => $"{g.Count()} {g.Key}");
    Console.WriteLine($"{tasksPath}: {tasks.Count} tasks, all valid ({string.Join(", ", counts)}).");
    return 0;
}

// ---- check ----

// Code replies run in the .NET SDK image, so it has to be here before anything starts.
string? sdkImage = null;
if (tasks.Any(t => t.GetProperty("checker").GetString() == "csharp-tests"))
{
    sdkImage = PinnedImage(@"mcr\.microsoft\.com/dotnet/sdk@sha256:[0-9a-f]{64}", ".NET SDK");
    if (sdkImage is null)
    {
        return 1;
    }
}

if (mode == "check")
{
    var replies = new Dictionary<string, string>();
    string[] answerLines = File.ReadAllLines(answersPath!, Encoding.UTF8);
    for (int n = 0; n < answerLines.Length; n++)
    {
        if (answerLines[n].Trim().Length == 0)
        {
            continue;
        }
        var answer = JsonDocument.Parse(answerLines[n]).RootElement;
        string? id = StringField(answer, "id");
        string? reply = StringField(answer, "reply");
        if (id is null || reply is null)
        {
            Console.Error.WriteLine($"{answersPath} line {n + 1}: needs a string id and a string reply.");
            return 1;
        }
        if (!ids.Contains(id))
        {
            Console.Error.WriteLine($"{answersPath} line {n + 1}: no task with id {id}.");
            return 1;
        }
        if (!replies.TryAdd(id, reply))
        {
            Console.Error.WriteLine($"{answersPath} line {n + 1}: a second reply for {id}.");
            return 1;
        }
    }

    PrintScores(ScoreAll(replies));
    return 0;
}

// ---- run ----

outPath ??= Path.Combine("experiments", experiment!, "results", "tasks.csv");
string rawDirectory = Path.Combine(Path.GetDirectoryName(outPath) ?? ".", "raw");
Directory.CreateDirectory(rawDirectory);

var modelRow = ReadCsv(Path.Combine("models", "models.csv")).FirstOrDefault(row => row["file_name"] == modelFile);
if (modelRow is null)
{
    Console.Error.WriteLine($"{modelFile} isn't in models/models.csv.");
    return 1;
}

string? llamaImage = PinnedImage(@"ghcr\.io/ggml-org/llama\.cpp@sha256:[0-9a-f]{64}", "llama.cpp");
if (llamaImage is null)
{
    return 1;
}

string runId = $"{DateTime.Now:yyyy-MM-ddTHHmmss}-{Path.GetFileNameWithoutExtension(modelFile)}";
string serverLogPath = Path.Combine(rawDirectory, $"{runId}-server.log");
string serverName = "tasks-server-" + Guid.NewGuid().ToString("N");
string baseUrl = $"http://127.0.0.1:{port}";
Console.WriteLine($"Run {runId}");

// Every flag is checked against docker/help/b12030/llama-server.txt.
// -fit off: the configuration is used as given, so a run fails rather than quietly getting fewer GPU layers.
// --no-cache-prompt: every request is processed from scratch, so a reply doesn't depend on the task before it.
// --reasoning-format none: anything the model thinks stays in the reply, where the checkers remove it.
string[] serverArguments =
[
    "run", "--rm", "--pull", "never", "--name", serverName, "--gpus", "all",
    "-v", "llm-models:/models:ro",
    "-p", $"127.0.0.1:{port}:8080",
    "--entrypoint", "/app/llama-server",
    llamaImage,
    "-m", $"/models/{modelFile}",
    "-ngl", gpuLayers,
    "-fa", flashAttn,
    "-c", ctx,
    "-ctk", cacheTypeK,
    "-ctv", cacheTypeV,
    "-ncmoe", nCpuMoe,
    "-t", threads,
    "-fit", "off",
    "-np", "1",
    "--temp", "0",
    "--seed", Seed.ToString(CultureInfo.InvariantCulture),
    "--reasoning", "off",
    "--reasoning-format", "none",
    "--no-cache-prompt",
    "--no-webui",
    "--host", "0.0.0.0",
    "--port", "8080",
];

var serverInfo = new ProcessStartInfo("docker")
{
    UseShellExecute        = false,
    RedirectStandardOutput = true,
    RedirectStandardError  = true,
    StandardOutputEncoding = Encoding.UTF8,
    StandardErrorEncoding  = Encoding.UTF8,
};
foreach (string argument in serverArguments)
{
    serverInfo.ArgumentList.Add(argument);
}

var answers = new Dictionary<string, Answer>();
var started = DateTimeOffset.Now;
using (var serverLog = new StreamWriter(serverLogPath) { AutoFlush = true })
using (var server = Process.Start(serverInfo) ?? throw new InvalidOperationException("Couldn't start docker."))
{
    void Log(object _, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            lock (serverLog)
            {
                serverLog.WriteLine(e.Data);
            }
        }
    }
    server.OutputDataReceived += Log;
    server.ErrorDataReceived += Log;
    server.BeginOutputReadLine();
    server.BeginErrorReadLine();
    Console.CancelKeyPress += (_, _) => Docker(["stop", serverName]);

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };

        // The health endpoint answers 503 while the model loads and 200 once it's ready.
        Console.WriteLine($"Starting llama-server with {modelFile}");
        var loading = Stopwatch.StartNew();
        while (true)
        {
            if (server.HasExited)
            {
                throw new InvalidOperationException($"llama-server stopped while loading. See {serverLogPath}.");
            }
            if (loading.Elapsed > TimeSpan.FromSeconds(HealthTimeoutSeconds))
            {
                throw new TimeoutException($"llama-server wasn't ready after {HealthTimeoutSeconds} s. See {serverLogPath}.");
            }
            try
            {
                using var healthTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var health = await http.GetAsync($"{baseUrl}/health", healthTimeout.Token);
                if (health.IsSuccessStatusCode)
                {
                    break;
                }
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
            {
                // Not listening yet.
            }
            await Task.Delay(1000);
        }
        Console.WriteLine($"Ready after {loading.Elapsed.TotalSeconds:0} s");

        int idWidth = tasks.Max(t => t.GetProperty("id").GetString()!.Length);
        foreach (var task in tasks)
        {
            string id = task.GetProperty("id").GetString()!;
            string checker = task.GetProperty("checker").GetString()!;
            string userMessage = task.GetProperty("prompt").GetString()! + "\n\n" + formatLines[checker];

            var timer = Stopwatch.StartNew();
            using var content = new StringContent(ChatRequest(SystemPrompt, userMessage), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"{baseUrl}/v1/chat/completions", content);
            string body = await response.Content.ReadAsStringAsync();
            timer.Stop();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{id}: llama-server answered {(int)response.StatusCode}: {body}");
            }

            var root = JsonDocument.Parse(body).RootElement;
            var choice = root.GetProperty("choices")[0];
            var usage = root.GetProperty("usage");
            double tokensPerSecond = root.TryGetProperty("timings", out var timings)
                && timings.TryGetProperty("predicted_per_second", out var perSecond)
                    ? perSecond.GetDouble()
                    : 0;
            var answer = new Answer(
                choice.GetProperty("message").GetProperty("content").GetString() ?? "",
                choice.GetProperty("finish_reason").GetString() ?? "",
                usage.GetProperty("prompt_tokens").GetInt32(),
                usage.GetProperty("completion_tokens").GetInt32(),
                timer.Elapsed.TotalSeconds,
                tokensPerSecond);
            answers[id] = answer;
            Console.WriteLine($"  {id.PadRight(idWidth)}  {answer.CompletionTokens,5} tokens  {answer.Seconds,6:0.0} s  {answer.FinishReason}");
        }
    }
    catch (Exception e) when (e is InvalidOperationException or TimeoutException or HttpRequestException
        or TaskCanceledException or JsonException or KeyNotFoundException)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
    finally
    {
        // The server stops before the code checks start, so they don't share the machine with it.
        Docker(["stop", serverName]);
        server.WaitForExit();
    }
}
var ended = DateTimeOffset.Now;

string answersFile = Path.Combine(rawDirectory, $"{runId}-answers.jsonl");
using (var writer = new StreamWriter(answersFile, append: false, new UTF8Encoding(false)))
{
    foreach (var (id, answer) in answers)
    {
        writer.Write(JsonLine(w =>
        {
            w.WriteString("id", id);
            w.WriteString("reply", answer.Reply);
            w.WriteString("finish_reason", answer.FinishReason);
            w.WriteNumber("prompt_tokens", answer.PromptTokens);
            w.WriteNumber("completion_tokens", answer.CompletionTokens);
            w.WriteNumber("seconds", Math.Round(answer.Seconds, 3));
        }));
        writer.Write('\n');
    }
}

Console.WriteLine();
var results = ScoreAll(answers.ToDictionary(p => p.Key, p => p.Value.Reply));
PrintScores(results);

// One row per task, next to the answers.
string tasksCsv = Path.Combine(rawDirectory, $"{runId}-tasks.csv");
var taskRows = new List<string>
{
    "id,category,checker,right,finish_reason,prompt_tokens,completion_tokens,seconds,tokens_per_s,detail",
};
foreach (var result in results)
{
    var answer = answers[result.Id];
    taskRows.Add(string.Join(',',
        result.Id, result.Category, result.Checker, result.Right ? "1" : "0", answer.FinishReason,
        Text(answer.PromptTokens), Text(answer.CompletionTokens), Text(Math.Round(answer.Seconds, 3)),
        Text(Math.Round(answer.TokensPerSecond, 2)), Csv(result.Detail)));
}
File.WriteAllLines(tasksCsv, taskRows);

// One summary row per run. The header is written only when the file is new or empty.
string Percent(string? category)
{
    var selected = results.Where(r => category is null || r.Category == category).ToList();
    return selected.Count == 0
        ? ""
        : (100.0 * selected.Count(r => r.Right) / selected.Count).ToString("0.0", CultureInfo.InvariantCulture);
}
string[] summaryColumns =
[
    "run_id", "started_at", "ended_at", "model_file", "quant", "n_gpu_layers", "ctx", "flash_attn",
    "cache_type_k", "cache_type_v", "n_cpu_moe", "threads", "tasks", "right", "score_pct",
    "code_pct", "extraction_pct", "arithmetic_pct", "logic_pct", "cut_off", "completion_tokens", "generation_s",
    "tasks_sha256_short", "image_digest_short",
];
string[] summary =
[
    runId, Iso(started), Iso(ended), modelFile!, modelRow["quant"], gpuLayers, ctx, flashAttn,
    cacheTypeK, cacheTypeV, nCpuMoe, threads, Text(results.Count), Text(results.Count(r => r.Right)), Percent(null),
    Percent("code"), Percent("extraction"), Percent("arithmetic"), Percent("logic"),
    Text(answers.Values.Count(a => a.FinishReason == "length")),
    Text(answers.Values.Sum(a => a.CompletionTokens)),
    Text(Math.Round(answers.Values.Sum(a => a.Seconds), 1)),
    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(tasksPath)))[..8],
    llamaImage[(llamaImage.IndexOf(':') + 1)..][..8],
];
bool newFile = !File.Exists(outPath) || new FileInfo(outPath).Length == 0;
using (var writer = new StreamWriter(outPath, append: true))
{
    if (newFile)
    {
        writer.WriteLine(string.Join(',', summaryColumns));
    }
    writer.WriteLine(string.Join(',', summary));
}

Console.WriteLine();
Console.WriteLine($"Answers:  {answersFile}");
Console.WriteLine($"Per task: {tasksCsv}");
Console.WriteLine($"Summary:  {outPath}");
return 0;

// ---- scoring ----

// Scores every task and prints one line per task as it goes. A task with no reply is wrong.
List<Result> ScoreAll(Dictionary<string, string> replies)
{
    int idWidth = tasks.Max(t => t.GetProperty("id").GetString()!.Length);
    var results = new List<Result>();
    foreach (var task in tasks)
    {
        string id = task.GetProperty("id").GetString()!;
        string category = task.GetProperty("category").GetString()!;
        string checker = task.GetProperty("checker").GetString()!;
        var expected = task.GetProperty("expected");

        bool right = false;
        string detail = "no reply";
        if (replies.TryGetValue(id, out string? rawReply))
        {
            string reply = StripThinking(rawReply);
            (right, detail) = checker switch
            {
                "csharp-tests" => CheckCode(reply, Path.Combine(tasksDirectory, expected.GetString()!)),
                "json" => CheckJson(reply, expected),
                "number" => CheckNumber(reply, expected.GetDecimal()),
                _ => CheckExact(reply, expected),
            };
        }

        results.Add(new Result(id, category, checker, right, detail));
        Console.WriteLine($"{id.PadRight(idWidth)}  {checker,-12}  {(right ? "right" : "wrong")}  {detail}");
    }
    return results;
}

void PrintScores(List<Result> results)
{
    Console.WriteLine();
    foreach (string category in categories.Where(c => results.Any(r => r.Category == c)))
    {
        var inCategory = results.Where(r => r.Category == category).ToList();
        Console.WriteLine($"{category,-11} {Score(inCategory.Count(r => r.Right), inCategory.Count)}");
    }
    Console.WriteLine($"{"all",-11} {Score(results.Count(r => r.Right), results.Count)}");
}

// ---- checkers ----

// Builds the reply next to the tests in a throwaway container, with no network, and runs the tests.
(bool Right, string Detail) CheckCode(string reply, string testsPath)
{
    string? code = FirstCodeBlock(reply);
    if (code is null)
    {
        return (false, "no code block");
    }

    string workDirectory = Path.Combine(Path.GetTempPath(), "tasks-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workDirectory);
    try
    {
        File.WriteAllText(Path.Combine(workDirectory, "task.csproj"), ProjectFile);
        File.Copy(testsPath, Path.Combine(workDirectory, "Program.cs"));
        File.WriteAllText(Path.Combine(workDirectory, "Solution.cs"), code);

        // The project is mounted read-only and copied into a tmpfs, because the root file system is read-only too.
        // A build failure exits with BuildFailedExitCode, so it can be told apart from failed tests.
        string script =
            "cp -r /src /tmp/w && cd /tmp/w"
            + " && { dotnet build -nologo -v q -o out > build.log 2>&1 || { cat build.log; exit " + BuildFailedExitCode + "; }; }"
            + " && dotnet out/task.dll";
        string containerName = "tasks-" + Guid.NewGuid().ToString("N");
        (int exitCode, string output) = Docker(
            [
                "run", "--rm", "--name", containerName,
                "--network", "none", "--memory", "1g", "--cpus", "2", "--pids-limit", "256",
                "--read-only", "--tmpfs", "/tmp:exec,size=512m",
                "-e", "HOME=/tmp", "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "-e", "DOTNET_NOLOGO=1",
                "-v", $"{workDirectory}:/src:ro",
                sdkImage!, "sh", "-c", script,
            ],
            SandboxTimeoutSeconds,
            containerName);

        return exitCode switch
        {
            0 => (true, ""),
            -1 => (false, $"timed out after {SandboxTimeoutSeconds} s"),
            BuildFailedExitCode => (false, "build failed: " + FirstLine(output, " error ")),
            _ => (false, "tests failed: " + FirstLine(output, "")),
        };
    }
    finally
    {
        Directory.Delete(workDirectory, recursive: true);
    }
}

static (bool Right, string Detail) CheckJson(string reply, JsonElement expected)
{
    string text = (FirstCodeBlock(reply) ?? reply).Trim();
    JsonElement actual;
    try
    {
        actual = JsonDocument.Parse(text).RootElement;
    }
    catch (JsonException)
    {
        return (false, "not valid JSON");
    }
    string? difference = JsonDifference(expected, actual, "");
    return difference is null ? (true, "") : (false, difference);
}

static (bool Right, string Detail) CheckNumber(string reply, decimal expected)
{
    string? answer = AnswerLine(reply);
    if (answer is null)
    {
        return (false, "no Answer: line");
    }
    string text = answer.Replace(" ", "").Replace("**", "").TrimEnd('.');
    if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
    {
        return (false, $"not a plain number: {answer}");
    }
    return number == expected ? (true, "") : (false, $"got {text}");
}

static (bool Right, string Detail) CheckExact(string reply, JsonElement expected)
{
    string? answer = AnswerLine(reply);
    if (answer is null)
    {
        return (false, "no Answer: line");
    }
    string text = answer.Replace("**", "").Trim().TrimEnd('.').Trim().Trim('"', '\'', '`').Trim();
    IEnumerable<string> accepted = expected.ValueKind == JsonValueKind.Array
        ? expected.EnumerateArray().Select(e => e.GetString()!)
        : [expected.GetString()!];
    return accepted.Any(a => string.Equals(a, text, StringComparison.OrdinalIgnoreCase))
        ? (true, "")
        : (false, $"got {text}");
}

// ---- helpers ----

// Compares by value: key order doesn't matter, array order does, numbers by value, strings exactly.
// Returns where the first difference is, or null when the two are equal.
static string? JsonDifference(JsonElement expected, JsonElement actual, string path)
{
    string at = path.Length == 0 ? "the reply" : path;
    if (expected.ValueKind != actual.ValueKind)
    {
        return $"{at}: expected {expected.GetRawText()}, got {actual.GetRawText()}";
    }

    switch (expected.ValueKind)
    {
        case JsonValueKind.Object:
            var actualNames = actual.EnumerateObject().Select(p => p.Name).ToList();
            if (actualNames.Count != actualNames.Distinct().Count())
            {
                return $"{at}: a key appears twice";
            }
            foreach (var property in expected.EnumerateObject())
            {
                if (!actual.TryGetProperty(property.Name, out var actualValue))
                {
                    return $"{path}.{property.Name}: missing".TrimStart('.');
                }
                string? difference = JsonDifference(property.Value, actualValue, $"{path}.{property.Name}".TrimStart('.'));
                if (difference is not null)
                {
                    return difference;
                }
            }
            string? extra = actualNames.FirstOrDefault(name => !expected.TryGetProperty(name, out _));
            return extra is null ? null : $"{path}.{extra}: not expected".TrimStart('.');

        case JsonValueKind.Array:
            if (expected.GetArrayLength() != actual.GetArrayLength())
            {
                return $"{at}: expected {expected.GetArrayLength()} items, got {actual.GetArrayLength()}";
            }
            for (int i = 0; i < expected.GetArrayLength(); i++)
            {
                string? difference = JsonDifference(expected[i], actual[i], $"{path}[{i}]");
                if (difference is not null)
                {
                    return difference;
                }
            }
            return null;

        case JsonValueKind.Number:
            bool same = expected.TryGetDecimal(out decimal a) && actual.TryGetDecimal(out decimal b)
                ? a == b
                : expected.GetRawText() == actual.GetRawText();
            return same ? null : $"{at}: expected {expected.GetRawText()}, got {actual.GetRawText()}";

        case JsonValueKind.String:
            return expected.GetString() == actual.GetString()
                ? null
                : $"{at}: expected {expected.GetRawText()}, got {actual.GetRawText()}";

        default:
            // true, false and null: the kinds are already the same.
            return null;
    }
}

// A model can think even with thinking off. Its <think>…</think> block isn't part of the answer.
static string StripThinking(string reply) =>
    Regex.Replace(reply, @"<think>.*?</think>", "", RegexOptions.Singleline);

// The contents of the first ``` code block, or null if there's no complete one.
static string? FirstCodeBlock(string reply)
{
    var match = Regex.Match(reply, @"```[^\n]*\n(.*?)```", RegexOptions.Singleline);
    return match.Success ? match.Groups[1].Value : null;
}

// What follows "Answer:" on the last line that starts with it. "**" around the whole line is allowed.
static string? AnswerLine(string reply)
{
    string? line = reply
        .Split('\n')
        .Select(l => l.Trim().TrimStart('*').TrimStart())
        .LastOrDefault(l => l.StartsWith("Answer:", StringComparison.Ordinal));
    return line?["Answer:".Length..].Trim();
}

static bool IsAnswerText(JsonElement element) =>
    element.ValueKind == JsonValueKind.String && element.GetString()!.Trim().Length > 0;

static string? StringField(JsonElement element, string name) =>
    element.ValueKind == JsonValueKind.Object
    && element.TryGetProperty(name, out var value)
    && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;

// The first line of the output that contains the marker, so a failure has a short reason next to it.
static string FirstLine(string output, string marker) =>
    output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && l.Contains(marker, StringComparison.Ordinal)) ?? "";

static string Score(int right, int total) =>
    $"{right,3} of {total,3}  {(total == 0 ? 0 : 100.0 * right / total).ToString("0.0", CultureInfo.InvariantCulture),5} %";

// The image is always started by digest. docker/images.md is the one place the digest is written down.
// The tool never pulls, so it stops here if the pinned image isn't on this machine.
static string? PinnedImage(string pattern, string name)
{
    string image = Regex.Match(File.ReadAllText("docker/images.md"), pattern).Value;
    if (image.Length == 0)
    {
        Console.Error.WriteLine($"No {name} image digest found in docker/images.md.");
        return null;
    }
    if (Docker(["image", "inspect", image]).ExitCode != 0)
    {
        Console.Error.WriteLine($"The image {image} isn't on this machine.");
        return null;
    }
    return image;
}

// The body of one chat request, with the generation settings from tasks/README.md.
static string ChatRequest(string system, string user) => JsonLine(w =>
{
    w.WriteStartArray("messages");
    w.WriteStartObject();
    w.WriteString("role", "system");
    w.WriteString("content", system);
    w.WriteEndObject();
    w.WriteStartObject();
    w.WriteString("role", "user");
    w.WriteString("content", user);
    w.WriteEndObject();
    w.WriteEndArray();
    w.WriteNumber("temperature", 0);
    w.WriteNumber("max_tokens", MaxTokens);
    w.WriteNumber("seed", Seed);
});

// One JSON object on one line. Non-ASCII text is written as it is, so the answers file stays readable.
static string JsonLine(Action<Utf8JsonWriter> write)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
    {
        writer.WriteStartObject();
        write(writer);
        writer.WriteEndObject();
    }
    return Encoding.UTF8.GetString(stream.ToArray());
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

// The detail column can hold commas and quotes, so it's quoted the CSV way when it needs to be.
static string Csv(string value) =>
    value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);

static string Iso(DateTimeOffset time) => time.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

// Runs docker and returns its exit code and output. With a timeout, the container is killed when the time is up,
// and the exit code is -1.
static (int ExitCode, string Output) Docker(string[] arguments, int timeoutSeconds = 0, string? containerName = null)
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

    // stdout and stderr go into one string, in the order they arrive, like 2>&1.
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

    if (timeoutSeconds > 0 && !process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
    {
        if (containerName is not null)
        {
            Docker(["kill", containerName]);
        }
        process.WaitForExit();
        return (-1, "");
    }
    process.WaitForExit();

    lock (output)
    {
        return (process.ExitCode, output.ToString());
    }
}

// One reply from the server, with what it cost.
record Answer(string Reply, string FinishReason, int PromptTokens, int CompletionTokens, double Seconds, double TokensPerSecond);

// One scored task.
record Result(string Id, string Category, string Checker, bool Right, string Detail);

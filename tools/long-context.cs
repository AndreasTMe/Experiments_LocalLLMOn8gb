// Builds a long-context tasks file: a long text with made-up facts planted in it, and one task per fact that asks
// about it. The file has the format in tasks/README.md, so tools/tasks.cs runs and scores it like the task set.
//
// The text is the start of the source, cut at a line break so that the text with the facts in it is at most
// ctx - 1024 - 256 tokens long: 1024 for the reply and 256 for the system prompt, the chat template, the question and
// the reply format line. Tokens are counted with llama-tokenize in the pinned llama.cpp image, on the CPU. Each fact
// is its own line, put in before the line at its position, a share of the text's length in characters.
//
// Usage: dotnet run tools/long-context.cs -- --ctx <tokens> --out <tasks.jsonl>
//            [--model Qwen3-8B-Q4_K_M.gguf] [--facts tasks/long-context/facts.jsonl]
//            [--source staging/wikitext-2-raw/wiki.test.raw]
//
// Needs Docker, the pinned llama.cpp image and the llm-models volume. Run it from the repo root. The output holds
// the source text, so it belongs in staging/, which isn't committed.

using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

const int ReplyTokens = 1024;
const int OverheadTokens = 256;
// A cut is good enough when the text is at most this many tokens short of the target.
const int ToleranceTokens = 300;
const int MaxAttempts = 8;
const string Instruction = "Read the text below, then answer the question after it. The answer is stated in the text.";

Console.OutputEncoding = Encoding.UTF8;

int? ctx = null;
string? outPath = null;
string modelFile = "Qwen3-8B-Q4_K_M.gguf";
string factsPath = Path.Combine("tasks", "long-context", "facts.jsonl");
string sourcePath = Path.Combine("staging", "wikitext-2-raw", "wiki.test.raw");

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
    switch (args[i])
    {
        case "--ctx": ctx = int.Parse(Next()); break;
        case "--out": outPath = Next(); break;
        case "--model": modelFile = Next(); break;
        case "--facts": factsPath = Next(); break;
        case "--source": sourcePath = Next(); break;
        default:
            Console.Error.WriteLine($"Unknown argument {args[i]}");
            return 1;
    }
}

if (ctx is null || outPath is null)
{
    Console.Error.WriteLine("Usage: dotnet run tools/long-context.cs -- --ctx <tokens> --out <tasks.jsonl>");
    Console.Error.WriteLine("           [--model Qwen3-8B-Q4_K_M.gguf] [--facts tasks/long-context/facts.jsonl]");
    Console.Error.WriteLine("           [--source staging/wikitext-2-raw/wiki.test.raw]");
    return 1;
}

string image = Regex.Match(File.ReadAllText("docker/images.md"), @"ghcr\.io/ggml-org/llama\.cpp@sha256:[0-9a-f]{64}").Value;
if (image.Length == 0)
{
    Console.Error.WriteLine("No llama.cpp image digest found in docker/images.md.");
    return 1;
}

var facts = File.ReadAllLines(factsPath, Encoding.UTF8)
    .Where(line => line.Trim().Length > 0)
    .Select(line => JsonDocument.Parse(line).RootElement)
    .ToList();

// Blank lines carry nothing, so only lines with text are kept.
string[] lines = File.ReadAllLines(sourcePath, Encoding.UTF8).Where(line => line.Trim().Length > 0).ToArray();
long[] charsBefore = new long[lines.Length + 1];
for (int i = 0; i < lines.Length; i++)
{
    charsBefore[i + 1] = charsBefore[i] + lines[i].Length + 1;
}

int target = ctx.Value - ReplyTokens - OverheadTokens;
Console.WriteLine($"Target: {target} tokens of text for ctx {ctx}.");

// Start from about 4 characters per token, then scale by what each count says.
int lineCount = LinesForChars(target * 4L);
string? text = null;
int tokens = 0;
int textLines = 0;
for (int attempt = 1; attempt <= MaxAttempts; attempt++)
{
    string candidate = Build(lineCount);
    int count = CountTokens(candidate);
    Console.WriteLine($"  {lineCount} lines, {candidate.Length} characters: {count} tokens");
    if (count <= target)
    {
        text = candidate;
        tokens = count;
        textLines = lineCount;
        if (target - count <= ToleranceTokens || lineCount == lines.Length)
        {
            break;
        }
    }
    int next = LinesForChars((long)(charsBefore[lineCount] * (double)target / count * 0.995));
    if (next == lineCount)
    {
        next = count > target ? lineCount - 1 : lineCount + 1;
    }
    lineCount = next;
}

if (text is null)
{
    Console.Error.WriteLine($"No cut of the text fitted in {target} tokens after {MaxAttempts} attempts.");
    return 1;
}

var options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var output = new StringBuilder();
foreach (var fact in facts)
{
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer, options))
    {
        writer.WriteStartObject();
        writer.WriteString("id", fact.GetProperty("id").GetString());
        writer.WriteString("category", "extraction");
        writer.WriteString("checker", fact.GetProperty("checker").GetString());
        writer.WriteString("prompt", $"{Instruction}\n\nText:\n{text}\n\nQuestion: {fact.GetProperty("question").GetString()}");
        writer.WritePropertyName("expected");
        fact.GetProperty("expected").WriteTo(writer);
        writer.WriteEndObject();
    }
    output.Append(Encoding.UTF8.GetString(buffer.ToArray())).Append('\n');
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(outPath, output.ToString(), new UTF8Encoding(false));
Console.WriteLine($"{outPath}: {facts.Count} tasks, {tokens} tokens of text from the first {textLines} lines.");
return 0;

// The most lines whose characters, line breaks included, add up to at most chars.
int LinesForChars(long chars)
{
    int count = Array.BinarySearch(charsBefore, chars);
    count = count >= 0 ? count : ~count - 1;
    return Math.Clamp(count, 1, lines.Length);
}

// The first count lines, with each fact put in before the line at its position.
string Build(int count)
{
    var kept = lines.Take(count).ToList();
    long total = charsBefore[count];
    foreach (var fact in facts.OrderByDescending(f => f.GetProperty("position").GetDouble()))
    {
        long at = (long)(total * fact.GetProperty("position").GetDouble());
        int index = Array.BinarySearch(charsBefore, 0, count + 1, at);
        index = index >= 0 ? index : ~index;
        kept.Insert(Math.Min(index, kept.Count), " " + fact.GetProperty("fact").GetString() + " ");
    }
    return string.Join("\n", kept);
}

int CountTokens(string candidate)
{
    var start = new ProcessStartInfo("docker")
    {
        RedirectStandardInput  = true,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
    };
    foreach (string arg in (string[])[
        "run", "--rm", "-i", "--pull", "never",
        "-v", "llm-models:/models:ro",
        "--entrypoint", "/app/llama-tokenize",
        image,
        "-m", $"/models/{modelFile}",
        "--stdin", "--ids", "--show-count", "--log-disable"])
    {
        start.ArgumentList.Add(arg);
    }
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    using (var stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
    {
        stdin.Write(candidate);
    }
    process.WaitForExit();
    var match = Regex.Match(stdout.Result + stderr.Result, @"Total number of tokens:\s*(\d+)");
    if (process.ExitCode != 0 || !match.Success)
    {
        throw new InvalidOperationException($"llama-tokenize failed ({process.ExitCode}): {stderr.Result.Trim()}");
    }
    return int.Parse(match.Groups[1].Value);
}

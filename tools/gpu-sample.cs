// Samples the GPU with Windows nvidia-smi and appends one CSV row per sample.
//
// Usage:
//   dotnet run tools/gpu-sample.cs -- --label <label> --out <file.csv> [--interval-ms 250] [--duration-s N]
//
// Runs until Ctrl+C, or for --duration-s seconds. Every row is flushed as it's written,
// so the CSV is complete whenever the tool stops. The header is written only when the file is new or empty.

using System.Diagnostics;
using System.Globalization;

// nvidia-smi query fields, checked against `nvidia-smi --help-query-gpu` (driver 617.42).
// power.draw.average is the 1-second average; power.draw.instant is the last instant reading.
// clocks_event_reasons.active is a bitmask: 0x1 means idle, 0x0 means nothing is holding the clocks back.
string[] fields =
[
    "timestamp",
    "memory.used",
    "memory.total",
    "power.draw.instant",
    "power.draw.average",
    "temperature.gpu",
    "clocks.sm",
    "clocks.mem",
    "pstate",
    "clocks_event_reasons.active",
    "utilization.gpu",
];

string[] columns =
[
    "timestamp",
    "label",
    "memory_used_mib",
    "memory_total_mib",
    "power_instant_w",
    "power_average_w",
    "temperature_c",
    "sm_clock_mhz",
    "mem_clock_mhz",
    "pstate",
    "clock_event_reasons",
    "gpu_utilization_pct",
];

string? label = null;
string? outPath = null;
int intervalMs = 250;
int? durationS = null;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

    switch (args[i])
    {
        case "--label": label = Next(); break;
        case "--out": outPath = Next(); break;
        case "--interval-ms": intervalMs = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--duration-s": durationS = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        default: throw new ArgumentException($"Unknown argument: {args[i]}");
    }
}

if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(outPath))
{
    Console.Error.WriteLine("Usage: dotnet run tools/gpu-sample.cs -- --label <label> --out <file.csv> [--interval-ms 250] [--duration-s N]");
    return 1;
}

if (label.Contains(',') || label.Contains('"'))
{
    Console.Error.WriteLine("The label can't contain commas or quotes.");
    return 1;
}

if (intervalMs < 100)
{
    Console.Error.WriteLine("--interval-ms must be at least 100.");
    return 1;
}

string? directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
if (directory is not null)
{
    Directory.CreateDirectory(directory);
}

bool writeHeader = !File.Exists(outPath) || new FileInfo(outPath).Length == 0;

using var writer = new StreamWriter(outPath, append: true) { AutoFlush = true, NewLine = "\n" };
if (writeHeader)
{
    writer.WriteLine(string.Join(',', columns));
}

var startInfo = new ProcessStartInfo("nvidia-smi")
{
    RedirectStandardOutput = true,
    UseShellExecute = false,
};
startInfo.ArgumentList.Add($"--query-gpu={string.Join(',', fields)}");
startInfo.ArgumentList.Add("--format=csv,noheader,nounits");
startInfo.ArgumentList.Add($"--loop-ms={intervalMs}");

using var nvidiaSmi = Process.Start(startInfo) ?? throw new InvalidOperationException("Couldn't start nvidia-smi.");

void Stop()
{
    try
    {
        if (!nvidiaSmi.HasExited)
        {
            nvidiaSmi.Kill();
        }
    }
    catch (InvalidOperationException)
    {
        // Already exited.
    }
}

Console.CancelKeyPress += (_, e) =>
{
    // Keep this process alive long enough to finish the last row and close the file.
    e.Cancel = true;
    Stop();
};

if (durationS is int seconds)
{
    _ = Task.Delay(TimeSpan.FromSeconds(seconds)).ContinueWith(_ => Stop());
}

Console.Error.WriteLine($"Sampling every {intervalMs} ms into {outPath} with label '{label}'. Ctrl+C to stop.");

int rows = 0;
string? line;
while ((line = nvidiaSmi.StandardOutput.ReadLine()) is not null)
{
    string[] values = line.Split(',', StringSplitOptions.TrimEntries);
    if (values.Length != fields.Length)
    {
        Console.Error.WriteLine($"Skipped a line with {values.Length} values instead of {fields.Length}: {line}");
        continue;
    }

    // nvidia-smi prints local time as "yyyy/MM/dd HH:mm:ss.fff". Store it as ISO 8601 with the UTC offset.
    var localTime = DateTime.ParseExact(values[0], "yyyy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    string timestamp = new DateTimeOffset(localTime).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    writer.WriteLine(string.Join(',', [timestamp, label, .. values[1..]]));
    rows++;
}

nvidiaSmi.WaitForExit();
Console.Error.WriteLine($"Wrote {rows} rows.");
return 0;

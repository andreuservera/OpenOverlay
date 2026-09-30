using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace IRacingOverlay.App.Diagnostics;

/// <summary>What a run left in the journal.</summary>
/// <param name="CrashReport">File name of the crash report, when a crash handler got to write one.</param>
public sealed record RunRecord(string RunId, int ProcessId, DateTime StartedUtc, string Version, string? CrashReport = null);

/// <summary>
/// One marker file per running instance, removed on a clean exit. A marker whose process is gone
/// belongs to a run that never shut down — killed, taken down by a fault no handler can catch
/// (stack overflow, native crash), or a power cut — and the next start records it, naming the run
/// whose log stops abruptly.
/// </summary>
public sealed class RunJournal
{
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromMinutes(1);

    private readonly string _directory;
    private readonly RunRecord _run;
    private readonly string _markerPath;
    private volatile bool _crashed;

    public RunJournal(string directory, RunRecord run)
    {
        _directory = directory;
        _run = run;
        _markerPath = Path.Combine(directory, run.RunId + ".run");
    }

    public static RunJournal? Current { get; set; }

    /// <summary>Marks this run as started and returns the earlier runs found: those that never shut
    /// down, and those still running (a second instance).</summary>
    public (IReadOnlyList<RunRecord> Unclean, IReadOnlyList<RunRecord> StillRunning) Begin()
    {
        var unclean = new List<RunRecord>();
        var running = new List<RunRecord>();
        try
        {
            Directory.CreateDirectory(_directory);
            foreach (var path in Directory.GetFiles(_directory, "*.run"))
            {
                var record = Read(path);
                if (record is not null && IsRunning(record))
                {
                    running.Add(record);
                    continue;
                }

                if (record is not null)
                {
                    unclean.Add(record);
                }

                File.Delete(path);
            }

            File.WriteAllText(_markerPath, JsonSerializer.Serialize(_run));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Run journal", "Run journal unavailable; unclean exits won't be detected", e);
        }

        return (unclean, running);
    }

    /// <summary>Keeps the marker past exit, pointing at the crash report, for the next start to report.</summary>
    public void RecordCrash(string? reportPath)
    {
        _crashed = true;
        try
        {
            File.WriteAllText(_markerPath, JsonSerializer.Serialize(_run with { CrashReport = Path.GetFileName(reportPath) }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The marker from Begin still flags the run as unclean.
        }
    }

    public void End()
    {
        if (_crashed)
        {
            return;
        }

        try
        {
            File.Delete(_markerPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Next start reports this run as unclean; a false alarm beats a missed crash.
        }
    }

    private static RunRecord? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Alive, and the same process rather than a later one that reused its ID.</summary>
    private static bool IsRunning(RunRecord record)
    {
        try
        {
            using var process = Process.GetProcessById(record.ProcessId);
            return !process.HasExited
                && (process.StartTime.ToUniversalTime() - record.StartedUtc).Duration() < StartTimeTolerance;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Tests;

public sealed class RunJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "oo-runs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static RunRecord Run(string id, int pid, DateTime started) => new(id, pid, started, "1.2.3");

    private RunJournal Journal(string id) => new(_directory, Run(id, Environment.ProcessId, CurrentProcessStart()));

    private static DateTime CurrentProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }

    private void Leave(RunRecord record)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, record.RunId + ".run"), JsonSerializer.Serialize(record));
    }

    [Fact]
    public void CleanExit_LeavesNothingBehind()
    {
        var journal = Journal("AAAA0001");

        Assert.Empty(journal.Begin().Unclean);
        Assert.Single(Directory.GetFiles(_directory, "*.run"));

        journal.End();
        Assert.Empty(Directory.GetFiles(_directory, "*.run"));
    }

    [Fact]
    public void RunWhoseProcessIsGone_IsReportedOnceAsUnclean()
    {
        // A PID from a run long ago: whatever holds it now started at a different time.
        Leave(Run("DEAD0001", Environment.ProcessId, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var (unclean, running) = Journal("AAAA0002").Begin();

        Assert.Equal("DEAD0001", Assert.Single(unclean).RunId);
        Assert.Empty(running);
        Assert.False(File.Exists(Path.Combine(_directory, "DEAD0001.run")));
        Assert.Empty(Journal("AAAA0003").Begin().Unclean);
    }

    [Fact]
    public void RunStillAlive_IsAnotherInstanceNotACrash()
    {
        Leave(Run("LIVE0001", Environment.ProcessId, CurrentProcessStart()));

        var (unclean, running) = Journal("AAAA0004").Begin();

        Assert.Empty(unclean);
        Assert.Equal("LIVE0001", Assert.Single(running).RunId);
    }

    [Fact]
    public void Crash_KeepsTheMarkerWithTheReportForTheNextStart()
    {
        var crashed = Journal("CRSH0001");
        crashed.Begin();
        crashed.RecordCrash(@"C:\logs\crashes\crash-20260930-120000-CRSH0001.txt");
        crashed.End();

        var marker = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(Path.Combine(_directory, "CRSH0001.run")));
        Assert.Equal("crash-20260930-120000-CRSH0001.txt", marker!.CrashReport);
    }

    [Fact]
    public void UnreadableMarker_IsDiscarded()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "JUNK.run"), "{ not json");

        Assert.Empty(Journal("AAAA0005").Begin().Unclean);
        Assert.False(File.Exists(Path.Combine(_directory, "JUNK.run")));
    }
}

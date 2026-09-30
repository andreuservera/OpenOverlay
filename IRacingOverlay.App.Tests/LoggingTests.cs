using System.IO;
using System.Text.Json;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Tests;

public class LoggingTests
{
    private sealed class ListSink : ILogSink
    {
        public List<LogEntry> Entries { get; } = [];

        public void Write(LogEntry entry) => Entries.Add(entry);

        public void Flush(TimeSpan timeout)
        {
        }
    }

    private sealed class Clock(DateTime start)
    {
        public DateTime Now { get; set; } = start;
    }

    [Fact]
    public void Write_RepeatedEntry_IsSuppressedWithinTheWindowAndCountedOnTheNextOne()
    {
        var clock = new Clock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        var logger = new Logger(() => clock.Now, duplicateWindow: TimeSpan.FromSeconds(60));
        var failure = new InvalidOperationException("same failure");

        for (var i = 0; i < 100; i++)
        {
            logger.Write(LogLevel.Error, "Widget: Fuel", "Build failed", failure);
            clock.Now += TimeSpan.FromMilliseconds(100);
        }

        Assert.Single(logger.Recent());
        Assert.Equal(99, logger.TotalSuppressed);

        clock.Now += TimeSpan.FromSeconds(60);
        logger.Write(LogLevel.Error, "Widget: Fuel", "Build failed", failure);

        Assert.Equal(2, logger.Recent().Count);
        Assert.Equal(99, logger.Recent()[1].Suppressed);
    }

    [Fact]
    public void Write_DifferentMessagesOrSources_AreNotSuppressed()
    {
        var logger = new Logger();

        logger.Write(LogLevel.Error, "A", "failed", new InvalidOperationException("x"));
        logger.Write(LogLevel.Error, "B", "failed", new InvalidOperationException("x"));
        logger.Write(LogLevel.Error, "A", "failed", new InvalidOperationException("y"));
        logger.Write(LogLevel.Warning, "A", "failed", new InvalidOperationException("x"));

        Assert.Equal(4, logger.Recent().Count);
    }

    [Fact]
    public void Write_Critical_IsNeverSuppressed()
    {
        var logger = new Logger();

        logger.Write(LogLevel.Critical, "Crash", "terminating");
        logger.Write(LogLevel.Critical, "Crash", "terminating");

        Assert.Equal(2, logger.Recent().Count);
    }

    [Fact]
    public void Write_WarningsAndErrorsGetSequentialReferences_InfoDoesNot()
    {
        var logger = new Logger(refPrefix: "7F3A91C2");

        var info = logger.Write(LogLevel.Info, "Startup", "starting");
        var warning = logger.Write(LogLevel.Warning, "Updates", "offline");
        var error = logger.Write(LogLevel.Error, "Widget: Fuel", "Build failed", new InvalidOperationException());

        Assert.Null(info!.Ref);
        Assert.Equal("7F3A91C2-001", warning!.Ref);
        Assert.Equal("7F3A91C2-002", error!.Ref);
    }

    [Fact]
    public void Write_Suppressed_ReturnsNull()
    {
        var logger = new Logger();

        Assert.NotNull(logger.Write(LogLevel.Error, "A", "failed"));
        Assert.Null(logger.Write(LogLevel.Error, "A", "failed"));
    }

    [Fact]
    public void Write_ActivityEntries_AreNeverCollapsed()
    {
        var logger = new Logger();

        logger.Write(LogLevel.Info, "Widget: Fuel", "Switched on", deduplicate: false);
        logger.Write(LogLevel.Info, "Widget: Fuel", "Switched off", deduplicate: false);
        logger.Write(LogLevel.Info, "Widget: Fuel", "Switched on", deduplicate: false);

        Assert.Equal(["Switched on", "Switched off", "Switched on"], logger.Recent().Select(e => e.Message));
    }

    [Fact]
    public void Problems_CountEveryOccurrenceIncludingSuppressedOnes()
    {
        var clock = new Clock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        var logger = new Logger(() => clock.Now, refPrefix: "RUN");
        var first = clock.Now;

        for (var i = 0; i < 50; i++)
        {
            logger.Write(LogLevel.Error, "Widget: Fuel", "Build failed", new IndexOutOfRangeException("bad index"));
            clock.Now += TimeSpan.FromSeconds(1);
        }

        logger.Write(LogLevel.Warning, "Updates", "offline");
        logger.Write(LogLevel.Info, "Startup", "not a problem");

        var problems = logger.Problems();
        Assert.Equal(2, problems.Count);
        var fuel = problems[0];
        Assert.Equal(LogLevel.Error, fuel.Level);
        Assert.Equal(50, fuel.Count);
        Assert.Equal(first, fuel.FirstUtc);
        Assert.Equal(first.AddSeconds(49), fuel.LastUtc);
        Assert.Equal("System.IndexOutOfRangeException", fuel.ExceptionType);
        // 50 errors a second apart, one written per 60 s window: the reference points at the written one.
        Assert.Equal("RUN-001", fuel.LastRef);
        Assert.Equal("RUN-001", fuel.FirstRef);
        Assert.Equal(LogLevel.Warning, problems[1].Level);
    }

    private static Exception ThrownByBuilder() => Capture(() => throw new IndexOutOfRangeException("bad index"));

    private static Exception ThrownByRenderer() => Capture(() => throw new IndexOutOfRangeException("bad index"));

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            return e;
        }

        throw new InvalidOperationException("Expected an exception.");
    }

    [Fact]
    public void StackTrace_IsWrittenOnceThenReferenced()
    {
        var clock = new Clock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        var logger = new Logger(() => clock.Now, refPrefix: "RUN");

        var first = logger.Write(LogLevel.Error, "Widget: Fuel", "Build failed", ThrownByBuilder());
        clock.Now += TimeSpan.FromSeconds(61);
        var repeat = logger.Write(LogLevel.Error, "Widget: Fuel", "Build failed", ThrownByBuilder());

        Assert.NotNull(first!.Exception!.StackTrace);
        Assert.Null(first.Exception.StackRef);
        Assert.Null(repeat!.Exception!.StackTrace);
        Assert.Equal(first.Ref, repeat.Exception.StackRef);
        Assert.Equal("bad index", repeat.Exception.Message);
        Assert.Contains($"(stack trace: see ref {first.Ref})", repeat.Exception.ToString());
        Assert.Equal(first.Ref, logger.Problems().Single().FirstRef);
    }

    [Fact]
    public void StackTrace_OfTheSameErrorFromAnotherPlace_IsWrittenToo()
    {
        var clock = new Clock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        var logger = new Logger(() => clock.Now);

        logger.Write(LogLevel.Error, "Widget: Fuel", "Build failed", ThrownByBuilder());
        clock.Now += TimeSpan.FromSeconds(61);
        var elsewhere = logger.Write(LogLevel.Error, "Widget: Fuel", "Build failed", ThrownByRenderer());

        Assert.NotNull(elsewhere!.Exception!.StackTrace);
        Assert.Null(elsewhere.Exception.StackRef);
    }

    [Fact]
    public void StackTrace_OfCriticalEntries_IsAlwaysWritten()
    {
        var logger = new Logger();

        logger.Write(LogLevel.Critical, "Crash", "terminating", ThrownByBuilder());
        var again = logger.Write(LogLevel.Critical, "Crash", "terminating", ThrownByBuilder());

        Assert.NotNull(again!.Exception!.StackTrace);
    }

    [Fact]
    public void ToJsonLine_ShortenedException_CarriesStackRefInsteadOfStack()
    {
        var exception = ExceptionInfo.From(ThrownByBuilder()).WithoutStackTraces("RUN-007");
        var entry = new LogEntry(DateTime.UtcNow, LogLevel.Error, "Widget: Fuel", "Build failed", exception, 1, null, 0, null, "RUN-042");

        using var json = JsonDocument.Parse(LogFormatter.ToJsonLine(entry));
        var logged = json.RootElement.GetProperty("exception");

        Assert.Equal("RUN-007", logged.GetProperty("stackRef").GetString());
        Assert.False(logged.TryGetProperty("stack", out _));
    }

    [Fact]
    public void Recent_KeepsTheNewestEntriesInOrder()
    {
        var logger = new Logger(ringCapacity: 3);

        for (var i = 0; i < 5; i++)
        {
            logger.Write(LogLevel.Info, "Test", $"entry {i}");
        }

        Assert.Equal(["entry 2", "entry 3", "entry 4"], logger.Recent().Select(e => e.Message));
    }

    [Fact]
    public void AttachSink_ReplaysWhatWasLoggedBeforeIt()
    {
        var logger = new Logger();
        logger.Write(LogLevel.Info, "Startup", "before the file existed");
        var sink = new ListSink();

        logger.AttachSink(sink);
        logger.Write(LogLevel.Info, "Startup", "after");

        Assert.Equal(["before the file existed", "after"], sink.Entries.Select(e => e.Message));
    }

    [Fact]
    public void Write_StampsTheSessionContext()
    {
        var logger = new Logger { Session = "Spa · Race" };

        logger.Write(LogLevel.Info, "Test", "hello");

        Assert.Equal("Spa · Race", logger.Recent()[0].Session);
    }

    [Fact]
    public void ExceptionInfo_KeepsInnerAndAggregatedExceptions()
    {
        Exception thrown;
        try
        {
            throw new AggregateException(
                new InvalidOperationException("outer", new IOException("disk")),
                new TimeoutException("slow"));
        }
        catch (Exception e)
        {
            thrown = e;
        }

        var info = ExceptionInfo.From(thrown);

        Assert.Equal(typeof(AggregateException).FullName, info.Type);
        Assert.NotNull(info.StackTrace);
        Assert.Equal(2, info.Inner.Count);
        Assert.Equal(typeof(IOException).FullName, info.Inner[0].Inner[0].Type);
        Assert.Contains("---> System.TimeoutException: slow", info.ToString());
    }

    [Fact]
    public void ToJsonLine_IsOneValidJsonObjectWithTheStructuredFields()
    {
        var entry = new LogEntry(
            new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc),
            LogLevel.Error,
            "Telemetry",
            "Read failed",
            ExceptionInfo.From(new IOException("gone", new InvalidDataException("inner"))),
            7,
            "Spa · Race",
            3,
            new Dictionary<string, string> { ["stage"] = "read" },
            "7F3A91C2-012");

        var bytes = LogFormatter.ToJsonLine(entry);

        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\n', bytes[..^1]);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Assert.Equal("2026-09-30T12:00:00.0000000Z", root.GetProperty("ts").GetString());
        Assert.Equal("Error", root.GetProperty("level").GetString());
        Assert.Equal("Telemetry", root.GetProperty("source").GetString());
        Assert.Equal("7F3A91C2-012", root.GetProperty("ref").GetString());
        Assert.Equal(AppInfo.RunId, root.GetProperty("run").GetString());
        Assert.Equal("Spa · Race", root.GetProperty("session").GetString());
        Assert.Equal(3, root.GetProperty("suppressed").GetInt32());
        Assert.Equal("read", root.GetProperty("data").GetProperty("stage").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("version").GetString()));
        Assert.Equal("System.IO.IOException", root.GetProperty("exception").GetProperty("type").GetString());
        Assert.Equal("inner", root.GetProperty("exception").GetProperty("inner")[0].GetProperty("message").GetString());
    }

    [Fact]
    public void RollingFileSink_WritesJsonLinesAndPrunesOldFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "oo-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var stale = Path.Combine(directory, RollingFileSink.FilePrefix + "20000101.log");
        File.WriteAllText(stale, "{}\n");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-30));
        try
        {
            var sink = new RollingFileSink(directory);
            var logger = new Logger();
            logger.AttachSink(sink);
            for (var i = 0; i < 10; i++)
            {
                logger.Write(LogLevel.Info, "Test", $"line {i}");
            }

            sink.Flush(TimeSpan.FromSeconds(5));
            sink.Dispose(TimeSpan.FromSeconds(5));

            Assert.False(File.Exists(stale));
            var lines = Directory.GetFiles(directory, RollingFileSink.FilePrefix + "*.log").SelectMany(File.ReadAllLines).ToList();
            Assert.Equal(10, lines.Count);
            Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RollingFileSink_KeepsTheTotalUnderTheCapWithinOneDay()
    {
        const long maxFile = 2_000;
        const long maxTotal = 6_000;
        var directory = Path.Combine(Path.GetTempPath(), "oo-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sink = new RollingFileSink(directory, maxFile, maxTotal);
            var logger = new Logger();
            logger.AttachSink(sink);
            for (var i = 0; i < 300; i++)
            {
                logger.Write(LogLevel.Info, "Test", $"line {i}", deduplicate: false);
            }

            sink.Flush(TimeSpan.FromSeconds(5));
            sink.Dispose(TimeSpan.FromSeconds(5));

            var files = new DirectoryInfo(directory).GetFiles(RollingFileSink.FilePrefix + "*.log");
            Assert.True(files.Length > 1, "Expected the log to roll over several files.");
            // A file can overshoot its limit by the one line that crossed it.
            Assert.InRange(files.Sum(f => f.Length), 1, maxTotal + maxFile + 1_000);
            Assert.Contains(files.SelectMany(f => File.ReadAllLines(f.FullName)), line => line.Contains("\"line 299\""));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

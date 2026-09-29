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
            new Dictionary<string, string> { ["stage"] = "read" });

        var bytes = LogFormatter.ToJsonLine(entry);

        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\n', bytes[..^1]);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Assert.Equal("2026-09-30T12:00:00.0000000Z", root.GetProperty("ts").GetString());
        Assert.Equal("Error", root.GetProperty("level").GetString());
        Assert.Equal("Telemetry", root.GetProperty("source").GetString());
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
}

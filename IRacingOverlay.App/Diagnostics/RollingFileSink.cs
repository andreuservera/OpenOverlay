using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace IRacingOverlay.App.Diagnostics;

/// <summary>
/// Persists log entries as JSON Lines: one file per local day, rolled at 10 MB, pruned after 30 days
/// or past 100 MB in total. A dedicated background thread does the I/O, so a slow or full disk costs
/// dropped log lines rather than a stalled UI thread; the queue is bounded for the same reason.
/// </summary>
public sealed class RollingFileSink : ILogSink
{
    public const string FilePrefix = "openoverlay-";
    private const long MaxFileBytes = 10L * 1024 * 1024;
    private const long MaxTotalBytes = 100L * 1024 * 1024;
    private const int QueueCapacity = 10_000;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan RetryOpenAfter = TimeSpan.FromSeconds(30);

    private readonly string _directory;
    private readonly BlockingCollection<LogEntry> _queue = new(QueueCapacity);
    private readonly Thread _writer;
    private long _enqueued;
    private long _processed;
    private long _dropped;
    private FileStream? _stream;
    private string? _currentDate;
    private int _currentPart;
    private long _retryOpenAtMs;

    public RollingFileSink(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        Prune();
        _writer = new Thread(Run)
        {
            IsBackground = true,
            Name = "OpenOverlay log writer",
            Priority = ThreadPriority.BelowNormal,
        };
        _writer.Start();
    }

    public void Write(LogEntry entry)
    {
        try
        {
            if (_queue.TryAdd(entry))
            {
                Interlocked.Increment(ref _enqueued);
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // Shut down between the caller's check and the add.
        }

        Interlocked.Increment(ref _dropped);
    }

    public void Flush(TimeSpan timeout)
    {
        var target = Interlocked.Read(ref _enqueued);
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Interlocked.Read(ref _processed) < target && _writer.IsAlive && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(5);
        }
    }

    public void Dispose(TimeSpan timeout)
    {
        _queue.CompleteAdding();
        _writer.Join(timeout);
    }

    private void Run()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
        {
            try
            {
                WriteEntry(entry);
            }
            catch (Exception)
            {
                // Drop the handle; the next entry reopens (or waits out the retry delay).
                CloseStream();
                _retryOpenAtMs = Environment.TickCount64 + (long)RetryOpenAfter.TotalMilliseconds;
            }
            finally
            {
                Interlocked.Increment(ref _processed);
            }
        }

        CloseStream();
    }

    private void WriteEntry(LogEntry entry)
    {
        if (_stream is null && Environment.TickCount64 < _retryOpenAtMs)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        var stream = EnsureStream(entry.TimestampUtc);
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            stream.Write(LogFormatter.ToJsonLine(new LogEntry(
                DateTime.UtcNow, LogLevel.Warning, "Logging", $"{dropped} log entries were dropped (queue full or disk unavailable)",
                null, Environment.CurrentManagedThreadId, entry.Session, 0, null)));
        }

        stream.Write(LogFormatter.ToJsonLine(entry));
        // To the OS, not the disk: survives a process crash, which is the case that matters here.
        stream.Flush();
    }

    private FileStream EnsureStream(DateTime timestampUtc)
    {
        var date = timestampUtc.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (_stream is not null && date == _currentDate && _stream.Length < MaxFileBytes)
        {
            return _stream;
        }

        CloseStream();
        if (date != _currentDate)
        {
            _currentDate = date;
            _currentPart = 0;
            Prune();
        }

        while (true)
        {
            var path = PathFor(date, _currentPart);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxFileBytes)
            {
                // Shared so the log can be opened, tailed or zipped while the app is running.
                _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                return _stream;
            }

            _currentPart++;
        }
    }

    private string PathFor(string date, int part) =>
        Path.Combine(_directory, $"{FilePrefix}{date}{(part == 0 ? "" : "-" + part.ToString(CultureInfo.InvariantCulture))}.log");

    private void CloseStream()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (Exception)
        {
            // Closing a handle to a vanished disk can throw; there is nothing left to save.
        }

        _stream = null;
    }

    private void Prune()
    {
        try
        {
            long total = 0;
            var files = new DirectoryInfo(_directory).GetFiles(FilePrefix + "*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc);
            foreach (var file in files)
            {
                total += file.Length;
                if (DateTime.UtcNow - file.LastWriteTimeUtc > Retention || total > MaxTotalBytes)
                {
                    file.Delete();
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Retention is housekeeping; a locked old file can go next time.
        }
    }
}

/// <summary>Renders entries for the log file (JSON Lines) and for humans (diagnostics reports).</summary>
public static class LogFormatter
{
    private static readonly JsonWriterOptions JsonOptions = new()
    {
        // Track and driver names are routinely non-ASCII; keep them readable in the file.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] ToJsonLine(LogEntry entry)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var json = new Utf8JsonWriter(buffer, JsonOptions))
        {
            json.WriteStartObject();
            json.WriteString("ts", entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("level", entry.Level.ToString());
            json.WriteString("source", entry.Source);
            json.WriteString("msg", entry.Message);
            if (entry.Ref is { } reference)
            {
                json.WriteString("ref", reference);
            }

            json.WriteString("run", AppInfo.RunId);
            json.WriteString("version", AppInfo.Version);
            json.WriteString("env", AppInfo.InstallKind);
            json.WriteNumber("pid", AppInfo.ProcessId);
            json.WriteNumber("tid", entry.ThreadId);
            if (entry.Session is { } session)
            {
                json.WriteString("session", session);
            }

            if (entry.Suppressed > 0)
            {
                json.WriteNumber("suppressed", entry.Suppressed);
            }

            if (entry.Data is { Count: > 0 } data)
            {
                json.WriteStartObject("data");
                foreach (var (key, value) in data)
                {
                    json.WriteString(key, value);
                }

                json.WriteEndObject();
            }

            if (entry.Exception is { } exception)
            {
                json.WritePropertyName("exception");
                WriteException(json, exception);
            }

            json.WriteEndObject();
        }

        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    public static string ToText(LogEntry entry)
    {
        var text = new StringBuilder()
            .Append(entry.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ').Append(entry.Level.ToString().ToUpperInvariant().PadRight(8))
            .Append('[').Append(entry.Source).Append("] ")
            .Append(entry.Message);
        if (entry.Ref is { } reference)
        {
            text.Append(" (ref ").Append(reference).Append(')');
        }

        if (entry.Suppressed > 0)
        {
            text.Append(" (+").Append(entry.Suppressed).Append(" identical suppressed)");
        }

        if (entry.Data is { Count: > 0 } data)
        {
            text.Append(" {").Append(string.Join(", ", data.Select(pair => $"{pair.Key}={pair.Value}"))).Append('}');
        }

        if (entry.Exception is { } exception)
        {
            text.AppendLine().Append(exception.ToString().TrimEnd());
        }

        return text.ToString();
    }

    private static void WriteException(Utf8JsonWriter json, ExceptionInfo exception)
    {
        json.WriteStartObject();
        json.WriteString("type", exception.Type);
        json.WriteString("message", exception.Message);
        json.WriteNumber("hresult", exception.HResult);
        if (exception.StackTrace is { } stack)
        {
            json.WriteString("stack", stack);
        }

        if (exception.Inner.Count > 0)
        {
            json.WriteStartArray("inner");
            foreach (var inner in exception.Inner)
            {
                WriteException(json, inner);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }
}

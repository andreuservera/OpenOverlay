using System.IO;

namespace IRacingOverlay.App.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical,
}

/// <summary>An exception reduced to plain data when it is logged, so the log never keeps the object
/// graph an exception can reference alive.</summary>
public sealed record ExceptionInfo(string Type, string Message, int HResult, string? StackTrace, IReadOnlyList<ExceptionInfo> Inner)
{
    private const int MaxDepth = 4;
    private const int MaxInner = 5;

    public static ExceptionInfo From(Exception exception, int depth = 0)
    {
        IEnumerable<Exception> inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } single ? [single] : [];

        return new ExceptionInfo(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.HResult,
            exception.StackTrace,
            depth >= MaxDepth ? [] : inner.Take(MaxInner).Select(e => From(e, depth + 1)).ToList());
    }

    public override string ToString()
    {
        var text = new System.Text.StringBuilder();
        Append(text, this, 0);
        return text.ToString();
    }

    private static void Append(System.Text.StringBuilder text, ExceptionInfo info, int depth)
    {
        var indent = new string(' ', depth * 2);
        text.Append(indent).Append(depth == 0 ? "" : "---> ").Append(info.Type).Append(": ").AppendLine(info.Message);
        if (!string.IsNullOrEmpty(info.StackTrace))
        {
            foreach (var line in info.StackTrace.Split('\n'))
            {
                text.Append(indent).Append("  ").AppendLine(line.TrimEnd('\r').Trim());
            }
        }

        foreach (var inner in info.Inner)
        {
            Append(text, inner, depth + 1);
        }
    }
}

/// <summary>One structured log record, immutable so it can cross threads freely.</summary>
/// <param name="Suppressed">Identical entries dropped by the duplicate filter since this one was last written.</param>
public sealed record LogEntry(
    DateTime TimestampUtc,
    LogLevel Level,
    string Source,
    string Message,
    ExceptionInfo? Exception,
    int ThreadId,
    string? Session,
    int Suppressed,
    IReadOnlyDictionary<string, string>? Data);

/// <summary>Where written entries go after the in-memory buffer.</summary>
public interface ILogSink
{
    void Write(LogEntry entry);

    /// <summary>Blocks until everything handed to <see cref="Write"/> is persisted, or the timeout passes.</summary>
    void Flush(TimeSpan timeout);
}

/// <summary>
/// The logging pipeline: duplicate suppression, a ring buffer of recent entries for diagnostics
/// reports, and an optional persistent sink. Thread-safe and never throws into the caller — a
/// failing logger must not be what takes the overlay down.
/// </summary>
public sealed class Logger
{
    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _duplicateWindow;
    private readonly LogEntry[] _ring;
    private readonly Dictionary<string, (DateTime LastWritten, int Suppressed)> _recent = new(StringComparer.Ordinal);
    private int _ringNext;
    private int _ringCount;
    private long _totalSuppressed;
    private ILogSink? _sink;
    private volatile string? _session;

    public Logger(Func<DateTime>? clock = null, int ringCapacity = 500, TimeSpan? duplicateWindow = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _ring = new LogEntry[Math.Max(1, ringCapacity)];
        _duplicateWindow = duplicateWindow ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>What the sim is running right now, stamped on every entry.</summary>
    public string? Session
    {
        get => _session;
        set => _session = value;
    }

    public long TotalSuppressed => Interlocked.Read(ref _totalSuppressed);

    /// <summary>Attaches the persistent sink, first replaying what was logged before it existed.</summary>
    public void AttachSink(ILogSink sink)
    {
        LogEntry[] backlog;
        lock (_gate)
        {
            _sink = sink;
            backlog = RecentUnlocked();
        }

        foreach (var entry in backlog)
        {
            WriteToSink(sink, entry);
        }
    }

    public void Write(
        LogLevel level,
        string source,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? data = null)
    {
        try
        {
            var now = _clock();
            int suppressed;
            ILogSink? sink;
            lock (_gate)
            {
                if (!ShouldWrite(level, source, message, exception, now, out suppressed))
                {
                    return;
                }

                sink = _sink;
            }

            var entry = new LogEntry(
                now,
                level,
                source,
                message,
                exception is null ? null : ExceptionInfo.From(exception),
                Environment.CurrentManagedThreadId,
                _session,
                suppressed,
                data);

            lock (_gate)
            {
                _ring[_ringNext] = entry;
                _ringNext = (_ringNext + 1) % _ring.Length;
                _ringCount = Math.Min(_ringCount + 1, _ring.Length);
            }

            System.Diagnostics.Debug.WriteLine($"[{level}] {source}: {message}{(exception is null ? "" : " — " + exception.Message)}");
            if (sink is not null)
            {
                WriteToSink(sink, entry);
            }
        }
        catch (Exception)
        {
            // Logging is best effort by design.
        }
    }

    public IReadOnlyList<LogEntry> Recent()
    {
        lock (_gate)
        {
            return RecentUnlocked();
        }
    }

    public void Flush(TimeSpan timeout)
    {
        try
        {
            _sink?.Flush(timeout);
        }
        catch (Exception)
        {
            // Best effort, as above.
        }
    }

    /// <summary>Drops an entry identical to one written less than a window ago, counting it instead,
    /// so a failure repeating at 60 Hz costs one line a minute rather than a disk full of the same
    /// stack trace over a 24-hour race. Critical entries are never dropped.</summary>
    private bool ShouldWrite(LogLevel level, string source, string message, Exception? exception, DateTime now, out int suppressed)
    {
        suppressed = 0;
        if (level == LogLevel.Critical)
        {
            return true;
        }

        var key = $"{(int)level}|{source}|{message}|{exception?.GetType().FullName}|{Truncate(exception?.Message, 300)}";
        if (_recent.TryGetValue(key, out var state) && now - state.LastWritten < _duplicateWindow)
        {
            _recent[key] = state with { Suppressed = state.Suppressed + 1 };
            Interlocked.Increment(ref _totalSuppressed);
            return false;
        }

        suppressed = state.Suppressed;
        _recent[key] = (now, 0);
        if (_recent.Count > 2000)
        {
            foreach (var stale in _recent.Where(pair => now - pair.Value.LastWritten >= _duplicateWindow).Select(pair => pair.Key).ToList())
            {
                _recent.Remove(stale);
            }
        }

        return true;
    }

    private LogEntry[] RecentUnlocked()
    {
        var result = new LogEntry[_ringCount];
        var start = (_ringNext - _ringCount + _ring.Length) % _ring.Length;
        for (var i = 0; i < _ringCount; i++)
        {
            result[i] = _ring[(start + i) % _ring.Length];
        }

        return result;
    }

    private static void WriteToSink(ILogSink sink, LogEntry entry)
    {
        try
        {
            sink.Write(entry);
        }
        catch (Exception)
        {
            // The ring buffer still has it.
        }
    }

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}

/// <summary>The application-wide logger. Every layer logs through here, so one file (and one
/// diagnostics report) tells the whole story of a session.</summary>
public static class AppLog
{
    public static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "logs");

    private static RollingFileSink? _fileSink;

    public static Logger Current { get; } = new();

    public static string? Session
    {
        get => Current.Session;
        set => Current.Session = value;
    }

    /// <summary>Starts writing to rolling files. Entries logged before this are written first.</summary>
    public static void Initialize(string? directory = null)
    {
        if (_fileSink is not null)
        {
            return;
        }

        try
        {
            _fileSink = new RollingFileSink(directory ?? LogDirectory);
            Current.AttachSink(_fileSink);
        }
        catch (Exception e)
        {
            Current.Write(LogLevel.Error, "Logging", "File logging unavailable; keeping entries in memory only", e);
        }
    }

    public static void Shutdown(TimeSpan timeout) => _fileSink?.Dispose(timeout);

    public static void Flush(TimeSpan timeout) => Current.Flush(timeout);

    public static IReadOnlyList<LogEntry> Recent() => Current.Recent();

    public static void Debug(string source, string message, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Debug, source, message, null, data);

    public static void Info(string source, string message, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Info, source, message, null, data);

    public static void Warn(string source, string message, Exception? exception = null, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Warning, source, message, exception, data);

    public static void Error(string source, string message, Exception? exception = null, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Error, source, message, exception, data);

    /// <summary>For failures that end the process. Flushed immediately, since there may be no later.</summary>
    public static void Critical(string source, string message, Exception? exception = null, IReadOnlyDictionary<string, string>? data = null)
    {
        Current.Write(LogLevel.Critical, source, message, exception, data);
        Current.Flush(TimeSpan.FromSeconds(2));
    }
}

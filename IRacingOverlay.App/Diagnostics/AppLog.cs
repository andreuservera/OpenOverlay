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
/// <param name="StackRef">Set instead of the stack traces when this run already logged them, pointing
/// at the entry that has them.</param>
public sealed record ExceptionInfo(
    string Type,
    string Message,
    int HResult,
    string? StackTrace,
    IReadOnlyList<ExceptionInfo> Inner,
    string? StackRef = null)
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

    public bool HasStackTrace => StackTrace is not null || Inner.Any(i => i.HasStackTrace);

    /// <summary>Equal for the same exception type thrown along the same path, within this run.</summary>
    public int Fingerprint()
    {
        var hash = new HashCode();
        hash.Add(Type);
        hash.Add(StackTrace);
        foreach (var inner in Inner)
        {
            hash.Add(inner.Fingerprint());
        }

        return hash.ToHashCode();
    }

    public ExceptionInfo WithoutStackTraces(string stackRef) => Strip(this) with { StackRef = stackRef };

    private static ExceptionInfo Strip(ExceptionInfo info) =>
        info with { StackTrace = null, Inner = info.Inner.Select(Strip).ToList() };

    public override string ToString()
    {
        var text = new System.Text.StringBuilder();
        Append(text, this, 0);
        if (StackRef is { } stackRef)
        {
            text.Append("  (stack trace: see ref ").Append(stackRef).AppendLine(")");
        }

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
/// <param name="Ref">Short reference for warnings and errors ("7F3A91C2-012"): shown in the Control Panel
/// and reports, so a problem the user sees can be found in the log.</param>
public sealed record LogEntry(
    DateTime TimestampUtc,
    LogLevel Level,
    string Source,
    string Message,
    ExceptionInfo? Exception,
    int ThreadId,
    string? Session,
    int Suppressed,
    IReadOnlyDictionary<string, string>? Data,
    string? Ref = null);

/// <summary>Every occurrence of one kind of warning or error in this run, suppressed ones included.</summary>
/// <param name="FirstRef">The first entry written for it, the one that carries the stack trace.</param>
public sealed record ProblemTally(
    LogLevel Level,
    string Source,
    string Message,
    string? ExceptionType,
    long Count,
    DateTime FirstUtc,
    DateTime LastUtc,
    string? LastRef,
    string? FirstRef = null);

/// <summary>Where written entries go after the in-memory buffer.</summary>
public interface ILogSink
{
    void Write(LogEntry entry);

    /// <summary>Blocks until everything handed to <see cref="Write"/> is persisted, or the timeout passes.</summary>
    void Flush(TimeSpan timeout);
}

/// <summary>
/// The logging pipeline: duplicate suppression, error references, a tally of every problem in the
/// run, a ring buffer of recent entries for diagnostics reports, and an optional persistent sink.
/// Thread-safe and never throws into the caller — a failing logger must not take the app down.
/// </summary>
public sealed class Logger
{
    private const int MaxProblemKinds = 200;
    private const int MaxStackSignatures = 1000;

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _duplicateWindow;
    private readonly string _refPrefix;
    private readonly LogEntry[] _ring;
    private readonly Dictionary<string, (DateTime LastWritten, int Suppressed)> _recent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProblemTally> _problems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _stackRefs = new(StringComparer.Ordinal);
    private int _ringNext;
    private int _ringCount;
    private int _refSequence;
    private long _totalSuppressed;
    private ILogSink? _sink;
    private volatile string? _session;

    public Logger(Func<DateTime>? clock = null, int ringCapacity = 500, TimeSpan? duplicateWindow = null, string refPrefix = "LOG")
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _ring = new LogEntry[Math.Max(1, ringCapacity)];
        _duplicateWindow = duplicateWindow ?? TimeSpan.FromSeconds(60);
        _refPrefix = refPrefix;
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

    /// <summary>Writes an entry, or counts it as a duplicate. Returns what was written (null when
    /// suppressed) so callers can keep its <see cref="LogEntry.Ref"/>.</summary>
    /// <param name="deduplicate">False for the activity trail, where two identical entries are two
    /// separate things that happened.</param>
    public LogEntry? Write(
        LogLevel level,
        string source,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? data = null,
        bool deduplicate = true)
    {
        try
        {
            var now = _clock();
            var key = $"{(int)level}|{source}|{message}|{exception?.GetType().FullName}|{Truncate(exception?.Message, 300)}";
            var suppressed = 0;
            string? reference = null;
            ILogSink? sink;
            lock (_gate)
            {
                var write = !deduplicate || level == LogLevel.Critical || ShouldWrite(key, now, out suppressed);
                if (write && level >= LogLevel.Warning)
                {
                    reference = $"{_refPrefix}-{++_refSequence:D3}";
                }

                if (level >= LogLevel.Warning)
                {
                    Tally(key, level, source, message, exception, now, reference);
                }

                if (!write)
                {
                    return null;
                }

                sink = _sink;
            }

            var entry = new LogEntry(
                now,
                level,
                source,
                message,
                exception is null ? null : StackTraceOnce(key, level, ExceptionInfo.From(exception), reference),
                Environment.CurrentManagedThreadId,
                _session,
                suppressed,
                data,
                reference);

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

            return entry;
        }
        catch (Exception)
        {
            // Logging is best effort by design.
            return null;
        }
    }

    public IReadOnlyList<LogEntry> Recent()
    {
        lock (_gate)
        {
            return RecentUnlocked();
        }
    }

    /// <summary>Every kind of warning and error this run, most severe and most frequent first. Unlike
    /// the ring buffer, nothing here scrolls away, and suppressed repeats are counted.</summary>
    public IReadOnlyList<ProblemTally> Problems()
    {
        lock (_gate)
        {
            return _problems.Values
                .OrderByDescending(p => p.Level)
                .ThenByDescending(p => p.Count)
                .ToList();
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
    /// stack trace.</summary>
    private bool ShouldWrite(string key, DateTime now, out int suppressed)
    {
        suppressed = 0;
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

    private void Tally(string key, LogLevel level, string source, string message, Exception? exception, DateTime now, string? reference)
    {
        if (_problems.TryGetValue(key, out var tally))
        {
            _problems[key] = tally with
            {
                Count = tally.Count + 1,
                LastUtc = now,
                LastRef = reference ?? tally.LastRef,
                FirstRef = tally.FirstRef ?? reference,
            };
        }
        else if (_problems.Count < MaxProblemKinds)
        {
            _problems[key] = new ProblemTally(level, source, message, exception?.GetType().FullName, 1, now, now, reference, reference);
        }
    }

    /// <summary>A stack trace is written the first time it appears in the run; later entries for the
    /// same error from the same place point at that entry instead of repeating kilobytes of it.
    /// Critical entries always keep theirs.</summary>
    private ExceptionInfo StackTraceOnce(string key, LogLevel level, ExceptionInfo exception, string? reference)
    {
        if (level == LogLevel.Critical || !exception.HasStackTrace)
        {
            return exception;
        }

        var signature = $"{key}|{exception.Fingerprint()}";
        lock (_gate)
        {
            if (_stackRefs.TryGetValue(signature, out var first))
            {
                return exception.WithoutStackTraces(first);
            }

            // Without a reference there is nothing for later entries to point at.
            if (reference is not null && _stackRefs.Count < MaxStackSignatures)
            {
                _stackRefs[signature] = reference;
            }
        }

        return exception;
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
/// diagnostics report) tells the whole story of a run.</summary>
public static class AppLog
{
    public static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "logs");

    private static RollingFileSink? _fileSink;

    public static Logger Current { get; } = new(refPrefix: AppInfo.RunId);

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

    public static IReadOnlyList<ProblemTally> Problems() => Current.Problems();

    /// <summary>Something the user did or the app decided: the trail that explains the errors around
    /// it. Never collapsed as a duplicate — switching a widget off, on and off again is three events.</summary>
    public static LogEntry? Activity(string source, string message, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Info, source, message, null, data, deduplicate: false);

    public static LogEntry? Debug(string source, string message, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Debug, source, message, null, data);

    public static LogEntry? Info(string source, string message, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Info, source, message, null, data);

    public static LogEntry? Warn(string source, string message, Exception? exception = null, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Warning, source, message, exception, data);

    public static LogEntry? Error(string source, string message, Exception? exception = null, IReadOnlyDictionary<string, string>? data = null) =>
        Current.Write(LogLevel.Error, source, message, exception, data);

    /// <summary>For failures that end the process. Flushed immediately, since there may be no later.</summary>
    public static LogEntry? Critical(string source, string message, Exception? exception = null, IReadOnlyDictionary<string, string>? data = null)
    {
        var entry = Current.Write(LogLevel.Critical, source, message, exception, data);
        Current.Flush(TimeSpan.FromSeconds(2));
        return entry;
    }
}

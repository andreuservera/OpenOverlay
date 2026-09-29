using System.Globalization;

namespace IRacingOverlay.App.Diagnostics;

public enum HealthStatus
{
    Healthy,
    Degraded,
    Failed,
}

/// <summary>Which half of a widget's update failed: computing its state from telemetry, or pushing
/// that state into the window. They recover differently — a builder is rebuilt, a window reopened.</summary>
public enum GuardStage
{
    Build,
    Render,
}

public sealed record ComponentHealth(
    string Name,
    HealthStatus Status,
    string Summary,
    DateTime SinceUtc,
    DateTime? LastSuccessUtc = null,
    DateTime? LastFailureUtc = null,
    long Failures = 0,
    int Recoveries = 0,
    string? LastError = null,
    string? LastErrorRef = null);

public sealed record HealthReport(DateTime GeneratedUtc, HealthStatus Overall, IReadOnlyList<ComponentHealth> Components)
{
    public static HealthReport Empty { get; } = new(DateTime.MinValue, HealthStatus.Healthy, []);

    /// <summary>One line for the status bar: the worst component, with the log reference of its
    /// latest error so what the user sees can be found in the log.</summary>
    public string Summary
    {
        get
        {
            var problems = Components.Where(c => c.Status != HealthStatus.Healthy)
                .OrderByDescending(c => c.Status)
                .ToList();
            if (problems.Count == 0)
            {
                return "All systems healthy";
            }

            var worst = problems[0];
            var reference = worst.LastErrorRef is { } r ? $" · ref {r}" : "";
            var more = problems.Count > 1 ? $" (+{problems.Count - 1} more)" : "";
            return $"{worst.Name}: {worst.Summary}{reference}{more}";
        }
    }
}

/// <summary>Exceptions no amount of retrying fixes; they are left to end the process (and be reported).</summary>
public static class ExceptionPolicy
{
    public static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or AccessViolationException or StackOverflowException
            or System.Runtime.InteropServices.SEHException or BadImageFormatException or InvalidProgramException;
}

/// <summary>
/// A circuit breaker around one component of the update loop. Each failure is logged and counted;
/// after <c>failureThreshold</c> in a row the component is switched off for a cooldown that doubles
/// on every repeat (1 s, 2 s, 4 s … 60 s), its recovery action runs, and it is retried when the
/// cooldown ends. A component failing on every tick therefore costs a log line a minute and never
/// touches the components around it.
/// </summary>
public sealed class ComponentGuard
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ForgetTripsAfter = TimeSpan.FromMinutes(5);

    private readonly Action<GuardStage>? _recover;
    private readonly Func<DateTime> _clock;
    private readonly int _failureThreshold;
    private readonly TimeSpan _baseCooldown;
    private readonly TimeSpan _maxCooldown;
    private readonly int[] _consecutive = new int[2];
    private DateTime _retryAtUtc = DateTime.MinValue;
    private DateTime _sinceUtc;
    private DateTime? _lastSuccessUtc;
    private DateTime? _lastFailureUtc;
    private DateTime? _recoveredAtUtc;
    private bool _tripped;
    private int _trips;
    private long _failures;
    private int _recoveries;
    private string? _lastError;
    private string? _lastErrorRef;

    public ComponentGuard(
        string name,
        Action<GuardStage>? recover = null,
        Func<DateTime>? clock = null,
        int failureThreshold = 3,
        TimeSpan? baseCooldown = null,
        TimeSpan? maxCooldown = null)
    {
        Name = name;
        _recover = recover;
        _clock = clock ?? (() => DateTime.UtcNow);
        _failureThreshold = Math.Max(1, failureThreshold);
        _baseCooldown = baseCooldown ?? TimeSpan.FromSeconds(1);
        _maxCooldown = maxCooldown ?? TimeSpan.FromSeconds(60);
        _sinceUtc = _clock();
    }

    public string Name { get; }

    public bool IsCoolingDown => _clock() < _retryAtUtc;

    public long Failures => _failures;

    /// <summary>Runs <paramref name="action"/> unless the component is cooling down. False when it
    /// was skipped or failed; the failure is already logged and counted.</summary>
    public bool Run(Action action, GuardStage stage = GuardStage.Render)
    {
        var now = _clock();
        if (now < _retryAtUtc)
        {
            return false;
        }

        try
        {
            action();
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            OnFailure(stage, e, now);
            return false;
        }

        OnSuccess(stage, now);
        return true;
    }

    public bool TryRun<T>(Func<T> func, out T result, GuardStage stage = GuardStage.Build)
    {
        var value = default(T)!;
        var ok = Run(() => value = func(), stage);
        result = value;
        return ok;
    }

    public ComponentHealth Health
    {
        get
        {
            var now = _clock();
            var (status, summary) = true switch
            {
                _ when _tripped && now < _retryAtUtc => (HealthStatus.Failed,
                    $"stopped after repeated errors, retrying in {Math.Ceiling((_retryAtUtc - now).TotalSeconds):0} s"),
                _ when _tripped => (HealthStatus.Failed, "retrying after repeated errors"),
                _ when _recoveredAtUtc is { } recovered && now - recovered < RecentWindow => (HealthStatus.Degraded, "recovered from errors"),
                _ when _lastFailureUtc is { } failed && now - failed < RecentWindow => (HealthStatus.Degraded,
                    $"{_failures.ToString(CultureInfo.InvariantCulture)} error(s), latest {Math.Round((now - failed).TotalSeconds):0} s ago"),
                _ => (HealthStatus.Healthy, _lastSuccessUtc is null ? "idle" : "running"),
            };

            return new ComponentHealth(Name, status, summary, _sinceUtc, _lastSuccessUtc, _lastFailureUtc, _failures, _recoveries, _lastError, _lastErrorRef);
        }
    }

    private void OnSuccess(GuardStage stage, DateTime now)
    {
        _consecutive[(int)stage] = 0;
        _lastSuccessUtc = now;
        if (_tripped)
        {
            _tripped = false;
            _recoveredAtUtc = now;
            _sinceUtc = now;
            AppLog.Info(Name, "Recovered", new Dictionary<string, string> { ["failures"] = _failures.ToString(CultureInfo.InvariantCulture) });
        }

        if (_trips > 0 && _lastFailureUtc is { } lastFailure && now - lastFailure > ForgetTripsAfter)
        {
            _trips = 0;
        }
    }

    private void OnFailure(GuardStage stage, Exception exception, DateTime now)
    {
        _failures++;
        _lastFailureUtc = now;
        _lastError = $"{stage}: {exception.GetType().Name}: {exception.Message}";
        if (!_tripped && _consecutive.All(c => c == 0))
        {
            _sinceUtc = now;
        }

        var consecutive = ++_consecutive[(int)stage];
        var logged = AppLog.Error(Name, $"{stage} failed", exception, new Dictionary<string, string>
        {
            ["consecutive"] = consecutive.ToString(CultureInfo.InvariantCulture),
            ["total"] = _failures.ToString(CultureInfo.InvariantCulture),
        });
        // A suppressed repeat keeps pointing at the entry that has the stack trace.
        _lastErrorRef = logged?.Ref ?? _lastErrorRef;

        if (consecutive < _failureThreshold)
        {
            return;
        }

        _consecutive[(int)stage] = 0;
        var cooldown = TimeSpan.FromTicks(Math.Min(_maxCooldown.Ticks, _baseCooldown.Ticks << Math.Min(_trips, 16)));
        _trips++;
        _tripped = true;
        _sinceUtc = now;
        _retryAtUtc = now + cooldown;
        _recoveries++;
        AppLog.Warn(Name, $"Paused after repeated {stage} failures; recovering", data: new Dictionary<string, string>
        {
            ["retryInSeconds"] = cooldown.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture),
            ["trip"] = _trips.ToString(CultureInfo.InvariantCulture),
        });

        try
        {
            _recover?.Invoke(stage);
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Error(Name, "Recovery action failed", e);
        }
    }
}

/// <summary>
/// Collects the health of every component — guarded update steps plus services that report their
/// own state (telemetry, UI thread, settings, updates) — and rolls it up into one status. Logs
/// every change of the overall status so the log shows when and why the app degraded.
/// </summary>
public sealed class HealthMonitor
{
    private readonly Func<DateTime> _clock;
    private readonly List<ComponentGuard> _guards = [];
    private readonly Dictionary<string, ComponentHealth> _reported = new(StringComparer.Ordinal);
    private HealthStatus _lastOverall = HealthStatus.Healthy;
    private static volatile HealthReport _latest = HealthReport.Empty;

    public HealthMonitor(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>The most recent snapshot from any monitor; read by crash reporting from any thread.</summary>
    public static HealthReport Latest => _latest;

    public ComponentGuard CreateGuard(string name, Action<GuardStage>? recover = null)
    {
        var guard = new ComponentGuard(name, recover, _clock);
        _guards.Add(guard);
        return guard;
    }

    /// <summary>Updates a self-reporting component. The "since" time only moves when the status changes.</summary>
    public void Report(
        string name,
        HealthStatus status,
        string summary,
        DateTime? lastSuccessUtc = null,
        DateTime? lastFailureUtc = null,
        long failures = 0,
        string? lastError = null,
        string? lastErrorRef = null)
    {
        var since = _reported.TryGetValue(name, out var previous) && previous.Status == status ? previous.SinceUtc : _clock();
        _reported[name] = new ComponentHealth(name, status, summary, since, lastSuccessUtc, lastFailureUtc, failures, 0, lastError, lastErrorRef);
    }

    public HealthReport Snapshot()
    {
        var components = _reported.Values.Concat(_guards.Select(g => g.Health)).ToList();
        var overall = components.Count == 0 ? HealthStatus.Healthy : components.Max(c => c.Status);
        var report = new HealthReport(_clock(), overall, components);
        if (overall != _lastOverall)
        {
            var level = overall > _lastOverall ? LogLevel.Warning : LogLevel.Info;
            AppLog.Current.Write(level, "Health", $"Overall health {_lastOverall} -> {overall}", data: new Dictionary<string, string>
            {
                ["detail"] = report.Summary,
            });
            _lastOverall = overall;
        }

        _latest = report;
        return report;
    }
}

using IRacingOverlay.App.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace IRacingOverlay.App;

public enum UpdateState
{
    NotStarted,
    NotInstalled,
    Checking,
    UpToDate,
    Deferred,
    Downloading,
    ReadyOnRestart,
    Failed,
}

/// <summary>
/// Checks GitHub Releases in the background and downloads a newer version for Velopack to apply on
/// the next start (its auto-apply-on-startup default), so an update never replaces the overlay
/// mid-race. Waits until iRacing is closed before downloading: a large download competing with the
/// sim's netcode is the last thing a driver needs. Failures retry with growing delays, are logged,
/// and never reach the user — the app works the same whether or not updating does.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DeferPoll = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

    private readonly Func<bool> _sessionLive;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;
    private volatile UpdateState _state = UpdateState.NotStarted;
    private volatile string _detail = "Not checked yet";
    private volatile string? _lastErrorRef;
    private int _failures;

    /// <param name="sessionLive">True while iRacing is in a session. Called from a background thread.</param>
    public UpdateService(Func<bool> sessionLive) => _sessionLive = sessionLive;

    public UpdateState State => _state;

    public string Detail => _detail;

    public int Failures => Volatile.Read(ref _failures);

    public string? LastErrorRef => _lastErrorRef;

    public void Start() => _task ??= Task.Run(() => RunAsync(_cts.Token));

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // RunAsync handles its own failures; nothing may escape shutdown.
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(StartupDelay, token);
            var manager = new UpdateManager(new GithubSource(App.GitHubRepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
            {
                Set(UpdateState.NotInstalled, "Development build: only installed copies update");
                return;
            }

            if (manager.UpdatePendingRestart is { } pending)
            {
                Set(UpdateState.ReadyOnRestart, $"Version {pending.Version} installs on the next start");
                return;
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await CheckAndDownloadAsync(manager, token);
                    return;
                }
                catch (Exception e) when (!token.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _failures);
                    if (attempt >= RetryDelays.Length)
                    {
                        Set(UpdateState.Failed, $"{e.Message} — checking again next launch");
                        _lastErrorRef = AppLog.Warn("Updates", "Update check failed; giving up until next launch", e)?.Ref ?? _lastErrorRef;
                        return;
                    }

                    Set(UpdateState.Failed, $"{e.Message} — retrying in {RetryDelays[attempt].TotalMinutes:0} min");
                    _lastErrorRef = AppLog.Warn("Updates", "Update check failed; will retry", e)?.Ref ?? _lastErrorRef;
                    await Task.Delay(RetryDelays[attempt], token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref _failures);
            Set(UpdateState.Failed, e.Message);
            _lastErrorRef = AppLog.Error("Updates", "Update service stopped", e)?.Ref ?? _lastErrorRef;
        }
    }

    private async Task CheckAndDownloadAsync(UpdateManager manager, CancellationToken token)
    {
        Set(UpdateState.Checking, "Checking GitHub for a newer release");
        var update = await manager.CheckForUpdatesAsync().WaitAsync(CheckTimeout, token);
        if (update is null)
        {
            Set(UpdateState.UpToDate, $"Version {AppInfo.Version} is the latest");
            AppLog.Info("Updates", "Up to date");
            return;
        }

        var version = update.TargetFullRelease.Version.ToString();
        while (_sessionLive())
        {
            if (_state != UpdateState.Deferred)
            {
                AppLog.Info("Updates", "Update download deferred until iRacing is closed", Version(version));
            }

            Set(UpdateState.Deferred, $"Version {version} downloads once iRacing is closed");
            await Task.Delay(DeferPoll, token);
        }

        Set(UpdateState.Downloading, $"Downloading version {version}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(DownloadTimeout);
        await manager.DownloadUpdatesAsync(update, null, timeout.Token);
        Set(UpdateState.ReadyOnRestart, $"Version {version} installs on the next start");
        AppLog.Info("Updates", "Update downloaded; it is applied on the next start", Version(version));
    }

    private void Set(UpdateState state, string detail)
    {
        _state = state;
        _detail = detail;
    }

    private static Dictionary<string, string> Version(string version) => new() { ["version"] = version };
}

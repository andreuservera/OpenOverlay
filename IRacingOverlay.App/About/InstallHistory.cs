using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.About;

/// <summary>
/// This PC's record of which versions have run: read and updated once at startup, then asked when
/// this version was installed and whether its update notice is still owed. Never throws — losing
/// the history costs one notice, never the start.
/// </summary>
internal static class InstallHistory
{
    private static VersionHistory? _history;

    public static VersionChange? StartupChange { get; private set; }

    /// <summary>When this version first ran on this PC.</summary>
    public static DateTime? InstalledUtc =>
        _history?.Versions.LastOrDefault(version => version.Version == BuildInfo.Version)?.FirstRunUtc;

    /// <summary>The version that ran here before this one.</summary>
    public static string? PreviousVersion =>
        _history is { Versions.Count: >= 2 } history && history.Versions[^1].Version == BuildInfo.Version
            ? history.Versions[^2].Version
            : null;

    public static bool NoticePending => _history?.PendingNotice is { } pending && pending == BuildInfo.Version;

    public static void RecordStartup()
    {
        try
        {
            var (history, change) = VersionTracker.Observe(
                VersionHistoryStore.Load(),
                BuildInfo.Version,
                VersionHistoryStore.HasEarlierSettings(),
                DateTime.UtcNow);
            _history = history;
            StartupChange = change;
            if (change.Kind == VersionChangeKind.Unchanged)
            {
                return;
            }

            VersionHistoryStore.Save(history);
            var data = new Dictionary<string, string> { ["version"] = change.Current, ["previous"] = change.Previous ?? "" };
            switch (change.Kind)
            {
                case VersionChangeKind.Updated:
                    AppLog.Info("Updates", "Updated since the last run", data);
                    break;
                case VersionChangeKind.Downgraded:
                    AppLog.Info("Updates", "Running an older version than the last run", data);
                    break;
                default:
                    AppLog.Info("Updates", "First run on this PC", data);
                    break;
            }
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Warn("Updates", "Version history unavailable; no update notice this run", e);
        }
    }

    public static void MarkNoticeShown()
    {
        if (_history is { PendingNotice: not null } history)
        {
            _history = VersionTracker.NoticeShown(history);
            VersionHistoryStore.Save(_history);
        }
    }
}

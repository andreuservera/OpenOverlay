namespace IRacingOverlay.App.About;

/// <summary>A version's first run on this PC.</summary>
public sealed record InstalledVersion(string Version, DateTime FirstRunUtc);

/// <summary>Which versions have run on this PC, kept between runs.</summary>
/// <param name="Versions">Oldest first, one entry per version, at most <see cref="VersionTracker.Remembered"/>.</param>
/// <param name="HighestRelease">The newest release that has ever run here, so switching to an older
/// version and back doesn't announce the same update twice.</param>
/// <param name="PendingNotice">A version whose update notice hasn't been shown yet.</param>
public sealed record VersionHistory(IReadOnlyList<InstalledVersion> Versions, string? HighestRelease, string? PendingNotice);

public enum VersionChangeKind
{
    /// <summary>Nothing has run here before.</summary>
    FirstRun,
    Unchanged,
    Updated,
    Downgraded,
}

/// <param name="Previous">The version that ran before this one, when known.</param>
public sealed record VersionChange(VersionChangeKind Kind, string Current, string? Previous);

/// <summary>
/// Decides, once per start, how this version relates to the last one that ran and whether the
/// post-update notice is owed: only when a version newer than any seen before runs on a PC that
/// has run OpenOverlay before.
/// </summary>
public static class VersionTracker
{
    public const int Remembered = 20;

    /// <param name="hasEarlierRuns">Settings from an earlier run exist although no history does: the
    /// version that wrote them predates version tracking, so this run is an update.</param>
    public static (VersionHistory History, VersionChange Change) Observe(VersionHistory? history, string current, bool hasEarlierRuns, DateTime nowUtc)
    {
        var versions = history?.Versions ?? [];
        var previous = versions.Count > 0 ? versions[^1].Version : null;
        var kind = previous is null
            ? hasEarlierRuns ? VersionChangeKind.Updated : VersionChangeKind.FirstRun
            : Compare(current, previous) switch
            {
                0 => VersionChangeKind.Unchanged,
                > 0 => VersionChangeKind.Updated,
                _ => VersionChangeKind.Downgraded,
            };

        if (kind == VersionChangeKind.Unchanged)
        {
            return (history!, new VersionChange(kind, current, previous));
        }

        var release = ReleaseVersion.ParseOrNull(current);
        var highest = ReleaseVersion.ParseOrNull(history?.HighestRelease);
        var isNewRelease = release is not null && (highest is null || release > highest);

        var updated = new VersionHistory(
            versions
                .Where(version => Compare(version.Version, current) != 0)
                .Append(new InstalledVersion(current, nowUtc))
                .TakeLast(Remembered)
                .ToList(),
            isNewRelease ? current : history?.HighestRelease,
            kind == VersionChangeKind.Updated && isNewRelease ? current : null);
        return (updated, new VersionChange(kind, current, previous));
    }

    public static VersionHistory NoticeShown(VersionHistory history) => history with { PendingNotice = null };

    /// <summary>By version precedence when both parse — so the same version built twice compares
    /// equal — and as text otherwise.</summary>
    private static int Compare(string left, string right) =>
        ReleaseVersion.TryParse(left, out var a) && ReleaseVersion.TryParse(right, out var b)
            ? a.CompareTo(b)
            : string.Equals(left, right, StringComparison.Ordinal) ? 0 : 1;
}

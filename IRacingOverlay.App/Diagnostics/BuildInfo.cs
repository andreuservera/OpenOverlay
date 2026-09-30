using System.Reflection;
using IRacingOverlay.App.About;

namespace IRacingOverlay.App.Diagnostics;

/// <summary>How a copy of OpenOverlay gets new versions.</summary>
public enum UpdateChannel
{
    /// <summary>Installed from a regular release; updates itself to each new one.</summary>
    Stable,

    /// <summary>Installed from a pre-release, or from a channel other than the default.</summary>
    Preview,

    /// <summary>A loose copy, which only a new download updates.</summary>
    Portable,
}

/// <summary>
/// What this copy of OpenOverlay is — version, commit and update channel — for the About page, the
/// status bar and diagnostics reports. The version is the newest CHANGELOG.md entry, stamped at
/// build time.
/// </summary>
public static class BuildInfo
{
    /// <summary>The version without build metadata: "0.7.0".</summary>
    public static string Version
    {
        get
        {
            var version = AppInfo.Version;
            var plus = version.IndexOf('+');
            return plus < 0 ? version : version[..plus];
        }
    }

    public static ReleaseVersion? Release => ReleaseVersion.ParseOrNull(AppInfo.Version);

    /// <summary>The commit the .NET SDK stamps into the informational version, abbreviated the way
    /// git shows it; null for a build made outside a git checkout.</summary>
    public static string? Commit
    {
        get
        {
            var informational = typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            var plus = informational.IndexOf('+');
            var commit = plus < 0 ? "" : informational[(plus + 1)..];
            return commit.Length >= 7 && commit.All(Uri.IsHexDigit) ? commit[..7] : null;
        }
    }

    public static UpdateChannel Channel => ChannelFor(AppInfo.InstallKind, AppInfo.UpdateChannel, Release);

    /// <summary>One line naming this exact build, for tooltips and bug reports.</summary>
    public static string Summary => $"OpenOverlay {Version}{(Commit is { } commit ? $" ({commit})" : "")} · {Channel}";

    internal static UpdateChannel ChannelFor(string installKind, string? velopackChannel, ReleaseVersion? version)
    {
        if (installKind != "installed")
        {
            return UpdateChannel.Portable;
        }

        // Velopack's default channel on Windows is "win"; any other was chosen on purpose.
        var customChannel = !string.IsNullOrEmpty(velopackChannel) && !velopackChannel.Equals("win", StringComparison.OrdinalIgnoreCase);
        return version is { IsPrerelease: true } || customChannel ? UpdateChannel.Preview : UpdateChannel.Stable;
    }
}

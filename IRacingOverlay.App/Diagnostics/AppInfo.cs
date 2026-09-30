using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Velopack.Locators;

namespace IRacingOverlay.App.Diagnostics;

/// <summary>Who and where this process is, for log lines and diagnostics reports.</summary>
public static class AppInfo
{
    private static string? _version;
    private static string? _installKind;

    public static DateTime StartedUtc { get; } = DateTime.UtcNow;

    public static int ProcessId { get; } = Environment.ProcessId;

    /// <summary>Stamped on every log line and report, so one launch can be pulled out of weeks of logs.</summary>
    public static string RunId { get; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>The run this process replaced when <see cref="AppRestarter"/> relaunched it.</summary>
    public static string? PreviousRunId { get; set; }

    public static TimeSpan Uptime => DateTime.UtcNow - StartedUtc;

    /// <summary>The Velopack release version when installed; otherwise the assembly's own version,
    /// which builds stamp with <c>-p:Version</c> (1.0.0 when nothing does).</summary>
    public static string Version => _version ?? AssemblyVersion;

    /// <summary>"installed" for a Velopack install, "development" for a loose build.</summary>
    public static string InstallKind => _installKind ?? "development";

    /// <summary>True when this process was started by <see cref="AppRestarter"/> after a crash or hang.</summary>
    public static bool IsRecoveredLaunch { get; set; }

    private static string AssemblyVersion => ShortenCommit(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown");

    /// <summary>"0.4.0-dev.33+af575b8f322e…" → "0.4.0-dev.33+af575b8", git's short form of the commit.</summary>
    internal static string ShortenCommit(string version)
    {
        var plus = version.IndexOf('+');
        var metadata = plus < 0 ? "" : version[(plus + 1)..];
        return metadata.Length > 7 && metadata.All(Uri.IsHexDigit) ? version[..(plus + 8)] : version;
    }

    /// <summary>Reads the installed version. Must run after VelopackApp.Build().Run(), which is what
    /// sets up the locator this asks.</summary>
    public static void Initialize()
    {
        try
        {
            if (VelopackLocator.IsCurrentSet && VelopackLocator.Current.CurrentlyInstalledVersion is { } installed)
            {
                _version = installed.ToString();
                _installKind = "installed";
                return;
            }
        }
        catch (Exception)
        {
            // Diagnostics only; fall back to the assembly version.
        }

        _version = AssemblyVersion;
        _installKind = "development";
    }

    /// <summary>The environment a bug report needs. Nothing personal: no user or machine name.</summary>
    public static IReadOnlyDictionary<string, string> Describe() => new Dictionary<string, string>
    {
        ["run"] = RunId,
        ["previousRun"] = PreviousRunId ?? "",
        ["version"] = Version,
        ["install"] = InstallKind,
        ["os"] = RuntimeInformation.OSDescription,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
        ["cpus"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
        ["culture"] = CultureInfo.CurrentCulture.Name,
        ["pid"] = ProcessId.ToString(CultureInfo.InvariantCulture),
        ["recovered"] = IsRecoveredLaunch ? "yes" : "no",
    };
}

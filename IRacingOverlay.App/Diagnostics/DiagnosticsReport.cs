using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.Diagnostics;

/// <summary>What the running app knows that a report needs. Published by the Control Panel once a
/// second as an immutable snapshot, so a crash on any thread can still describe the moment before.</summary>
public sealed record DiagnosticsContext(
    string? Session,
    ConnectionHealth? Telemetry,
    IReadOnlyList<string> Widgets,
    string? Performance);

/// <summary>
/// Builds the plain-text diagnostics report behind "Copy diagnostics", "Export report" and every
/// crash report: version, run and environment, the session, telemetry and component health, every
/// problem of the run with its reference, and the recent log. The user's profile path is masked so
/// a report can be pasted into a public issue.
/// </summary>
public static class DiagnosticsReport
{
    private const int MaxCrashReports = 20;
    private const long MaxExportedLogBytes = 25L * 1024 * 1024;
    private static volatile DiagnosticsContext? _latestContext;

    public static readonly string CrashDirectory = Path.Combine(AppLog.LogDirectory, "crashes");

    public static readonly string ExportDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "reports");

    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay");

    public static DiagnosticsContext? LatestContext
    {
        get => _latestContext;
        set => _latestContext = value;
    }

    public static string Build(string? reason = null, Exception? exception = null)
    {
        var text = new StringBuilder();
        var now = DateTime.UtcNow;
        text.AppendLine("OpenOverlay diagnostics report");
        Line(text, "Generated", $"{Utc(now)} (local {now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)})");
        if (reason is not null)
        {
            Line(text, "Reason", reason);
        }

        AppendApplication(text);

        var context = _latestContext;
        Section(text, "Session");
        text.AppendLine(context?.Session ?? "No session");

        AppendTelemetry(text, context?.Telemetry);
        AppendHealth(text, HealthMonitor.Latest);
        AppendProblems(text, AppLog.Problems());

        Section(text, "Widgets");
        foreach (var widget in context?.Widgets ?? [])
        {
            text.AppendLine(widget);
        }

        if (context?.Performance is { Length: > 0 } performance)
        {
            Section(text, "Performance");
            text.AppendLine(performance);
        }

        if (exception is not null)
        {
            Section(text, "Exception");
            text.AppendLine(ExceptionInfo.From(exception).ToString().TrimEnd());
        }

        var recent = AppLog.Recent();
        Section(text, $"Recent log ({recent.Count} entries, {AppLog.Current.TotalSuppressed} duplicates suppressed)");
        foreach (var entry in recent)
        {
            text.AppendLine(LogFormatter.ToText(entry));
        }

        return Redact(text.ToString());
    }

    /// <summary>Writes a crash report next to the logs and returns its path, or null if even that
    /// failed. Safe to call from any thread, including one that is about to take the process down.</summary>
    public static string? WriteCrashReport(string reason, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(CrashDirectory);
            var path = Path.Combine(CrashDirectory, $"crash-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{AppInfo.RunId}.txt");
            File.WriteAllText(path, Build(reason, exception), Encoding.UTF8);
            PruneCrashReports();
            return path;
        }
        catch (Exception e)
        {
            AppLog.Error("Diagnostics", "Could not write the crash report", e);
            return null;
        }
    }

    /// <summary>Zips the report, recent logs, crash reports and settings for attaching to an issue.</summary>
    public static string Export()
    {
        Directory.CreateDirectory(ExportDirectory);
        var path = Path.Combine(ExportDirectory, $"OpenOverlay-report-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip");
        AppLog.Flush(TimeSpan.FromSeconds(2));

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            AddText(zip, "report.txt", Build("Exported by the user"));

            long logBytes = 0;
            foreach (var log in Files(AppLog.LogDirectory, RollingFileSink.FilePrefix + "*.log"))
            {
                logBytes += log.Length;
                if (logBytes > MaxExportedLogBytes)
                {
                    break;
                }

                AddText(zip, "logs/" + log.Name, Redact(ReadShared(log.FullName)));
            }

            foreach (var crash in Files(CrashDirectory, "crash-*.txt").Take(10))
            {
                AddText(zip, "crashes/" + crash.Name, ReadShared(crash.FullName));
            }

            foreach (var setting in Files(ConfigDirectory, "*.json").Concat(Files(ConfigDirectory, "*.txt")).Where(f => f.Length < 1024 * 1024))
            {
                AddText(zip, "settings/" + setting.Name, ReadShared(setting.FullName));
            }
        }

        AppLog.Info("Diagnostics", "Diagnostics report exported", new Dictionary<string, string> { ["file"] = Path.GetFileName(path) });
        return path;
    }

    /// <summary>Masks the Windows profile folder, which carries the account name.</summary>
    public static string Redact(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 3 ? text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : text;
    }

    private static void AppendApplication(StringBuilder text)
    {
        Section(text, "Application");
        var environment = AppInfo.Describe();
        Line(text, "Version", $"{AppInfo.Version} ({AppInfo.InstallKind})");
        Line(text, "Run", AppInfo.PreviousRunId is { } previous
            ? $"{AppInfo.RunId}, relaunched automatically after run {previous}"
            : AppInfo.RunId);
        Line(text, "Started", $"{Utc(AppInfo.StartedUtc)}, up {Duration(AppInfo.Uptime)}");
        Line(text, "Process", $"{AppInfo.ProcessId}, recovered launch: {environment["recovered"]}");
        try
        {
            using var process = Process.GetCurrentProcess();
            Line(text, "Memory", string.Create(CultureInfo.InvariantCulture,
                $"working set {process.WorkingSet64 / (1024 * 1024)} MB, managed {GC.GetTotalMemory(false) / (1024 * 1024)} MB, GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"));
            Line(text, "Resources", string.Create(CultureInfo.InvariantCulture, $"{process.Threads.Count} threads, {process.HandleCount} handles"));
        }
        catch (Exception)
        {
            Line(text, "Memory", "unavailable");
        }

        Line(text, "OS", environment["os"]);
        Line(text, "Runtime", $"{environment["runtime"]} ({environment["arch"]}), {environment["cpus"]} CPUs, culture {environment["culture"]}");
    }

    private static void AppendTelemetry(StringBuilder text, ConnectionHealth? telemetry)
    {
        Section(text, "Telemetry");
        if (telemetry is null)
        {
            text.AppendLine("Not started");
            return;
        }

        var now = DateTime.UtcNow;
        Line(text, "State", telemetry.ConnectedSinceUtc is { } since ? $"{telemetry.State} (since {Utc(since)})" : telemetry.State.ToString());
        Line(text, "Last tick", telemetry.LastTickUtc is { } tick
            ? string.Create(CultureInfo.InvariantCulture, $"{(now - tick).TotalSeconds:0.0} s ago (tick {telemetry.LastTickCount}, {telemetry.TicksReceived:N0} received)")
            : "never");
        Line(text, "Reconnects", telemetry.Reconnects.ToString(CultureInfo.InvariantCulture));
        Line(text, "Failures", string.Create(CultureInfo.InvariantCulture,
            $"{telemetry.TotalFailures} total, {telemetry.ConsecutiveFailures} consecutive, {telemetry.LoopRestarts} reader restarts"));
        Line(text, "Session info", string.Create(CultureInfo.InvariantCulture,
            $"{telemetry.SessionInfoUpdates} updates, {telemetry.SessionInfoFailures} failures, degraded: {(telemetry.SessionInfoDegraded ? "yes" : "no")}"));
        if (telemetry.LastError is { } error)
        {
            Line(text, "Last error", telemetry.LastErrorUtc is { } at ? $"{error} ({Utc(at)})" : error);
        }
    }

    private static void AppendHealth(StringBuilder text, HealthReport health)
    {
        Section(text, $"Health: {health.Overall}");
        foreach (var component in health.Components.OrderByDescending(c => c.Status).ThenBy(c => c.Name, StringComparer.Ordinal))
        {
            var line = new StringBuilder()
                .Append('[').Append(component.Status.ToString().ToUpperInvariant().PadRight(8)).Append("] ")
                .Append(component.Name).Append(" — ").Append(component.Summary);
            if (component.Failures > 0)
            {
                line.Append(CultureInfo.InvariantCulture, $" | {component.Failures} failures, {component.Recoveries} recoveries");
            }

            if (component.LastSuccessUtc is { } ok)
            {
                line.Append(" | last OK ").Append(Utc(ok));
            }

            if (component.LastError is { } error)
            {
                line.Append(" | ").Append(error);
            }

            if (component.LastErrorRef is { } reference)
            {
                line.Append(" (ref ").Append(reference).Append(')');
            }

            text.AppendLine(line.ToString());
        }
    }

    /// <summary>Every kind of warning and error this run with how often it happened — the recent log
    /// only holds the last few hundred lines, this holds the whole run.</summary>
    private static void AppendProblems(StringBuilder text, IReadOnlyList<ProblemTally> problems)
    {
        Section(text, $"Problems this run (errors: {problems.Where(p => p.Level >= LogLevel.Error).Sum(p => p.Count)}, warnings: {problems.Where(p => p.Level == LogLevel.Warning).Sum(p => p.Count)})");
        if (problems.Count == 0)
        {
            text.AppendLine("None");
            return;
        }

        foreach (var problem in problems)
        {
            text.Append(problem.Level.ToString().ToUpperInvariant().PadRight(9))
                .Append(CultureInfo.InvariantCulture, $"x{problem.Count,-5} ")
                .Append(problem.Source).Append(" — ").Append(problem.Message);
            if (problem.ExceptionType is { } type)
            {
                text.Append(" (").Append(type).Append(')');
            }

            text.Append(" | first ").Append(Utc(problem.FirstUtc)).Append(", last ").Append(Utc(problem.LastUtc));
            if (problem.LastRef is { } reference)
            {
                text.Append(", ref ").Append(reference);
            }

            text.AppendLine();
        }
    }

    private static IEnumerable<FileInfo> Files(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory)
                ? new DirectoryInfo(directory).GetFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Opens files the logger (or anything else) may still have open for writing.</summary>
    private static string ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"<unreadable: {e.Message}>";
        }
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void PruneCrashReports()
    {
        foreach (var old in Files(CrashDirectory, "crash-*.txt").Skip(MaxCrashReports))
        {
            try
            {
                old.Delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Housekeeping only.
            }
        }
    }

    private static void Section(StringBuilder text, string title) => text.AppendLine().Append("== ").Append(title).AppendLine(" ==");

    private static void Line(StringBuilder text, string label, string value) => text.Append(label.PadRight(14)).AppendLine(value);

    private static string Utc(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan value) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}");
}

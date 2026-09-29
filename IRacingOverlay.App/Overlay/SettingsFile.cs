using System.IO;
using System.Text;
using System.Text.Json;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// Crash-safe persistence for every settings store. A write goes to a temporary file that then
/// replaces the real one in a single step, keeping the previous version as <c>.bak</c>; a read that
/// finds the file missing, unreadable or unparseable falls back to that backup. Neither ever throws:
/// a locked or full disk costs the change being remembered, not the overlay mid-race.
/// </summary>
internal static class SettingsFile
{
    private const int WriteAttempts = 3;
    private static long _writeFailures;
    private static volatile string? _lastWriteError;
    private static long _lastWriteFailureTicks;

    public static long WriteFailures => Interlocked.Read(ref _writeFailures);

    public static string? LastWriteError => _lastWriteError;

    public static DateTime? LastWriteFailureUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastWriteFailureTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>The parsed file, else the parsed backup, else null. <paramref name="parse"/> may
    /// throw or return null for content it rejects; both count as corrupt.</summary>
    public static T? Read<T>(string path, Func<string, T?> parse) where T : class
    {
        if (TryParse(path, parse, out var value))
        {
            return value;
        }

        var backup = path + ".bak";
        if (!TryParse(backup, parse, out value))
        {
            return null;
        }

        if (File.Exists(path))
        {
            // Set aside, or the next save would rotate the corrupt copy over the good backup.
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The restored values still get saved over it on the next write.
            }
        }

        AppLog.Warn("Settings", "Restored settings from backup", data: Name(path));
        return value;
    }

    public static T? ReadJson<T>(string path) where T : class => Read(path, json => JsonSerializer.Deserialize<T>(json));

    public static string? ReadText(string path) => Read(path, text => text);

    public static bool WriteJson<T>(string path, T value)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(value);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or JsonException)
        {
            RecordFailure(path, e);
            return false;
        }

        return Write(path, json);
    }

    public static bool Write(string path, string contents)
    {
        var temp = path + ".tmp";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temp, contents, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    Replace(temp, path);
                }
                else
                {
                    File.Move(temp, path);
                }

                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= WriteAttempts)
                {
                    RecordFailure(path, e);
                    TryDelete(temp);
                    return false;
                }

                // Antivirus, indexers and sync clients hold files open for a moment after a change.
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static void Replace(string temp, string path)
    {
        try
        {
            File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
        }
        catch (Exception e) when (e is PlatformNotSupportedException or IOException && File.Exists(temp) && File.Exists(path))
        {
            // Volumes without replace support: keep a backup by hand, then swap.
            File.Copy(path, path + ".bak", overwrite: true);
            File.Move(temp, path, overwrite: true);
        }
    }

    private static bool TryParse<T>(string path, Func<string, T?> parse, out T? value) where T : class
    {
        value = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            value = parse(File.ReadAllText(path));
            if (value is not null)
            {
                return true;
            }
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Warn("Settings", "Settings file unreadable", e, Name(path));
            return false;
        }

        AppLog.Warn("Settings", "Settings file empty or invalid", data: Name(path));
        return false;
    }

    private static void RecordFailure(string path, Exception exception)
    {
        Interlocked.Increment(ref _writeFailures);
        Interlocked.Exchange(ref _lastWriteFailureTicks, DateTime.UtcNow.Ticks);
        _lastWriteError = $"{Path.GetFileName(path)}: {exception.Message}";
        AppLog.Error("Settings", "Could not save settings; the change applies until the app closes", exception, Name(path));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Overwritten by the next attempt.
        }
    }

    private static Dictionary<string, string> Name(string path) => new() { ["file"] = Path.GetFileName(path) };
}

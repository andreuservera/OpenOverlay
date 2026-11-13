using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IRacingOverlay.Coaching.Reports;

/// <summary>
/// Saved session reports, one JSON file per session, for the Coaching page to list and open.
/// Default folder: %LOCALAPPDATA%\IRacingOverlay\coaching\sessions, next to the corner maps.
/// Writes go to a temporary file first, so a report being saved mid-session is never read half-written.
/// </summary>
public sealed class SessionReportStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public SessionReportStore(string directory)
    {
        Directory = directory;
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "coaching", "sessions");

    public string Directory { get; }

    public string PathFor(string id) => Path.Combine(Directory, FileName(id) + ".json");

    public void Save(SessionReport report)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(report.Id);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(report, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>One report, or null when it is missing, unreadable or from a newer format.</summary>
    public SessionReport? Load(string id) => LoadFile(PathFor(id));

    /// <summary>Every readable report, newest first. Files that can't be read are skipped.</summary>
    public IReadOnlyList<SessionReport> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(LoadFile)
            .OfType<SessionReport>()
            .OrderByDescending(r => r.StartedUtc)
            .ToList();
    }

    public bool Delete(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static SessionReport? LoadFile(string path)
    {
        try
        {
            var report = JsonSerializer.Deserialize<SessionReport>(File.ReadAllText(path), Json);
            return report is not null && report.Format <= SessionReport.CurrentFormat ? report : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A report id from when and where: "20261112-1830-spa-2024-up".</summary>
    public static string NewId(DateTime startedUtc, string? track, string? prefix = null)
    {
        var slug = Slug(track ?? "track");
        return $"{(prefix is null ? "" : prefix + "-")}{startedUtc:yyyyMMdd-HHmmss}-{slug}";
    }

    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "track" : s.Length > 60 ? s[..60] : s;
    }

    internal static string FileName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}

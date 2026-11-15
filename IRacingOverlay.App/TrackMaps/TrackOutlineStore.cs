using System.IO;
using System.Text;
using System.Text.Json;

namespace IRacingOverlay.App.TrackMaps;

/// <summary>
/// Learned track shapes, one JSON file per track (iRacing's TrackName, which names the layout too:
/// "spa up", "suzuka grandprix") in %LOCALAPPDATA%\IRacingOverlay\trackmaps.
/// </summary>
public sealed class TrackOutlineStore
{
    private const int Version = 1;

    private sealed record Snapshot(int Version, string Track, double LengthM, double ClosureShare, float[] X, float[] Y);

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "trackmaps");

    private readonly string _directory;

    public TrackOutlineStore(string? directory = null) => _directory = directory ?? DefaultDirectory;

    public string PathFor(string track) => Path.Combine(_directory, Slug(track) + ".json");

    /// <summary>The saved shape, or null when there is none or the file can't be read.</summary>
    public TrackOutline? Load(string track)
    {
        try
        {
            var path = PathFor(track);
            if (!File.Exists(path))
            {
                return null;
            }

            var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
            if (saved is null || saved.Version != Version || saved.X is not { Length: > 2 } || saved.Y?.Length != saved.X.Length)
            {
                return null;
            }

            return new TrackOutline(track, saved.X, saved.Y, saved.LengthM, saved.ClosureShare);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(TrackOutline outline)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(outline.Track);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(
            new Snapshot(Version, outline.Track, outline.LengthM, outline.ClosureShare, outline.X, outline.Y)));
        File.Move(temp, path, overwrite: true);
    }

    public bool Delete(string track)
    {
        var path = PathFor(track);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>The track name as a file name: lower case, anything but letters and digits as "-".</summary>
    public static string Slug(string track)
    {
        var slug = new StringBuilder(track.Length);
        foreach (var c in track.Trim().ToLowerInvariant())
        {
            slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        return slug.Length == 0 ? "track" : slug.ToString();
    }
}

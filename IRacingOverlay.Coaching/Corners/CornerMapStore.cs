using System.Text.Json;
using System.Text.Json.Serialization;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// Saved corner maps, one JSON file per track layout, so a corner keeps its T-number and name from one
/// session to the next and across cars. The file is meant to be edited by hand: set a corner's
/// <c>name</c> ("La Source") and it is shown and spoken instead of its T-number.
///
/// Default folder: %LOCALAPPDATA%\IRacingOverlay\coaching\corners. The key is iRacing's internal
/// track name (WeekendInfo.TrackName, e.g. "spa 2024 up"), which is different for every layout.
/// </summary>
public sealed class CornerMapStore
{
    /// <summary>Version of the corner detection that made a map. Bump it whenever detection changes
    /// where corners start or end: a map saved by an older version is then relearned (its names are
    /// kept) instead of being trusted forever.</summary>
    public const int DetectorVersion = 2;

    // A saved map is ignored if the track is now this much longer or shorter (layout changed).
    private const int MaxLengthDifferenceMetres = 25;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public CornerMapStore(string directory)
    {
        Directory = directory;
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "coaching", "corners");

    public string Directory { get; }

    public string PathFor(string trackKey) => Path.Combine(Directory, FileName(trackKey) + ".json");

    /// <summary>The saved map for a track, or null if there is none, it can't be read, the track
    /// length no longer matches, or it was made by an older <see cref="DetectorVersion"/>.</summary>
    public CornerMap? Load(string trackKey, int? trackLengthMetres = null) => Load(trackKey, trackLengthMetres, anyVersion: false);

    private CornerMap? Load(string trackKey, int? trackLengthMetres, bool anyVersion)
    {
        var path = PathFor(trackKey);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<CornerMapFile>(File.ReadAllText(path), Json);
            if (file is null || file.Corners.Count == 0 || (!anyVersion && file.DetectorVersion < DetectorVersion))
            {
                return null;
            }

            if (trackLengthMetres is { } length && Math.Abs(length - file.TrackLengthMetres) > MaxLengthDifferenceMetres)
            {
                return null;
            }

            var corners = file.Corners
                .OrderBy(c => c.ApexM)
                .Select(c => new Corner(c.Id, c.StartM, c.ApexM, c.EndM) { Label = string.IsNullOrWhiteSpace(c.Name) ? null : c.Name!.Trim() })
                .ToList();
            return new CornerMap(file.TrackLengthMetres, corners, file.LapsUsed);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves a map, keeping any names already set for corners at the same place.</summary>
    public void Save(string trackKey, CornerMap map)
    {
        // Names from any older map, even one being replaced because its detector was out of date.
        var previous = Load(trackKey, null, anyVersion: true);
        var file = new CornerMapFile
        {
            Track = trackKey,
            DetectorVersion = DetectorVersion,
            TrackLengthMetres = map.TrackLengthMetres,
            LapsUsed = map.LapsUsed,
            Corners = map.Corners.Select(c => new CornerEntry
            {
                Id = c.Name,
                Name = (c.LabelIsLookedUp ? null : c.Label) ?? previous?.Corners.FirstOrDefault(p => Math.Abs(p.ApexM - c.ApexM) <= CornerMapBuilder.ClusterRadiusMetres)?.Label,
                StartM = c.StartM,
                ApexM = c.ApexM,
                EndM = c.EndM,
            }).ToList(),
        };

        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(trackKey);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, path, overwrite: true);
    }

    internal static string FileName(string trackKey)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = trackKey.Trim().ToLowerInvariant().Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch).ToArray();
        var name = new string(chars).Trim('-');
        return name.Length == 0 ? "unknown-track" : name;
    }

    private sealed class CornerMapFile
    {
        public string Track { get; set; } = "";

        /// <summary>0 in files written before versions were recorded.</summary>
        public int DetectorVersion { get; set; }
        public int TrackLengthMetres { get; set; }
        public int LapsUsed { get; set; }
        public List<CornerEntry> Corners { get; set; } = [];
    }

    private sealed class CornerEntry
    {
        public string Id { get; set; } = "";

        /// <summary>The corner's real name; edit the file to set it.</summary>
        public string? Name { get; set; }

        public int StartM { get; set; }
        public int ApexM { get; set; }
        public int EndM { get; set; }
    }
}

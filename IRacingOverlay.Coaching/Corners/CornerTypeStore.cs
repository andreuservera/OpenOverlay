using System.Text.Json;
using System.Text.Json.Serialization;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// Each corner's type, saved per track and car once a session has settled it
/// (<see cref="CornerTypes.SettledAfterPasses"/> clean passes), so a borderline corner (Degner 2
/// at 111 km/h) keeps the same type in every later session with that car and reports stay
/// comparable. The map is per track; the type is per car, because the same bend can be a lift in
/// a GT3 and a braking corner in a slower car. Corners are matched by where their apex is, so a
/// relearned map keeps the types of corners that didn't move.
///
/// A type is only saved when the passes clearly agree on it (<see cref="CornerTypes.SavedShare"/>),
/// and a saved type is agreed again when a later session's passes clearly show another one.
///
/// One small JSON file per track and car, meant to be editable by hand: change a corner's
/// <c>type</c> and set <c>"fixed": true</c> to keep it whatever the passes say, or delete the file
/// to have the types agreed again. Default folder:
/// %LOCALAPPDATA%\IRacingOverlay\coaching\corner-types.
/// </summary>
public sealed class CornerTypeStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public CornerTypeStore(string directory)
    {
        Directory = directory;
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "coaching", "corner-types");

    public string Directory { get; }

    public string PathFor(string trackKey, string car) =>
        Path.Combine(Directory, CornerMapStore.FileName(trackKey) + "__" + CornerMapStore.FileName(car) + ".json");

    /// <summary>The saved types of <paramref name="map"/>'s corners, by corner id; corners with no
    /// saved type (or a file that can't be read) are left out.</summary>
    public IReadOnlyDictionary<string, CornerType> Load(string trackKey, string car, CornerMap map)
    {
        var result = new Dictionary<string, CornerType>(StringComparer.Ordinal);
        foreach (var e in Read(trackKey, car))
        {
            if (Match(map, e.ApexM) is { } c && !result.ContainsKey(c.Name))
            {
                result[c.Name] = e.Type;
            }
        }

        return result;
    }

    /// <summary>The corners of <paramref name="map"/> whose saved type is <c>fixed</c> (set by hand), by id.</summary>
    public IReadOnlySet<string> Fixed(string trackKey, string car, CornerMap map) =>
        Read(trackKey, car).Where(e => e.Fixed).Select(e => Match(map, e.ApexM)?.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);

    /// <summary>Saves the types of <paramref name="map"/>'s corners. A corner that already has a
    /// type keeps it unless <paramref name="replace"/> is true; a <c>fixed</c> type is never replaced.</summary>
    public void Save(string trackKey, string car, CornerMap map, IReadOnlyDictionary<string, CornerType> types, bool replace = false)
    {
        var entries = Read(trackKey, car).ToList();
        var changed = false;
        foreach (var c in map.Corners)
        {
            if (!types.TryGetValue(c.Name, out var type))
            {
                continue;
            }

            var existing = entries.FirstOrDefault(e => Math.Abs(e.ApexM - c.ApexM) <= CornerMapBuilder.ClusterRadiusMetres);
            if (existing is not null)
            {
                if (!replace || existing.Fixed || existing.Type == type)
                {
                    continue;
                }

                entries.Remove(existing);
            }

            entries.Add(new Entry { Id = c.Name, Name = c.Label, ApexM = c.ApexM, Type = type });
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(trackKey, car);
        var temp = path + ".tmp";
        var file = new TypeFile { Track = trackKey, Car = car, Corners = entries.OrderBy(e => e.ApexM).ToList() };
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string trackKey, string car)
    {
        var path = PathFor(trackKey, car);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static Corner? Match(CornerMap map, int apexM) =>
        map.Corners.Where(c => Math.Abs(c.ApexM - apexM) <= CornerMapBuilder.ClusterRadiusMetres).MinBy(c => Math.Abs(c.ApexM - apexM));

    private IEnumerable<Entry> Read(string trackKey, string car)
    {
        var path = PathFor(trackKey, car);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<TypeFile>(File.ReadAllText(path), Json)?.Corners ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private sealed class TypeFile
    {
        public string Track { get; set; } = "";

        public string Car { get; set; } = "";

        public List<Entry> Corners { get; set; } = [];
    }

    private sealed class Entry
    {
        public string Id { get; set; } = "";

        /// <summary>The corner's name when it had one, to help editing by hand; not read back.</summary>
        public string? Name { get; set; }

        public int ApexM { get; set; }

        public CornerType Type { get; set; }

        /// <summary>Set by hand to keep <see cref="Type"/> whatever later passes show.</summary>
        public bool Fixed { get; set; }
    }
}

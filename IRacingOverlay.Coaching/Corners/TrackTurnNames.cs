using System.Text.Json;
using System.Text.Json.Serialization;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// Real turn names for the corners the coach detects, from Lovely Track Data files (one per iRacing
/// layout, named after <c>WeekendInfo.TrackName</c>, turns as fractions of the lap; see
/// TrackData/README.md for the source and licence).
///
/// A detected corner is often a complex the track calls two turns (Eau Rouge and Raidillon) or part
/// of one long named turn. It takes the names of the turns whose middle lies inside its window, in
/// lap order ("Eau Rouge and Raidillon"); failing that, the turn its apex is in; failing that, the
/// turn it overlaps most. Corners that end up with the same name are numbered in lap order
/// ("Degner 1", "Degner 2"). A name typed into the saved corner map always wins.
/// </summary>
public sealed class TrackTurnNames
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly string _directory;

    public TrackTurnNames(string directory)
    {
        _directory = directory;
    }

    /// <summary>The iRacing files shipped next to the coaching assembly.</summary>
    public static TrackTurnNames Default { get; } = new(Path.Combine(AppContext.BaseDirectory, "TrackData", "iracing"));

    /// <summary>The named turns for a layout, in lap order; empty when the track isn't covered.</summary>
    public IReadOnlyList<NamedTurn> For(string trackKey)
    {
        var path = Path.Combine(_directory, CornerMapStore.FileName(trackKey) + ".json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var file = JsonSerializer.Deserialize<TrackFile>(File.ReadAllText(path), Json);
            return file?.Turn?
                .Where(t => !string.IsNullOrWhiteSpace(t.Name) && t.End > t.Start)
                .OrderBy(t => t.Start)
                .Select(t => new NamedTurn(t.Name!.Trim(), t.Start, t.End))
                .ToList() ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The map with names filled in for corners that have none; unchanged when the track
    /// isn't covered.</summary>
    public CornerMap Apply(CornerMap map, string trackKey)
    {
        var turns = For(trackKey);
        return turns.Count == 0 ? map : Apply(map, turns);
    }

    internal static CornerMap Apply(CornerMap map, IReadOnlyList<NamedTurn> turns)
    {
        var length = (double)map.TrackLengthMetres;
        var corners = map.Corners.Select(c =>
        {
            if (!string.IsNullOrWhiteSpace(c.Label))
            {
                return c;
            }

            var name = NameFor(c.StartM / length, c.ApexM / length, c.EndM / length, turns);
            return name is null ? c : c with { Label = name, LabelIsLookedUp = true };
        }).ToList();

        // One long named turn detected as several corners (Degner, Spoon, Les Fagnes): number the
        // parts in lap order, so "Degner 1" and "Degner 2" can be told apart when spoken.
        foreach (var group in corners.Where(c => c.LabelIsLookedUp).GroupBy(c => c.Label).Where(g => g.Count() > 1).ToList())
        {
            var part = 1;
            foreach (var c in group.ToList())
            {
                corners[corners.IndexOf(c)] = c with { Label = $"{c.Label} {part++}" };
            }
        }

        return new CornerMap(map.TrackLengthMetres, corners, map.LapsUsed);
    }

    private static string? NameFor(double start, double apex, double end, IReadOnlyList<NamedTurn> turns)
    {
        var inside = turns.Where(t => (t.Start + t.End) / 2 is var mid && mid >= start && mid <= end).Take(2).ToList();
        if (inside.Count > 0)
        {
            return string.Join(" and ", inside.Select(t => t.Name));
        }

        var atApex = turns.FirstOrDefault(t => apex >= t.Start && apex <= t.End);
        if (atApex is not null)
        {
            return atApex.Name;
        }

        var best = turns
            .Select(t => (Turn: t, Overlap: Math.Min(end, t.End) - Math.Max(start, t.Start)))
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .FirstOrDefault();
        return best.Turn?.Name;
    }

    private sealed class TrackFile
    {
        public List<TurnEntry>? Turn { get; set; }
    }

    private sealed class TurnEntry
    {
        public string? Name { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }
    }
}

/// <summary>A turn as the track names it, from <paramref name="Start"/> to <paramref name="End"/> as
/// fractions of the lap.</summary>
public sealed record NamedTurn(string Name, double Start, double End);

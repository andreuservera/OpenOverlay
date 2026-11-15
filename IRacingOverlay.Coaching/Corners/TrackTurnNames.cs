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

    /// <summary>The map with the named turns the detector missed added (see <see cref="AddMissed"/>)
    /// and names filled in for corners that have none; unchanged when the track isn't covered.</summary>
    public CornerMap Apply(CornerMap map, string trackKey)
    {
        var turns = For(trackKey);
        return turns.Count == 0 ? map : Apply(map, turns);
    }

    /// <summary>A named turn counts as found when the detected corners cover at least this share of it.</summary>
    public const double FoundShare = 0.3;

    /// <summary>Named turns that are missed and closer than this to each other are one corner (Eau
    /// Rouge and Raidillon), metres.</summary>
    public const int JoinGapMetres = 60;

    /// <summary>A missed turn left shorter than this once kept clear of its neighbours is dropped, metres.</summary>
    public const int MinAddedMetres = 40;

    /// <summary>
    /// Adds the named turns the detector didn't find. It only finds corners the driver slows for, so
    /// a turn taken flat or with a small lift (Eau Rouge, Blanchimont) is missing; the track data
    /// knows it is there. Each one becomes a corner over the turn's own stretch, kept clear of the
    /// detected corners either side, and the corners are numbered T1…Tn again in lap order.
    /// </summary>
    internal static CornerMap AddMissed(CornerMap map, IReadOnlyList<NamedTurn> turns)
    {
        var length = (double)map.TrackLengthMetres;
        var detected = map.Corners.ToList();
        var missed = turns
            .Select(t => (Start: (int)Math.Round(t.Start * length), End: (int)Math.Round(t.End * length)))
            .Where(t => t.End > t.Start && t.End < map.TrackLengthMetres)
            .Where(t => detected.Sum(c => Math.Max(0, Math.Min(c.EndM, t.End) - Math.Max(c.StartM, t.Start))) < FoundShare * (t.End - t.Start))
            .ToList();
        if (missed.Count == 0)
        {
            return map;
        }

        var joined = new List<(int Start, int End)>();
        foreach (var t in missed)
        {
            if (joined.Count > 0 && t.Start - joined[^1].End <= JoinGapMetres
                && !detected.Any(c => c.StartM < t.Start && c.EndM > joined[^1].End))
            {
                joined[^1] = (joined[^1].Start, Math.Max(joined[^1].End, t.End));
            }
            else
            {
                joined.Add(t);
            }
        }

        var added = new List<Corner>();
        foreach (var (start, end) in joined)
        {
            var from = detected.Where(c => c.StartM <= start).Select(c => c.EndM + 1).DefaultIfEmpty(0).Max();
            var to = detected.Where(c => c.EndM >= end).Select(c => c.StartM - 1).DefaultIfEmpty(map.TrackLengthMetres - 1).Min();
            var s = Math.Max(start, from);
            var e = Math.Min(end, to);
            if (e - s >= MinAddedMetres)
            {
                added.Add(new Corner("", s, (s + e) / 2, e));
            }
        }

        if (added.Count == 0)
        {
            return map;
        }

        var all = detected.Concat(added).OrderBy(c => c.StartM).Select((c, i) => c with { Name = $"T{i + 1}" }).ToList();
        return new CornerMap(map.TrackLengthMetres, all, map.LapsUsed);
    }

    internal static CornerMap Apply(CornerMap map, IReadOnlyList<NamedTurn> turns)
    {
        map = AddMissed(map, turns);
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

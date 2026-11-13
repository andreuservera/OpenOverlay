using System.Globalization;
using System.Text.Json;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Reports;

namespace IRacingOverlay.Coaching.History;

/// <summary>One corner to work on next session, with what to do and what it is worth.</summary>
/// <param name="Gain">What a normal lap loses here now, seconds: the time on the table.</param>
/// <param name="Target">The time to aim to win back next session, seconds (part of <paramref name="Gain"/>).</param>
/// <param name="Habit">The recurring mistake to fix; null when the time goes somewhere no single finding explains.</param>
/// <param name="Pinned">Chosen by the driver rather than picked by the coach.</param>
public sealed record FocusCorner(string CornerId, string Name, int ApexM, double? Gain, double? Target, FindingTally? Habit, bool Pinned)
{
    /// <summary>In words, in English: "T1: brake 10 metres later, aim for 0.15 s".</summary>
    public string Text
    {
        get
        {
            var what = Habit?.Advice is { Length: > 0 } a ? char.ToLowerInvariant(a[0]) + a[1..] : "compare your best and typical traces";
            var aim = Target is { } t ? $", aim for {t.ToString("0.00", CultureInfo.InvariantCulture)} s" : "";
            return $"{Name}: {what}{aim}";
        }
    }
}

/// <summary>
/// What to practise next session at a track with a car: the one or two corners with the most time
/// to gain over recent sessions, each with its recurring mistake and a target, or the corners the
/// driver pinned on the Coaching page instead. The live coach talks about these corners first.
/// </summary>
public static class FocusPlanner
{
    /// <summary>Corners picked when none is pinned.</summary>
    public const int MaxCorners = 2;

    /// <summary>A corner that loses less than this on a normal lap isn't worth a session's focus, seconds.</summary>
    public const double MinGainSeconds = 0.05;

    /// <summary>The share of the normal loss to aim to win back in one session.</summary>
    public const double TargetShare = 0.5;

    public static IReadOnlyList<FocusCorner> Plan(TrackHistory history, IReadOnlyCollection<int>? pinnedApexM = null)
    {
        FocusCorner Item(CornerHistory c, bool pinned)
        {
            double? target = c.RecentLoss is { } g && g >= MinGainSeconds ? Math.Max(MinGainSeconds, Math.Round(g * TargetShare / 0.05) * 0.05) : null;
            return new FocusCorner(c.Id, c.Name, c.ApexM, c.RecentLoss, target, c.Habit, pinned);
        }

        var pinned = pinnedApexM is null ? [] : history.Corners
            .Where(c => pinnedApexM.Any(a => Math.Abs(a - c.ApexM) <= CornerMapBuilder.ClusterRadiusMetres))
            .Select(c => Item(c, true))
            .ToList();
        if (pinned.Count > 0)
        {
            return pinned;
        }

        return history.Corners
            .Where(c => c.RecentLoss is { } g && g >= MinGainSeconds)
            .OrderByDescending(c => c.RecentLoss)
            .Take(MaxCorners)
            .Select(c => Item(c, false))
            .ToList();
    }
}

/// <summary>
/// The corners the driver pinned to work on, per track and car, as small JSON files. Default
/// folder: %LOCALAPPDATA%\IRacingOverlay\coaching\focus.
/// </summary>
public sealed class FocusStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public FocusStore(string directory)
    {
        Directory = directory;
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "coaching", "focus");

    public string Directory { get; }

    public string PathFor(string trackKey, string car) =>
        Path.Combine(Directory, CornerMapStore.FileName(trackKey) + "__" + CornerMapStore.FileName(car) + ".json");

    /// <summary>Where the pinned corners' apexes are, metres from the line.</summary>
    public IReadOnlyList<int> Pinned(string trackKey, string car) => Read(trackKey, car).Select(p => p.ApexM).ToList();

    public bool IsPinned(string trackKey, string car, int apexM) =>
        Read(trackKey, car).Any(p => Math.Abs(p.ApexM - apexM) <= CornerMapBuilder.ClusterRadiusMetres);

    /// <summary>Pins or unpins a corner.</summary>
    public void Set(string trackKey, string car, string cornerId, string name, int apexM, bool pinned)
    {
        var list = Read(trackKey, car).Where(p => Math.Abs(p.ApexM - apexM) > CornerMapBuilder.ClusterRadiusMetres).ToList();
        if (pinned)
        {
            list.Add(new Pin { Id = cornerId, Name = name, ApexM = apexM });
        }

        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(trackKey, car);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FocusFile { Track = trackKey, Car = car, Pinned = list.OrderBy(p => p.ApexM).ToList() }, Json));
        File.Move(temp, path, overwrite: true);
    }

    private List<Pin> Read(string trackKey, string car)
    {
        var path = PathFor(trackKey, car);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<FocusFile>(File.ReadAllText(path), Json)?.Pinned ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private sealed class FocusFile
    {
        public string Track { get; set; } = "";

        public string Car { get; set; } = "";

        public List<Pin> Pinned { get; set; } = [];
    }

    private sealed class Pin
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public int ApexM { get; set; }
    }
}

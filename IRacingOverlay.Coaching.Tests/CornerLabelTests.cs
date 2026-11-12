using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public sealed class CornerLabelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coaching-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static List<RecordedLap> CleanLaps(TrackSimulator track, int count, IReadOnlyDictionary<int, double>? cornerSpeed = null)
    {
        var profile = track.SpeedProfile(cornerSpeed);
        var recorder = new LapRecorder();
        foreach (var f in track.Drive(Enumerable.Repeat(profile, count + 1).ToList()))
        {
            recorder.Observe(f);
        }

        return recorder.Laps.Where(l => l.IsClean).ToList();
    }

    [Fact]
    public void TwoApexesCloserThanTheMinimumGapAreOneCorner()
    {
        // A chicane: two tight turns 40 m apart, no time to accelerate between them.
        var chicane = new TrackSimulator(
            new TrackPiece(500),
            new TrackPiece(30, 30),
            new TrackPiece(40),
            new TrackPiece(30, 30),
            new TrackPiece(900));

        var map = CornerDetector.Detect(CleanLaps(chicane, 1)[0].Trace!);

        var corner = Assert.Single(map.Corners);
        Assert.True(corner.StartM < chicane.PieceStart(1));
        Assert.True(corner.EndM > chicane.PieceStart(3) + 30);
    }

    [Fact]
    public void AConsensusMapIgnoresACornerOnlyOneLapShows()
    {
        var standard = TrackSimulator.Standard();
        // Same layout, but the kink (piece 5) is tight enough to brake for: a "corner" on this lap only.
        var odd = new TrackSimulator(standard.Pieces.Select((p, i) => i == 5 ? p with { Radius = 120 } : p).ToArray());
        var traces = CleanLaps(standard, 4).Select(l => l.Trace!).Append(CleanLaps(odd, 1)[0].Trace!).ToList();

        Assert.Equal(4, CornerDetector.Detect(traces[^1]).Corners.Count);
        var map = CornerMapBuilder.Build(traces);

        Assert.Equal(3, map.Corners.Count);
        Assert.Equal(5, map.LapsUsed);
    }

    [Fact]
    public void ASavedMapKeepsItsNamesWhenSavedAgain()
    {
        var store = new CornerMapStore(_dir);
        var map = CornerDetector.Detect(CleanLaps(TrackSimulator.Standard(), 1)[0].Trace!);
        store.Save("test track up", map);

        // The driver names T1 by editing the file.
        var path = store.PathFor("test track up");
        File.WriteAllText(path, NameFirst(File.ReadAllText(path)));

        var loaded = store.Load("test track up")!;
        Assert.Equal("La Source", loaded.Corners[0].Label);
        Assert.Equal("La Source", loaded.Corners[0].DisplayName);
        Assert.Equal("T2", loaded.Corners[1].DisplayName);

        // Saving a freshly detected map (no names) keeps the name for the corner at the same place.
        store.Save("test track up", map);
        Assert.Equal("La Source", store.Load("test track up")!.Corners[0].Label);
    }

    [Fact]
    public void ASavedMapForADifferentLengthIsIgnored()
    {
        var store = new CornerMapStore(_dir);
        var map = CornerDetector.Detect(CleanLaps(TrackSimulator.Standard(), 1)[0].Trace!);
        store.Save("track", map);

        Assert.NotNull(store.Load("track", map.TrackLengthMetres + 10));
        Assert.Null(store.Load("track", map.TrackLengthMetres + 100));
    }

    [Fact]
    public void TheNextSessionUsesTheSavedMapWithTheSameIdsAndNames()
    {
        var store = new CornerMapStore(_dir);
        var track = TrackSimulator.Standard();
        var normal = track.SpeedProfile();

        var first = new CoachingSession(new CoachingOptions { TrackKey = "test track", Store = store, CornerMapLaps = 3 });
        foreach (var f in track.Drive([normal, normal, normal, normal]))
        {
            first.Observe(f);
        }

        Assert.Equal(CornerMapOrigin.Consensus, first.CornerMapOrigin);
        Assert.True(File.Exists(store.PathFor("test track")));
        var path = store.PathFor("test track");
        File.WriteAllText(path, NameFirst(File.ReadAllText(path)));

        // Next session, slower everywhere: detection alone would move the windows; the saved map doesn't.
        var slower = track.SpeedProfile(new Dictionary<int, double> { [1] = 0.9, [3] = 0.9, [7] = 0.9 });
        var second = new CoachingSession(new CoachingOptions { TrackKey = "test track", Store = store });
        foreach (var f in track.Drive([slower, slower]))
        {
            second.Observe(f);
        }

        Assert.Equal(CornerMapOrigin.Saved, second.CornerMapOrigin);
        Assert.Equal(first.CornerMap!.Corners.Select(c => (c.Name, c.StartM, c.ApexM, c.EndM)), second.CornerMap!.Corners.Select(c => (c.Name, c.StartM, c.ApexM, c.EndM)));
        Assert.Equal("La Source", second.CornerMap.Corners[0].DisplayName);
        Assert.Equal("La Source", second.Analyses[0].Passes[0].Corner.DisplayName);
    }

    [Fact]
    public void ExitSpeedIsNeverReadInsideTheNextCorner()
    {
        var track = new TrackSimulator(
            new TrackPiece(500),
            new TrackPiece(60, 30),
            new TrackPiece(140),
            new TrackPiece(60, 30),
            new TrackPiece(900));
        var lap = CleanLaps(track, 1)[0].Trace!;
        var map = CornerDetector.Detect(lap);
        Assert.Equal(2, map.Corners.Count);

        var t1 = CornerAnalyzer.Analyze(lap, map, 1)[0];

        Assert.True(t1.ExitSpeedPlus100 >= t1.ExitSpeed, $"exit {t1.ExitSpeed}, +100 {t1.ExitSpeedPlus100}");
    }

    // Sets the first corner's name in a saved map file, as a driver editing it would.
    private static string NameFirst(string json)
    {
        var i = json.IndexOf("\"name\": null", StringComparison.Ordinal);
        return json[..i] + "\"name\": \"La Source\"" + json[(i + "\"name\": null".Length)..];
    }
}

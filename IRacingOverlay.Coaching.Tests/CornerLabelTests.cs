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

    [Fact]
    public void AComplexOnlySomeLapsSplitKeepsItsWholeWindowInTheAgreedMap()
    {
        // Bus Stop over three laps: one sees one valley, two see two halves, and where the first
        // half's slowest point falls moves from lap to lap. The first half is where the heavy braking is, so the
        // agreed corner must start there, not at the second half.
        var whole = new CornerCandidate(6496, 6727, 6776, 18f);
        CornerCandidate FirstHalf(int apex) => new(6496, apex, 6714, 20f);
        var secondHalf = new CornerCandidate(6714, 6727, 6776, 18f);
        List<List<CornerCandidate>> laps =
        [
            [whole], [FirstHalf(6650), secondHalf], [FirstHalf(6705), secondHalf],
        ];

        var map = CornerMapBuilder.Agree(laps, 7000, new CornerDetectionOptions());

        var corner = Assert.Single(map.Corners);
        Assert.Equal(6496, corner.StartM);
        Assert.Equal(6727, corner.ApexM);
        Assert.Equal(6776, corner.EndM);
    }

    [Fact]
    public void AMapFromAnOlderDetectorIsRelearnedButKeepsItsNames()
    {
        var store = new CornerMapStore(_dir);
        store.Save("spa", new CornerMap(7000, [new Corner("T1", 100, 200, 300) { Label = "La Source" }]));
        var path = store.PathFor("spa");
        File.WriteAllText(path, File.ReadAllText(path).Replace($"\"detectorVersion\": {CornerMapStore.DetectorVersion}", "\"detectorVersion\": 1"));

        Assert.Null(store.Load("spa"));

        store.Save("spa", new CornerMap(7000, [new Corner("T1", 90, 210, 320)]));
        Assert.Equal("La Source", store.Load("spa")!.Corners[0].Label);
    }

    [Fact]
    public void DetectedCornersTakeTheTrackDataNamesTheyCover()
    {
        // Spa, 6930 m: La Source 0.046–0.077, Eau Rouge 0.138–0.155, Raidillon 0.156–0.19.
        var turns = TrackTurnNames.Default.For("spa 2024 up");
        Assert.Contains(turns, t => t.Name == "La Source");

        var map = new CornerMap(6930,
        [
            new Corner("T1", 256, 391, 468),                          // inside La Source
            new Corner("T2", 900, 1150, 1330),                        // takes in Eau Rouge and Raidillon
            new Corner("T3", 3191, 3263, 3349) { Label = "My name" }, // a typed name wins
            new Corner("T4", 5900, 5950, 6000),                       // no named turn there
        ]);

        var named = TrackTurnNames.Default.Apply(map, "spa 2024 up").Corners;
        Corner At(int start) => named.Single(c => c.StartM == start);

        Assert.Equal("La Source", At(256).Label);
        Assert.True(At(256).LabelIsLookedUp);
        Assert.Equal("Eau Rouge and Raidillon", At(900).Label);
        Assert.Equal("My name", At(3191).Label);
        Assert.False(At(3191).LabelIsLookedUp);
        Assert.Null(At(5900).Label);
    }

    [Fact]
    public void NamedTurnsTakenFlatAreAddedAsCorners()
    {
        // The detector only found the corners the driver slowed for; Eau Rouge, Raidillon and
        // Blanchimont are flat in a GT3.
        var turns = TrackTurnNames.Default.For("spa 2024 up");
        var map = new CornerMap(6930,
        [
            new Corner("T1", 256, 391, 468),
            new Corner("T2", 2250, 2400, 2560),
        ]);

        var corners = TrackTurnNames.Apply(map, turns).Corners;

        Assert.Contains(corners, c => c.Label == "Eau Rouge and Raidillon");
        Assert.Contains(corners, c => c.Label == "Blanchimont");
        Assert.Equal(corners.Select((_, i) => $"T{i + 1}"), corners.Select(c => c.Name));
        Assert.Equal(corners.OrderBy(c => c.StartM).Select(c => c.StartM), corners.Select(c => c.StartM));
        // Added corners never overlap the detected ones.
        Assert.All(corners.Zip(corners.Skip(1)), p => Assert.True(p.First.EndM < p.Second.StartM));
        Assert.Equal("La Source", corners[0].Label);
    }

    [Fact]
    public void PartsOfOneLongNamedTurnAreNumbered()
    {
        var map = new CornerMap(1000, [new Corner("T1", 100, 150, 200), new Corner("T2", 300, 350, 400), new Corner("T3", 600, 650, 700)]);
        var turns = new List<NamedTurn> { new("Degner", 0.08, 0.45), new("Hairpin", 0.6, 0.7) };

        var named = TrackTurnNames.Apply(map, turns).Corners.Select(c => c.Label);

        Assert.Equal(["Degner 1", "Degner 2", "Hairpin"], named);
    }

    [Fact]
    public void LookedUpNamesAreNotWrittenIntoTheSavedMap()
    {
        var store = new CornerMapStore(_dir);
        store.Save("spa", new CornerMap(6930, [new Corner("T1", 256, 391, 468) { Label = "La Source", LabelIsLookedUp = true }]));

        Assert.Null(store.Load("spa")!.Corners[0].Label);
        Assert.Empty(TrackTurnNames.Default.For("no such track"));
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

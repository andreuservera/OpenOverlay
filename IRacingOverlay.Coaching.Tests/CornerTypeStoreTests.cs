using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public sealed class CornerTypeStoreTests : IDisposable
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coach-types-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static CoachingSession Drive(int laps, CornerTypeStore store, string car = "GT3")
    {
        var session = new CoachingSession(new CoachingOptions { CornerMapLaps = 3, TrackKey = "spa", Car = car, TypeStore = store });
        var normal = Track.SpeedProfile();
        foreach (var f in Track.Drive(Enumerable.Repeat(normal, laps).ToList()))
        {
            session.Observe(f);
        }

        session.Flush();
        return session;
    }

    [Fact]
    public void SettledTypesAreSavedPerTrackAndCar()
    {
        var store = new CornerTypeStore(_dir);
        var few = Drive(3, store);
        Assert.False(File.Exists(store.PathFor("spa", "GT3")));
        Assert.Empty(few.SavedCornerTypes);

        var session = Drive(7, store);
        Assert.True(File.Exists(store.PathFor("spa", "GT3")));
        var saved = store.Load("spa", "GT3", session.CornerMap!);
        Assert.Equal(session.CornerMap!.Corners.Count, saved.Count);
        foreach (var (id, type) in saved)
        {
            Assert.Equal(session.CornerTypeById[id], type);
        }

        Assert.Empty(store.Load("spa", "MX-5", session.CornerMap));
    }

    private void FixByHand(CornerTypeStore store, CornerMap map, string id, CornerType type, bool isFixed)
    {
        store.Delete("spa", "GT3");
        store.Save("spa", "GT3", map, new Dictionary<string, CornerType> { [id] = type });
        if (isFixed)
        {
            var path = store.PathFor("spa", "GT3");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"fixed\": false", "\"fixed\": true"));
        }
    }

    [Fact]
    public void ASavedTypeIsAgreedAgainWhenALaterSessionClearlyShowsAnother()
    {
        var store = new CornerTypeStore(_dir);
        var first = Drive(7, store);
        var map = first.CornerMap!;
        var corner = map.Corners[0];
        var real = first.CornerTypeById[corner.Name];
        var other = real == CornerType.Complex ? CornerType.Flat : CornerType.Complex;
        FixByHand(store, map, corner.Name, other, isFixed: false);

        var second = Drive(7, store);

        Assert.Equal(real, second.CornerTypeById[corner.Name]);
        Assert.Equal(real, store.Load("spa", "GT3", map)[corner.Name]);
    }

    [Fact]
    public void ATypeFixedByHandWinsOverWhatTheSessionSees()
    {
        var store = new CornerTypeStore(_dir);
        var first = Drive(7, store);
        var map = first.CornerMap!;
        var corner = map.Corners[0];
        var other = first.CornerTypeById[corner.Name] == CornerType.Complex ? CornerType.Flat : CornerType.Complex;
        FixByHand(store, map, corner.Name, other, isFixed: true);

        var second = Drive(7, store);
        Assert.Equal(other, second.CornerTypeById[corner.Name]);
        var report = SessionReportBuilder.Build(second, new SessionReportInfo { Id = "x", Source = "test", StartedUtc = DateTime.UtcNow });
        Assert.Equal(other.ToString(), report.Corners.First(c => c.Id == corner.Name).Type);

        // Not replaced by the session.
        Assert.Equal(other, store.Load("spa", "GT3", map)[corner.Name]);
    }
}

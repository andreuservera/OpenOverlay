using System.Reflection;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Telemetry;
using IRacingOverlay.Coaching.Tests.Support;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Coaching.Tests;

/// <summary>Where one live report ends and the next begins.</summary>
public class LiveCoachSessionTests : IDisposable
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oo-live-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void AnOnlineSessionAfterATestDriveOnTheSameTrackAndCar_IsAReportOfItsOwn()
    {
        var (coach, reports) = NewCoach();
        var normal = Track.SpeedProfile();

        // A test drive (no subsession), then iRacing closes...
        coach.OnSessionInfo(Info(subSession: 0));
        Drive(coach, Track.Drive([normal, normal, normal]));
        coach.EndSession();

        // ...and later an online practice, same track and car.
        coach.OnSessionInfo(Info(subSession: 90093227));
        Drive(coach, Track.Drive([normal, normal, normal, normal]));
        coach.Flush();

        var saved = reports.LoadAll();
        Assert.Equal(2, saved.Count);
        Assert.All(saved, r => Assert.Equal(r.Laps.Count, r.Laps.Select(l => l.Number).Distinct().Count()));
    }

    [Fact]
    public void ANewSubsessionWithoutADisconnect_StartsANewReport_ButMoreSessionInfoDoesNot()
    {
        var (coach, reports) = NewCoach();
        var normal = Track.SpeedProfile();

        coach.OnSessionInfo(Info(subSession: 1));
        var first = Track.Drive([normal, normal, normal]);
        Drive(coach, first.Take(first.Count / 2));
        // Online, session info is published again every time someone joins: same event, same report.
        coach.OnSessionInfo(Info(subSession: 1));
        Drive(coach, first.Skip(first.Count / 2));

        coach.OnSessionInfo(Info(subSession: 2));
        Drive(coach, Track.Drive([normal, normal, normal], startTime: 5000));
        coach.Flush();

        Assert.Equal(2, reports.LoadAll().Count);
    }

    private (LiveCoach, SessionReportStore) NewCoach()
    {
        var reports = new SessionReportStore(Path.Combine(_dir, "sessions"));
        return (new LiveCoach(new CornerMapStore(Path.Combine(_dir, "maps")), reports), reports);
    }

    private static IracingSessionInfo Info(int subSession) => new()
    {
        WeekendInfo = new WeekendInfoSection { TrackName = "testtrack", TrackDisplayName = "Test Track", SubSessionID = subSession },
        DriverInfo = new DriverInfoSection { DriverCarIdx = 0, Drivers = [new DriverEntry { CarIdx = 0, CarScreenName = "Test Car" }] },
    };

    private static void Drive(LiveCoach coach, IEnumerable<TelemetryFrame> frames)
    {
        foreach (var f in frames)
        {
            coach.OnTelemetry(Snapshot(f));
        }
    }

    // TelemetrySnapshot is built by the SDK only: one is made here from a frame, every variable a float
    // but the session time (a double) and the integers iRacing publishes as ints.
    private static readonly (string Name, IrsdkVarType Type, Func<TelemetryFrame, double> Value)[] Vars =
    [
        ("SessionTime", IrsdkVarType.Double, f => f.SessionTime),
        ("SessionNum", IrsdkVarType.Int, f => f.SessionNum),
        ("SessionState", IrsdkVarType.Int, f => f.SessionState),
        ("Lap", IrsdkVarType.Int, f => f.Lap),
        ("LapDist", IrsdkVarType.Float, f => f.LapDist),
        ("LapDistPct", IrsdkVarType.Float, f => f.LapDistPct),
        ("LapLastLapTime", IrsdkVarType.Float, f => f.LapLastLapTime),
        ("Speed", IrsdkVarType.Float, f => f.Speed),
        ("Throttle", IrsdkVarType.Float, f => f.Throttle),
        ("Brake", IrsdkVarType.Float, f => f.Brake),
        ("SteeringWheelAngle", IrsdkVarType.Float, f => f.SteeringWheelAngle),
        ("Gear", IrsdkVarType.Int, f => f.Gear),
        ("RPM", IrsdkVarType.Float, f => f.Rpm),
        ("LatAccel", IrsdkVarType.Float, f => f.LatAccel),
        ("LongAccel", IrsdkVarType.Float, f => f.LongAccel),
        ("YawRate", IrsdkVarType.Float, f => f.YawRate),
        ("IsOnTrack", IrsdkVarType.Bool, f => f.IsOnTrack ? 1 : 0),
        ("PlayerTrackSurface", IrsdkVarType.Int, f => f.TrackSurface),
        ("PlayerCarMyIncidentCount", IrsdkVarType.Int, f => f.IncidentCount),
    ];

    private static readonly Dictionary<string, IrsdkVarHeader> Headers = Vars.Select((v, i) => new IrsdkVarHeader
    {
        Type = v.Type, Offset = i * 8, Count = 1, CountAsTime = false, Name = v.Name, Description = "", Unit = "",
    }).ToDictionary(h => h.Name);

    private static readonly ConstructorInfo Ctor = typeof(TelemetrySnapshot).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();

    private static TelemetrySnapshot Snapshot(TelemetryFrame f)
    {
        var data = new byte[Vars.Length * 8];
        for (var i = 0; i < Vars.Length; i++)
        {
            var value = Vars[i].Value(f);
            var bytes = Vars[i].Type switch
            {
                IrsdkVarType.Double => BitConverter.GetBytes(value),
                IrsdkVarType.Float => BitConverter.GetBytes((float)value),
                IrsdkVarType.Bool => [(byte)(value != 0 ? 1 : 0)],
                _ => BitConverter.GetBytes((int)value),
            };
            bytes.CopyTo(data, i * 8);
        }

        return (TelemetrySnapshot)Ctor.Invoke([data, Headers, 1]);
    }
}

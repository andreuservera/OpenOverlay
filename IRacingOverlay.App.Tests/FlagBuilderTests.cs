using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class FlagBuilderTests
{
    private const uint Checkered = 0x00000001;
    private const uint White = 0x00000002;
    private const uint Green = 0x00000004;
    private const uint Yellow = 0x00000008;
    private const uint Red = 0x00000010;
    private const uint Blue = 0x00000020;
    private const uint Debris = 0x00000040;
    private const uint Crossed = 0x00000080;
    private const uint YellowWaving = 0x00000100;
    private const uint OneLapToGreen = 0x00000200;
    private const uint GreenHeld = 0x00000400;
    private const uint TenToGo = 0x00000800;
    private const uint FiveToGo = 0x00001000;
    private const uint RandomWaving = 0x00002000;
    private const uint Caution = 0x00004000;
    private const uint CautionWaving = 0x00008000;
    private const uint Black = 0x00010000;
    private const uint Disqualify = 0x00020000;
    private const uint Servicible = 0x00040000;
    private const uint Furled = 0x00080000;
    private const uint Repair = 0x00100000;
    private const uint DqScoringInvalid = 0x00200000;
    private const uint StartHidden = 0x10000000;
    private const uint StartReady = 0x20000000;
    private const uint StartSet = 0x40000000;
    private const uint StartGo = 0x80000000;

    private static IRacingOverlay.Sdk.TelemetrySnapshot BuildWithFlags(uint flags)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionFlags", IrsdkVarType.BitField);
        return TestSnapshotFactory.Build(builder, w => w.SetBitField("SessionFlags", flags));
    }

    private static IReadOnlyList<FlagState> Shown(uint flags) => FlagBuilder.Build(BuildWithFlags(flags));

    [Fact]
    public void RollingStart_GreenShowsWhenTheRaceGoesGreen_NotWhenTheFieldIsHeld()
    {
        // Micro sprint at Lanier: GreenHeld comes out with OneLapToGreen 8.4 s before the green.
        var presenter = new FlagPresenter();
        var options = new FlagOptions { InfoFlagSeconds = 5 };
        var start = TimeSpan.FromSeconds(125.8);

        var held = presenter.Present(FlagBuilder.Decode(StartReady | GreenHeld | OneLapToGreen), options, start);
        var green = presenter.Present(FlagBuilder.Decode(StartGo | GreenHeld | OneLapToGreen | Green), options, start + TimeSpan.FromSeconds(8.4));

        Assert.DoesNotContain(held, f => f.Kind == FlagKind.Green);
        Assert.Contains(green, f => f.Kind == FlagKind.Green);
    }

    [Theory]
    [InlineData("Race", 0, false)] // just after the start, with no car positions to confirm it
    [InlineData("Race", 1, false)] // a lap iRacing counted at a rallycross start, then took back
    [InlineData("Race", 2, true)]
    [InlineData("Practice", 0, true)] // a faster car can come up behind on an out-lap
    public void BlueFlag_UnconfirmedOnTheOpeningLapsOfARace(string sessionType, int lapsCompleted, bool shown)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionFlags", IrsdkVarType.BitField);
        builder.AddVar("SessionNum", IrsdkVarType.Int);
        builder.AddVar("LapCompleted", IrsdkVarType.Int);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetBitField("SessionFlags", Blue | Green);
            w.SetInt("SessionNum", 0);
            w.SetInt("LapCompleted", lapsCompleted);
        });
        var session = new IRacingOverlay.Sdk.IracingSessionInfo
        {
            SessionInfo = new IRacingOverlay.Sdk.SessionInfoSection { Sessions = [new IRacingOverlay.Sdk.SessionEntry { SessionNum = 0, SessionType = sessionType }] },
        };

        Assert.Equal(shown, FlagBuilder.Decode(snapshot, session).Any(f => f.Kind == FlagKind.Blue));
    }

    /// <summary>Player (car 0) with LapCompleted 1 and the blue flag out in a race; three cars, each
    /// at a race distance (lap + fraction) and in a class with an estimated lap time.</summary>
    private static (IRacingOverlay.Sdk.TelemetrySnapshot Snapshot, IRacingOverlay.Sdk.IracingSessionInfo Session) BlueOnTheOpeningLaps(
        (int Lap, float Pct, int ClassId, double EstLap)[] cars,
        bool[]? onPitRoad = null)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionFlags", IrsdkVarType.BitField);
        builder.AddVar("SessionNum", IrsdkVarType.Int);
        builder.AddVar("LapCompleted", IrsdkVarType.Int);
        builder.AddVar("CarIdxLap", IrsdkVarType.Int, count: cars.Length);
        builder.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: cars.Length);
        builder.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: cars.Length);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetBitField("SessionFlags", Blue | Green);
            w.SetInt("SessionNum", 0);
            w.SetInt("LapCompleted", 1);
            w.SetIntArray("CarIdxLap", cars.Select(c => c.Lap).ToArray());
            w.SetFloatArray("CarIdxLapDistPct", cars.Select(c => c.Pct).ToArray());
            w.SetBoolArray("CarIdxOnPitRoad", onPitRoad ?? new bool[cars.Length]);
        });
        var session = new IRacingOverlay.Sdk.IracingSessionInfo
        {
            SessionInfo = new IRacingOverlay.Sdk.SessionInfoSection { Sessions = [new IRacingOverlay.Sdk.SessionEntry { SessionNum = 0, SessionType = "Race" }] },
            DriverInfo = new IRacingOverlay.Sdk.DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers = cars.Select((c, i) => new IRacingOverlay.Sdk.DriverEntry
                {
                    CarIdx = i,
                    CarClassID = c.ClassId,
                    CarClassEstLapTime = c.EstLap,
                }).ToList(),
            },
        };
        return (snapshot, session);
    }

    [Fact]
    public void BlueFlag_OnTheOpeningLaps_HiddenWhenNothingOnTrackAccountsForIt()
    {
        // Rallycross start: the field bunched up behind the player, all on the same lap.
        var (snapshot, session) = BlueOnTheOpeningLaps([(1, 0.02f, 1, 50), (0, 0.99f, 1, 50), (0, 0.97f, 1, 50)]);
        OpeningLapBlue? reported = null;

        var flags = FlagBuilder.Decode(snapshot, session, check => reported = check);

        Assert.DoesNotContain(flags, f => f.Kind == FlagKind.Blue);
        Assert.Equal(OpeningLapBlueReason.Unconfirmed, reported?.Reason);
    }

    [Fact]
    public void BlueFlag_RallycrossStart_PlayerStillOnTheGrid_NobodyIsLappingThem()
    {
        // The rallycross grid sits off the lap path, read at ~0.5 of lap 0; the cars that got away
        // first are just over the line on lap 1. Half a lap apart by the numbers, not a lap down.
        var (snapshot, session) = BlueOnTheOpeningLaps([(0, 0.48f, 1, 50), (1, 0.02f, 1, 50), (1, 0.01f, 1, 50)]);
        OpeningLapBlue? reported = null;

        var flags = FlagBuilder.Decode(snapshot, session, check => reported = check);

        Assert.DoesNotContain(flags, f => f.Kind == FlagKind.Blue);
        Assert.Equal(OpeningLapBlueReason.Unconfirmed, reported?.Reason);
    }

    [Fact]
    public void BlueFlag_OnTheOpeningLaps_ShownWhenACarIsLappingThePlayer()
    {
        // Towed on lap 1: the player rejoins at 1.3 laps, the leader is coming through on 2.25.
        var (snapshot, session) = BlueOnTheOpeningLaps([(1, 0.30f, 1, 50), (2, 0.25f, 1, 50), (1, 0.10f, 1, 50)]);
        OpeningLapBlue? reported = null;

        var flags = FlagBuilder.Decode(snapshot, session, check => reported = check);

        Assert.Contains(flags, f => f.Kind == FlagKind.Blue);
        Assert.Equal(OpeningLapBlueReason.Lapping, reported?.Reason);
        Assert.Equal(1, reported?.ByCarIdx);
    }

    [Theory]
    [InlineData(2, 40, false, true)]   // a faster class 2 s behind on the same lap
    [InlineData(2, 50, false, false)]  // the player's own class
    [InlineData(1, 60, false, false)]  // a slower class
    [InlineData(2, 40, true, false)]   // a faster class, but in the pit lane
    public void BlueFlag_OnTheOpeningLaps_ShownForAFasterClassCloseBehind(int classId, double estLap, bool inPitLane, bool shown)
    {
        // Car 1 is 0.04 of a lap behind the player: 2 s at the player's 50 s lap.
        var (snapshot, session) = BlueOnTheOpeningLaps(
            [(1, 0.50f, 1, 50), (1, 0.46f, classId, estLap), (1, 0.10f, 1, 50)],
            [false, inPitLane, false]);

        Assert.Equal(shown, FlagBuilder.Decode(snapshot, session).Any(f => f.Kind == FlagKind.Blue));
    }

    [Fact]
    public void BlueFlag_OnTheOpeningLaps_FasterClassFarBehindDoesNotAccountForIt()
    {
        // 0.2 of a lap behind: 10 s.
        var (snapshot, session) = BlueOnTheOpeningLaps([(1, 0.50f, 1, 50), (1, 0.30f, 2, 40), (1, 0.10f, 1, 50)]);

        Assert.DoesNotContain(FlagBuilder.Decode(snapshot, session), f => f.Kind == FlagKind.Blue);
    }

    [Theory]
    [InlineData("Offline Testing", false)] // test drive: the bit stays up all session, no green ever comes
    [InlineData("Lone Qualify", true)] // the warm-up lap before the qualifying lap
    [InlineData("Race", true)]
    public void OneLapToGreen_IgnoredInATestDrive(string sessionType, bool shown)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionFlags", IrsdkVarType.BitField);
        builder.AddVar("SessionNum", IrsdkVarType.Int);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetBitField("SessionFlags", StartHidden | Servicible | OneLapToGreen);
            w.SetInt("SessionNum", 0);
        });
        var session = new IRacingOverlay.Sdk.IracingSessionInfo
        {
            SessionInfo = new IRacingOverlay.Sdk.SessionInfoSection { Sessions = [new IRacingOverlay.Sdk.SessionEntry { SessionNum = 0, SessionType = sessionType }] },
        };

        Assert.Equal(shown, FlagBuilder.Decode(snapshot, session).Any(f => f.Kind == FlagKind.OneLapToGreen));
    }

    [Fact]
    public void OneLapToGreen_IgnoredOnceTheCheckeredIsOut()
    {
        // End of a lone qualify and the cool down: 0x10040201, no green coming.
        Assert.Equal(FlagKind.Checkered, Assert.Single(FlagBuilder.Decode(StartHidden | Servicible | OneLapToGreen | Checkered)).Kind);
    }

    [Fact]
    public void NoFlagsSet_ReturnsEmpty()
    {
        Assert.Empty(Shown(0));
    }

    [Fact]
    public void MissingVariable_ReturnsEmpty()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        Assert.Empty(FlagBuilder.Build(snapshot));
    }

    [Theory]
    [InlineData(Checkered, FlagKind.Checkered)]
    [InlineData(White, FlagKind.White)]
    [InlineData(Green, FlagKind.Green)]
    [InlineData(StartGo, FlagKind.Green)]
    [InlineData(Yellow, FlagKind.Yellow)]
    [InlineData(Red, FlagKind.Red)]
    [InlineData(Blue, FlagKind.Blue)]
    [InlineData(Debris, FlagKind.Debris)]
    [InlineData(Crossed, FlagKind.Crossed)]
    [InlineData(OneLapToGreen, FlagKind.OneLapToGreen)]
    [InlineData(TenToGo, FlagKind.TenToGo)]
    [InlineData(FiveToGo, FlagKind.FiveToGo)]
    [InlineData(RandomWaving, FlagKind.RandomWaving)]
    [InlineData(Caution, FlagKind.Caution)]
    [InlineData(Black, FlagKind.Black)]
    [InlineData(Disqualify, FlagKind.Disqualified)]
    [InlineData(Furled, FlagKind.Furled)]
    [InlineData(Repair, FlagKind.Meatball)]
    [InlineData(StartReady, FlagKind.StartLights)]
    public void EverySdkFlagBit_DecodesToItsKind(uint bit, FlagKind expected)
    {
        var flag = Assert.Single(FlagBuilder.Decode(bit));
        Assert.Equal(expected, flag.Kind);
    }

    [Fact]
    public void EveryDecodableKind_HasACatalogEntry()
    {
        var all = FlagBuilder.Decode(uint.MaxValue & ~StartHidden & ~StartGo & ~Green & ~GreenHeld);
        foreach (var flag in all)
        {
            Assert.Equal(flag.Kind, FlagCatalog.Get(flag.Kind).Kind);
        }
    }

    [Fact]
    public void ServicibleBit_IsNotAFlag()
    {
        // Regression: irsdk_servicible ("car is allowed service") was once mistaken for the meatball.
        Assert.Empty(FlagBuilder.Decode(Servicible));
        Assert.Equal(FlagKind.Green, Assert.Single(Shown(Green | Servicible)).Kind);
    }

    [Fact]
    public void OneLapToGreen_NeverShowsGreen()
    {
        // Regression: it means "still on the pace lap", and once put a green flag up before the start.
        Assert.DoesNotContain(FlagBuilder.Decode(OneLapToGreen), f => f.Kind == FlagKind.Green);
    }

    [Fact]
    public void WavingBits_BecomeWavingVariants()
    {
        Assert.Equal(new ActiveFlag(FlagKind.Yellow, FlagVariant.Waving), Assert.Single(FlagBuilder.Decode(Yellow | YellowWaving)));
        Assert.Equal(new ActiveFlag(FlagKind.Caution, FlagVariant.Waving), Assert.Single(FlagBuilder.Decode(Caution | CautionWaving)));
        Assert.Equal("WAVING YELLOW", Assert.Single(Shown(YellowWaving)).Name);
    }

    [Fact]
    public void DqScoringInvalid_IsTheScoreVoidedDisqualification()
    {
        var flag = Assert.Single(FlagBuilder.Decode(Disqualify | DqScoringInvalid));
        Assert.Equal(new ActiveFlag(FlagKind.Disqualified, FlagVariant.ScoreVoided), flag);
    }

    [Fact]
    public void StartLights_FollowTheSequenceAndHideWhenTheyAreGone()
    {
        Assert.Equal(new ActiveFlag(FlagKind.StartLights, FlagVariant.LightsSet), Assert.Single(FlagBuilder.Decode(StartReady | StartSet)));
        Assert.Empty(FlagBuilder.Decode(StartReady | StartHidden));
        Assert.Equal(FlagKind.Green, Assert.Single(FlagBuilder.Decode(StartReady | StartSet | StartGo)).Kind);
    }

    [Fact]
    public void FiveToGo_ReplacesTenToGo()
    {
        Assert.Equal(FlagKind.FiveToGo, Assert.Single(FlagBuilder.Decode(TenToGo | FiveToGo)).Kind);
    }

    [Fact]
    public void DriverAndTrackFlags_ShowTogether_MostSeriousFirst()
    {
        // A penalty doesn't make the yellow ahead any less relevant.
        var flags = Shown(Black | Yellow);

        Assert.Equal([FlagKind.Black, FlagKind.Yellow], flags.Select(f => f.Kind));
        Assert.True(flags[0].IsPrimary);
        Assert.False(flags[1].IsPrimary);
    }

    [Fact]
    public void TrackFlags_OnlyTheMostSeriousShows()
    {
        Assert.Equal(FlagKind.Red, Assert.Single(Shown(Red | Yellow | Green)).Kind);
        Assert.Equal(FlagKind.Caution, Assert.Single(Shown(Caution | Yellow)).Kind);
    }

    [Fact]
    public void YellowOnThePaceLap_ShowsYellowAndTheOneToGreenBoard()
    {
        var flags = Shown(Yellow | OneLapToGreen);

        Assert.Equal([FlagKind.Yellow, FlagKind.OneLapToGreen], flags.Select(f => f.Kind));
    }

    [Fact]
    public void Advisories_StackAlongsideTheTrackFlag()
    {
        var flags = Shown(Yellow | Debris | Blue);

        Assert.Equal([FlagKind.Yellow, FlagKind.Debris, FlagKind.Blue], flags.Select(f => f.Kind));
    }

    [Fact]
    public void BlueDuringGreenRacing_BothAppear()
    {
        Assert.Equal([FlagKind.Blue, FlagKind.Green], Shown(Green | Blue).Select(f => f.Kind));
    }

    [Fact]
    public void FlagStyles_MatchTheRealFlags()
    {
        Assert.Equal(FlagVisualStyle.Checkered, Assert.Single(Shown(Checkered)).Style);
        Assert.Equal(FlagVisualStyle.Meatball, Assert.Single(Shown(Repair)).Style);
        Assert.Equal(FlagVisualStyle.DebrisStripes, Assert.Single(Shown(Debris)).Style);
        Assert.Equal(FlagVisualStyle.BlueWithOrangeStripe, Assert.Single(Shown(Blue)).Style);
        Assert.Equal(FlagVisualStyle.BlackWithCross, Assert.Single(Shown(Disqualify)).Style);
        Assert.Equal(FlagVisualStyle.CornerCross, Assert.Single(Shown(Furled)).Style);
    }

    [Fact]
    public void FurledBlack_IsAllBlackWithAWhiteCross()
    {
        var furled = Assert.Single(Shown(Furled));

        Assert.Equal("#0B0D10", furled.BackgroundColor);
        Assert.Equal("#F2F4F7", furled.ForegroundColor);
    }
}

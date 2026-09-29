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
    [InlineData(GreenHeld, FlagKind.Green)]
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
        Assert.Equal(FlagVisualStyle.DiagonalSplit, Assert.Single(Shown(Furled)).Style);
    }
}

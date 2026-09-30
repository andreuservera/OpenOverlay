using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class IncidentBuilderTests
{
    [Fact]
    public void Build_ReadsMyIncidentCount()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("PlayerCarMyIncidentCount", IrsdkVarType.Int);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetInt("PlayerCarMyIncidentCount", 3));

        var state = IncidentBuilder.Build(snapshot);

        Assert.Equal(3, state.MyIncidentCount);
        Assert.Null(state.TeamIncidentCount);
    }

    [Fact]
    public void Build_TeamRace_ReadsBoth()
    {
        var state = IncidentBuilder.Build(Snapshot(mine: 2, team: 9), Session(teamRacing: 1, limit: "unlimited"));

        Assert.Equal(2, state.MyIncidentCount);
        Assert.Equal(9, state.TeamIncidentCount);
        Assert.Null(state.Limit);
    }

    [Fact]
    public void Build_SoloRace_IgnoresTheTeamVariable()
    {
        var state = IncidentBuilder.Build(Snapshot(mine: 2, team: 2), Session(teamRacing: 0, limit: "17"));

        Assert.Null(state.TeamIncidentCount);
        Assert.Equal(17, state.Limit);
    }

    [Theory]
    [InlineData(4, null, IncidentSeverity.Normal)]   // no limit: never coloured
    [InlineData(20, null, IncidentSeverity.Normal)]
    [InlineData(4, 17, IncidentSeverity.Normal)]
    [InlineData(9, 17, IncidentSeverity.Warning)]
    [InlineData(14, 17, IncidentSeverity.Critical)]
    public void Severity_FollowsTheIncidentLimit(int count, int? limit, IncidentSeverity expected)
    {
        var state = new IncidentState { MyIncidentCount = count, TeamIncidentCount = null, Limit = limit };

        Assert.Equal(expected, state.Severity);
    }

    private static TelemetrySnapshot Snapshot(int mine, int team)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("PlayerCarMyIncidentCount", IrsdkVarType.Int);
        builder.AddVar("PlayerCarTeamIncidentCount", IrsdkVarType.Int);
        return TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("PlayerCarMyIncidentCount", mine);
            w.SetInt("PlayerCarTeamIncidentCount", team);
        });
    }

    private static IracingSessionInfo Session(int teamRacing, string limit) => new()
    {
        WeekendInfo = new WeekendInfoSection
        {
            TeamRacing = teamRacing,
            WeekendOptions = new WeekendOptionsSection { IncidentLimit = limit },
        },
    };

    [Fact]
    public void Build_MissingVariable_ReturnsEmpty()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = IncidentBuilder.Build(snapshot);

        Assert.Equal(0, state.MyIncidentCount);
        Assert.Null(state.TeamIncidentCount);
    }
}

using IRacingOverlay.App.ViewModels;
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
    public void Build_TeamCountPresent_ReadsBoth()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("PlayerCarMyIncidentCount", IrsdkVarType.Int);
        builder.AddVar("PlayerCarTeamIncidentCount", IrsdkVarType.Int);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("PlayerCarMyIncidentCount", 2);
            w.SetInt("PlayerCarTeamIncidentCount", 9);
        });

        var state = IncidentBuilder.Build(snapshot);

        Assert.Equal(2, state.MyIncidentCount);
        Assert.Equal(9, state.TeamIncidentCount);
    }

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

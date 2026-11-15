using IRacingOverlay.App.ViewModels;
using Xunit;

namespace IRacingOverlay.App.Tests;

public class RadarBuilderTests
{
    private const string Gt3 = "#FFDA59";

    [Fact]
    public void NoCarsInRange_IsEmpty()
    {
        Assert.Empty(RadarBuilder.Assign([], 1).Cars);
    }

    [Fact]
    public void ACarLevelOnTheLeft_GoesLeft_TheOneBehindStaysInLine()
    {
        var state = RadarBuilder.Assign([(-12, Gt3), (1.5, Gt3)], carLeftRight: 2);

        Assert.Collection(state.Cars,
            near => Assert.Equal((1.5, RadarLane.Left), (near.GapMeters, near.Lane)),
            behind => Assert.Equal((-12, RadarLane.Centre), (behind.GapMeters, behind.Lane)));
    }

    [Fact]
    public void CarsBothSides_FillLeftThenRight_NearestFirst()
    {
        var state = RadarBuilder.Assign([(3, Gt3), (-0.5, Gt3)], carLeftRight: 4);

        Assert.Equal([RadarLane.Left, RadarLane.Right], state.Cars.Select(car => car.Lane));
        Assert.Equal(-0.5, state.Cars[0].GapMeters);
    }

    [Fact]
    public void TwoCarsRight_BothGoRight()
    {
        var state = RadarBuilder.Assign([(2, Gt3), (-3, Gt3)], carLeftRight: 6);

        Assert.All(state.Cars, car => Assert.Equal(RadarLane.Right, car.Lane));
        Assert.All(state.Cars, car => Assert.True(car.Alongside));
    }

    [Fact]
    public void ALevelCarIRacingSeesOnNoSide_IsLeftOut()
    {
        // A crossover bridge or a parallel stretch: level by lap distance, nowhere near by the spotter.
        var state = RadarBuilder.Assign([(0.5, Gt3), (15, Gt3)], carLeftRight: 1);

        var only = Assert.Single(state.Cars);
        Assert.Equal(RadarLane.Centre, only.Lane);
        Assert.Equal(15, only.GapMeters);
    }
}

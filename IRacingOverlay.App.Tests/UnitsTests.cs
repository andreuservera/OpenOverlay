using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class UnitsTests
{
    [Theory]
    [InlineData(0, UnitSystem.Imperial)] // iRacing: 0 = English
    [InlineData(1, UnitSystem.Metric)]
    public void Read_MapsIRacingDisplayUnits(int displayUnits, UnitSystem expected)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("DisplayUnits", IrsdkVarType.Int);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetInt("DisplayUnits", displayUnits));

        Assert.Equal(expected, Units.Read(snapshot));
    }

    [Theory]
    [InlineData(UnitPreference.FollowIRacing, UnitSystem.Imperial, UnitSystem.Imperial)]
    [InlineData(UnitPreference.FollowIRacing, null, UnitSystem.Metric)] // sim not seen yet
    [InlineData(UnitPreference.Metric, UnitSystem.Imperial, UnitSystem.Metric)] // app choice wins
    [InlineData(UnitPreference.Imperial, UnitSystem.Metric, UnitSystem.Imperial)]
    [InlineData(UnitPreference.Imperial, null, UnitSystem.Imperial)]
    public void Resolve_AppChoiceOverridesIRacing(UnitPreference preference, UnitSystem? iracing, UnitSystem expected)
    {
        Assert.Equal(expected, Units.Resolve(preference, iracing));
    }

    [Fact]
    public void Conversions_MatchReferenceValues()
    {
        Assert.Equal(100, Units.Speed(160.9344, UnitSystem.Imperial), 3);
        Assert.Equal(212, Units.Temperature(100, UnitSystem.Imperial), 3);
        Assert.Equal(26, Units.Pressure(179.263682, UnitSystem.Imperial), 3);
        Assert.Equal(1, Units.Volume(3.785411784, UnitSystem.Imperial), 6);
        Assert.Equal(3.785411784, Units.VolumeToLiters(1, UnitSystem.Imperial), 6);
        Assert.Equal(55.5, Units.Speed(55.5, UnitSystem.Metric));
    }

    [Fact]
    public void Fuel_DisplaysGallonsInImperial()
    {
        var state = new FuelState
        {
            LevelLiters = 37.854,
            LevelPct = 0.5,
            PerLapLiters = 3.7854,
            LapsOfFuelRemaining = 10,
            LapsRemainingInSession = null,
            UnitSystem = UnitSystem.Imperial,
        };

        Assert.Equal("10.0 gal", state.LevelDisplay);
        Assert.Equal("1.00 gal/lap", state.PerLapDisplay);
    }

    [Fact]
    public void FuelCalculator_RefuelRoundsUpToATenthOfAGallon()
    {
        var shortBy = new FuelCalculatorState
        {
            LevelLiters = 10, LevelPct = 0.2, LastLapLiters = 3, AverageLiters = 3, MinLiters = 3, MaxLiters = 3,
            LapsRemainingWithFuel = 3, LapsLeftInSession = 5, FuelToFinishLiters = 15.5, FuelDeltaLiters = -5.5,
            TankCapacityLiters = 60, UnitSystem = UnitSystem.Imperial,
        };

        // 5.5 L = 1.453 gal → 1.5 gal, not the 6 L (1.59 gal) the metric rounding would give.
        Assert.Equal("1.5 gal", shortBy.RefuelDisplay);
        Assert.Equal("-1.5 gal", shortBy.FuelDeltaDisplay);
        Assert.Equal("gal", shortBy.VolumeUnit);
    }

    [Fact]
    public void Tires_DisplayPsiAndFahrenheitInImperial()
    {
        var corner = new TireCornerInfo
        {
            Label = "LF", PressureKPa = 172.369, ColdPressureKPa = 172.369, TempLeft = 80, TempMiddle = 90, TempRight = 100,
            IsSurfaceTemp = true, WearLeft = 1, WearMiddle = 1, WearRight = 1, HasWearData = false,
            UnitSystem = UnitSystem.Imperial,
        };

        Assert.Equal("25.0", corner.PressureDisplay);
        Assert.Equal("psi", corner.PressureUnit);
        Assert.Equal("176°", corner.TempLeftDisplay);
    }
}

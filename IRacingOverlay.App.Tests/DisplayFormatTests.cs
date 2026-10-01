using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.Tests;

public class DisplayFormatTests
{
    private static TireCornerInfo Corner(double left, double middle, double right, bool hasWear = true) => new()
    {
        Label = "LF",
        PressureKPa = 0,
        ColdPressureKPa = 0,
        TempLeft = 0,
        TempMiddle = 0,
        TempRight = 0,
        IsSurfaceTemp = false,
        WearLeft = left,
        WearMiddle = middle,
        WearRight = right,
        HasWearData = hasWear,
    };

    [Fact]
    public void TireWear_ShowsEveryZoneOnItsOwn()
    {
        var corner = Corner(0.92, 0.45, 0.18);

        Assert.Equal(new[] { "92%", "45%", "18%" }, new[] { corner.WearLeftDisplay, corner.WearMiddleDisplay, corner.WearRightDisplay });
        Assert.Equal(new[] { "#34D399", "#F5A524", "#F04438" }, new[] { corner.WearLeftColor, corner.WearMiddleColor, corner.WearRightColor });
        Assert.Equal(0.45, corner.WearMiddleFill, precision: 3);
    }

    [Fact]
    public void TireWear_NotReported_ShowsDashesAndEmptyBars()
    {
        var corner = Corner(1, 1, 1, hasWear: false);

        Assert.Equal("—", corner.WearLeftDisplay);
        Assert.Equal(0, corner.WearRightFill);
        Assert.False(corner.HasPressure);
        Assert.Equal("—", corner.PressureDisplay);
    }

    [Fact]
    public void RainChance_RunsFromSunYellowToRainBlue()
    {
        Assert.Equal(Color.FromRgb(0xFF, 0xD2, 0x4D), WeatherPanel.RainColor(0));
        Assert.Equal(Color.FromRgb(0x3D, 0x8B, 0xFF), WeatherPanel.RainColor(1));

        // Bluer with every step up the scale.
        var previous = WeatherPanel.RainColor(0);
        for (var pct = 10; pct <= 100; pct += 10)
        {
            var color = WeatherPanel.RainColor(pct / 100.0);
            Assert.True(color.B >= previous.B && color.R <= previous.R, $"{pct}%");
            previous = color;
        }
    }

    [Theory]
    [InlineData("Hard", "H")]
    [InlineData("Medium", "M")]
    [InlineData("Soft", "S")]
    [InlineData("Wet", "W")]
    [InlineData("Intermediate", "I")]
    public void TireCompound_NamesUseTheBroadcastLetters(string name, string letter)
    {
        Assert.Equal(letter, TireCompound.FromName(name).Letter);
    }

    [Fact]
    public void TireCompoundIcon_CentresTheGlyphsInkOnTheRing()
    {
        // An ink box offset the way a font's line box offsets a capital: low and to the right.
        var ink = new Rect(1.3, 2.9, 6.1, 7.2);
        var center = new Point(8.5, 8.5);

        var placed = TireCompoundIcon.Placement(ink, center, innerDiameter: 12).Transform(
            new Point(ink.X + (ink.Width / 2), ink.Y + (ink.Height / 2)));

        Assert.Equal(center.X, placed.X, precision: 6);
        Assert.Equal(center.Y, placed.Y, precision: 6);
    }

    [Fact]
    public void TireCompoundIcon_ShrinksAGlyphTooWideForTheRing()
    {
        var ink = new Rect(0, 0, 14, 7);

        var matrix = TireCompoundIcon.Placement(ink, new Point(8.5, 8.5), innerDiameter: 12);

        Assert.True(matrix.M11 * ink.Width <= 12 * 0.72 + 1e-9);
    }

    [Theory]
    [InlineData(false, false, false, PenaltyFlag.None, PenaltyFlag.None)]
    [InlineData(true, false, false, PenaltyFlag.Black, PenaltyFlag.None)]
    [InlineData(false, true, false, PenaltyFlag.Furled, PenaltyFlag.None)]
    [InlineData(false, false, true, PenaltyFlag.Meatball, PenaltyFlag.None)]
    [InlineData(true, true, false, PenaltyFlag.Black, PenaltyFlag.Furled)]
    [InlineData(true, false, true, PenaltyFlag.Black, PenaltyFlag.Meatball)]
    [InlineData(false, true, true, PenaltyFlag.Meatball, PenaltyFlag.Furled)]
    [InlineData(true, true, true, PenaltyFlag.Black, PenaltyFlag.Meatball)] // two at most, the most serious
    public void PenaltyTag_ShowsAtMostTwoFlags_MostSeriousFirst(
        bool black, bool furled, bool meatball, PenaltyFlag primary, PenaltyFlag secondary)
    {
        var row = new StandingsRow
        {
            CarIdx = 1,
            Position = 1,
            ClassPosition = 1,
            Name = "Driver",
            CarNumber = "1",
            IsPlayer = false,
            OnPitRoad = false,
            HasBlackFlag = black,
            HasFurledFlag = furled,
            HasMeatballFlag = meatball,
            CurrentLap = 1,
            LastLapTime = 0,
            BestLapTime = 0,
            IsMultiClass = false,
            IRating = 0,
            LicString = "",
            IRatingDelta = 0,
            IsSessionFastestLap = false,
            GapToLeaderSeconds = 0,
        };

        Assert.Equal(primary, row.PrimaryPenalty);
        Assert.Equal(secondary, row.SecondaryPenalty);
        Assert.Equal(primary != PenaltyFlag.None, row.HasPenaltyFlag);
    }
}

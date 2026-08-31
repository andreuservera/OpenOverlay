using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class StandingsRowTests
{
    private static StandingsRow Row(string licString = "", double iRatingDelta = 0, int iRating = 1000) => new()
    {
        CarIdx = 0,
        Position = 1,
        ClassPosition = 1,
        Name = "Driver",
        CarNumber = "1",
        IsPlayer = false,
        OnPitRoad = false,
        CurrentLap = 1,
        GapToLeaderSeconds = 0,
        LastLapTime = 0,
        BestLapTime = 0,
        IsMultiClass = false,
        IRating = iRating,
        LicString = licString,
        IRatingDelta = iRatingDelta,
        IsSessionFastestLap = false,
    };

    [Theory]
    [InlineData("R 1.5", "#E0413D")]
    [InlineData("D 2.1", "#E08A2E")]
    [InlineData("C 3.0", "#E0C93D")]
    [InlineData("B 3.8", "#3DBF5C")]
    [InlineData("A 4.5", "#3D7FE0")]
    [InlineData("Pro 5.0", "#9B4DE0")]
    public void LicenseColor_MapsLetterToExpectedColor(string licString, string expectedColor)
    {
        Assert.Equal(expectedColor, Row(licString).LicenseColor);
    }

    [Fact]
    public void LicenseColor_BlankLicense_FallsBackToGray()
    {
        Assert.Equal("#666666", Row("").LicenseColor);
    }

    [Fact]
    public void IRatingDeltaDisplay_PositiveDelta_ShowsPlusSign()
    {
        Assert.Equal("+12", Row(iRatingDelta: 12.4).IRatingDeltaDisplay);
    }

    [Fact]
    public void IRatingDeltaDisplay_NegativeDelta_ShowsMinusSign()
    {
        Assert.Equal("-8", Row(iRatingDelta: -7.6).IRatingDeltaDisplay);
    }

    [Fact]
    public void IRatingDeltaDisplay_NoRating_ShowsDash()
    {
        Assert.Equal("—", Row(iRatingDelta: 12, iRating: 0).IRatingDeltaDisplay);
    }

    [Theory]
    // A swing that rounds to nothing reads as a dash, not "+0": practice and qualifying produce no
    // estimate at all, and "+0" there looks like a computed result rather than an absent one.
    [InlineData(0, "—")]
    [InlineData(0.4, "—")]
    [InlineData(-0.4, "—")]
    [InlineData(0.6, "+1")]
    [InlineData(-0.6, "-1")]
    public void IRatingDeltaDisplay_RoundsBeforeDecidingWhetherThereIsASwingAtAll(double delta, string expected)
    {
        Assert.Equal(expected, Row(iRatingDelta: delta).IRatingDeltaDisplay);
    }

    [Fact]
    public void IRatingDeltaForeground_PositiveIsGreen_NegativeIsRed_NoSwingIsNeutral()
    {
        // The ViewModel hands the UI a colour string, so these have to agree with the palette by
        // hand. The red is lighter than the app's standard critical red: measured on a row
        // background, a saturated red doesn't clear 4.5:1 against the text beside it.
        Assert.Equal("#3DDC7A", Row(iRatingDelta: 5).IRatingDeltaForeground);
        Assert.Equal("#FFB3B3", Row(iRatingDelta: -5).IRatingDeltaForeground);
        Assert.Equal("#D2D8DE", Row(iRatingDelta: 0).IRatingDeltaForeground);
        // Colour follows the rounded value too, so a dash is never tinted as a gain.
        Assert.Equal("#D2D8DE", Row(iRatingDelta: 0.4).IRatingDeltaForeground);
    }
}

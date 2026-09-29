using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class StandingsRowTests
{
    private static StandingsRow Row(
        string licString = "", double iRatingDelta = 0, int iRating = 1000,
        double lastLap = 0, double bestLap = 0, bool sessionFastest = false) => new()
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
        LastLapTime = lastLap,
        BestLapTime = bestLap,
        IsMultiClass = false,
        IRating = iRating,
        LicString = licString,
        IRatingDelta = iRatingDelta,
        IsSessionFastestLap = sessionFastest,
    };

    [Theory]
    [InlineData(91.2, 90.5, false, "#C4CCD4")] // slower than their best: plain
    [InlineData(90.5, 90.5, false, "#34D399")] // personal best: green
    [InlineData(90.5, 90.5, true, "#B58CFF")]  // personal best that is also the session's fastest: purple
    [InlineData(0, 0, false, "#C4CCD4")]       // no lap yet
    public void LastLapForeground_MarksPersonalAndSessionBests(double last, double best, bool fastest, string expected)
    {
        Assert.Equal(expected, Row(lastLap: last, bestLap: best, sessionFastest: fastest).LastLapForeground);
    }

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
    public void IRatingDelta_Gain_ShowsMagnitudeWithUpwardTrend()
    {
        var row = Row(iRatingDelta: 12.4);
        Assert.Equal("12", row.IRatingDeltaDisplay);
        Assert.Equal(1, row.IRatingTrend);
    }

    [Fact]
    public void IRatingDelta_Loss_ShowsMagnitudeWithDownwardTrend()
    {
        var row = Row(iRatingDelta: -7.6);
        Assert.Equal("8", row.IRatingDeltaDisplay);
        Assert.Equal(-1, row.IRatingTrend);
    }

    [Fact]
    public void IRatingDeltaDisplay_NoRating_ShowsDash()
    {
        var row = Row(iRatingDelta: 12, iRating: 0);
        Assert.Equal("—", row.IRatingDeltaDisplay);
        Assert.Equal(0, row.IRatingTrend);
    }

    [Theory]
    // A swing that rounds to nothing reads as a dash, not "0": practice and qualifying produce no
    // estimate at all, and "0" there looks like a computed result rather than an absent one.
    [InlineData(0, "—", 0)]
    [InlineData(0.4, "—", 0)]
    [InlineData(-0.4, "—", 0)]
    [InlineData(0.6, "1", 1)]
    [InlineData(-0.6, "1", -1)]
    public void IRatingDelta_RoundsBeforeDecidingWhetherThereIsASwingAtAll(double delta, string expected, int trend)
    {
        Assert.Equal(expected, Row(iRatingDelta: delta).IRatingDeltaDisplay);
        Assert.Equal(trend, Row(iRatingDelta: delta).IRatingTrend);
    }

    [Fact]
    public void IRatingDeltaForeground_PositiveIsGreen_NegativeIsRed_NoSwingIsNeutral()
    {
        // The ViewModel hands the UI a colour string, so these have to agree with the palette by hand.
        Assert.Equal("#34D399", Row(iRatingDelta: 5).IRatingDeltaForeground);
        Assert.Equal("#FF6B6B", Row(iRatingDelta: -5).IRatingDeltaForeground);
        Assert.Equal("#8E99A5", Row(iRatingDelta: 0).IRatingDeltaForeground);
        // Colour follows the rounded value too, so a dash is never tinted as a gain.
        Assert.Equal("#8E99A5", Row(iRatingDelta: 0.4).IRatingDeltaForeground);
    }
}

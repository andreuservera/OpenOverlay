using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.Tests;

public class StandingsSofTests
{
    [Theory]
    [InlineData(2987, "SOF 2.9k")]
    [InlineData(2900, "SOF 2.9k")]
    [InlineData(1000, "SOF 1.0k")]
    [InlineData(12345, "SOF 12.3k")]
    [InlineData(850, "SOF 850")]
    [InlineData(612.6, "SOF 613")]
    [InlineData(999.7, "SOF 1.0k")]
    [InlineData(0, "")]
    [InlineData(double.NaN, "")]
    public void FormatSof_ShowsThousandsToOneDecimal_Truncated(double sof, string expected)
    {
        Assert.Equal(expected, StandingsPanel.FormatSof(sof));
    }
}

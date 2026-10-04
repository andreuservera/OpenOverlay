using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.Tests;

public class PenaltyFlagTagTests
{
    [Theory]
    // The plain black flag has no mark to lose, so the marked one goes in front.
    [InlineData(PenaltyFlag.Black, PenaltyFlag.Meatball, PenaltyFlag.Meatball, PenaltyFlag.Black)]
    [InlineData(PenaltyFlag.Black, PenaltyFlag.Furled, PenaltyFlag.Furled, PenaltyFlag.Black)]
    // Otherwise the more serious (first) one stays in front.
    [InlineData(PenaltyFlag.Meatball, PenaltyFlag.Furled, PenaltyFlag.Meatball, PenaltyFlag.Furled)]
    [InlineData(PenaltyFlag.Meatball, PenaltyFlag.Black, PenaltyFlag.Meatball, PenaltyFlag.Black)]
    public void Stacking_PutsTheFlagWithAMarkInFront(PenaltyFlag primary, PenaltyFlag secondary, PenaltyFlag front, PenaltyFlag back)
    {
        Assert.Equal((front, back), PenaltyFlagTag.Stacking(primary, secondary));
    }
}

using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class FlagPresenterTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);

    [Fact]
    public void DisabledFlag_IsNeverShown_AndTheNextInItsGroupTakesOver()
    {
        var options = new FlagOptions();
        options.SetEnabled(FlagKind.Caution, false);

        var shown = FlagPresenter.Compose([new(FlagKind.Caution), new(FlagKind.Yellow)], options);

        Assert.Equal(FlagKind.Yellow, Assert.Single(shown).Kind);
    }

    [Fact]
    public void RandomWaving_IsOffByDefault()
    {
        Assert.Empty(FlagPresenter.Compose([new(FlagKind.RandomWaving)], new FlagOptions()));
    }

    [Fact]
    public void MaxFlags_CapsTheList_KeepingTheMostImportant()
    {
        var options = new FlagOptions { MaxFlags = 2 };

        var shown = FlagPresenter.Compose([new(FlagKind.Blue), new(FlagKind.Debris), new(FlagKind.Black)], options);

        Assert.Equal([FlagKind.Black, FlagKind.Debris], shown.Select(f => f.Kind));
    }

    [Fact]
    public void InfoFlags_HideAfterTheHold_SafetyFlagsStay()
    {
        var presenter = new FlagPresenter();
        var options = new FlagOptions { InfoFlagSeconds = 5 };
        ActiveFlag[] active = [new(FlagKind.Green), new(FlagKind.Blue)];

        Assert.Equal(2, presenter.Present(active, options, T0).Count);

        var later = presenter.Present(active, options, T0 + TimeSpan.FromSeconds(6));
        Assert.Equal(FlagKind.Blue, Assert.Single(later).Kind);
    }

    [Fact]
    public void InfoFlag_ComesBackWithAFreshTimer_AfterGoingAway()
    {
        var presenter = new FlagPresenter();
        var options = new FlagOptions { InfoFlagSeconds = 5 };

        presenter.Present([new(FlagKind.Green)], options, T0);
        presenter.Present([new(FlagKind.Caution)], options, T0 + TimeSpan.FromSeconds(60));
        var restart = presenter.Present([new(FlagKind.Green)], options, T0 + TimeSpan.FromSeconds(120));

        Assert.Equal(FlagKind.Green, Assert.Single(restart).Kind);
    }

    [Fact]
    public void HoldOfZero_KeepsInfoFlagsWhileActive()
    {
        var presenter = new FlagPresenter();
        var options = new FlagOptions { InfoFlagSeconds = 0 };

        presenter.Present([new(FlagKind.Green)], options, T0);

        Assert.Single(presenter.Present([new(FlagKind.Green)], options, T0 + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Variant_ChangesTheWordingNotTheKind()
    {
        var shown = Assert.Single(FlagPresenter.Compose([new(FlagKind.Disqualified, FlagVariant.ScoreVoided)], new FlagOptions()));

        Assert.Equal(FlagKind.Disqualified, shown.Kind);
        Assert.Contains("voided", shown.Description);
    }

    [Fact]
    public void OnlyTheFirstFlagIsPrimary()
    {
        var shown = FlagPresenter.Compose([new(FlagKind.Yellow), new(FlagKind.Blue)], new FlagOptions());

        Assert.Equal([true, false], shown.Select(f => f.IsPrimary));
    }

    [Fact]
    public void Options_TextVisibility_FollowsDisplayMode()
    {
        var options = new FlagOptions();
        Assert.True(options.ShowNameText);

        options.DisplayMode = FlagDisplayMode.IconOnly;
        Assert.False(options.ShowText);
        Assert.False(options.ShowNameText);
        Assert.False(options.ShowDescriptionText);
    }

    [Fact]
    public void Catalog_CoversEveryKind_ExceptThePlaceholder()
    {
        var kinds = Enum.GetValues<FlagKind>().Where(k => k != FlagKind.None);

        Assert.All(kinds, kind => Assert.Equal(kind, FlagCatalog.Get(kind).Kind));
    }
}

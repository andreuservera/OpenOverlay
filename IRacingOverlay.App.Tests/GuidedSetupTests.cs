using System.Windows;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

/// <summary>The setup assistant's presets and steps, and the preview's scenarios.</summary>
public class GuidedSetupTests
{
    [Fact]
    public void Presets_NameOnlyRealWidgets_AndEssentialIsTheDocumentedThree()
    {
        Assert.All(WidgetPresets.All.SelectMany(preset => preset.Widgets), key => Assert.True(WidgetCatalog.Contains(key)));
        Assert.Equal(
            [WidgetCatalog.Relative, WidgetCatalog.Cockpit, WidgetCatalog.Flag],
            WidgetPresets.All.Single(preset => preset.Name == "Essential").Widgets);
    }

    /// <summary>Every widget starts inside the monitor it was placed for, wherever that monitor sits on the desktop.</summary>
    [Theory]
    [InlineData(0, 0, 2560, 1440)]
    [InlineData(-1920, 120, 1920, 1080)]
    [InlineData(3440, 0, 3440, 1440)]
    public void Positions_LandOnTheChosenMonitor(double left, double top, double width, double height)
    {
        var monitor = new Rect(left, top, width, height);

        foreach (var descriptor in WidgetCatalog.All)
        {
            var at = WidgetPresets.PositionOn(descriptor.Key, monitor);
            Assert.True(monitor.Contains(at), $"{descriptor.Key} at {at} is outside {monitor}");
        }
    }

    [Fact]
    public void Assistant_AppliesTheChoices_WhenLeavingThePresetStep_AndNotBefore()
    {
        (SetupChoice Monitor, SetupChoice Preset)? applied = null;
        bool? closedMovingNow = null;
        var monitors = new[] { new SetupChoice("Main", "", "2560×1440", 0), new SetupChoice("Side", "", "1920×1080", 1) };
        var setup = new SetupAssistant(monitors, (monitor, preset) => applied = (monitor, preset), moveNow => closedMovingNow = moveNow);

        setup.SelectCommand.Execute(monitors[1]);
        setup.NextCommand.Execute(null);
        Assert.Null(applied);

        setup.SelectCommand.Execute(setup.Presets[2]);
        setup.NextCommand.Execute(null);

        Assert.Equal(SetupAssistant.PlaceStep, setup.Step);
        Assert.Same(monitors[1], applied!.Value.Monitor);
        Assert.Same(setup.Presets[2], applied.Value.Preset);
        Assert.False(monitors[0].IsSelected);

        setup.MoveNowCommand.Execute(null);
        Assert.True(closedMovingNow);
    }

    /// <summary>Offered on a fresh install it can be skipped; opened by hand it is cancelled — and
    /// either way leaving it never applies anything.</summary>
    [Theory]
    [InlineData(true, "Skip for now")]
    [InlineData(false, "Cancel")]
    public void Assistant_LeavingIt_AppliesNothing(bool isFirstRun, string cancelText)
    {
        var applied = false;
        bool? closedMovingNow = null;
        var setup = new SetupAssistant([new SetupChoice("Main", "", "", 0)], (_, _) => applied = true, moveNow => closedMovingNow = moveNow, isFirstRun);

        setup.NextCommand.Execute(null);
        setup.CancelCommand.Execute(null);

        Assert.Equal(cancelText, setup.CancelText);
        Assert.False(applied);
        Assert.False(closedMovingNow);
    }

    [Fact]
    public void Scenarios_ChangeWhatThePreviewShows()
    {
        Assert.Equal(PreviewData.ScenarioLabels.Count, Enum.GetValues<PreviewScenario>().Length);

        Assert.Equal("RACE", PreviewData.SessionType(PreviewScenario.Race));
        Assert.Equal("PRACTICE", PreviewData.SessionType(PreviewScenario.Practice));
        Assert.Equal("QUALIFY", PreviewData.SessionType(PreviewScenario.Qualifying));

        Assert.Equal(WeatherCondition.Rain, PreviewData.Weather(PreviewScenario.Rain).Condition);
        Assert.Equal(WeatherCondition.Rain, PreviewData.TableConditions(PreviewScenario.Rain).Condition);
        var rainField = PreviewData.StandingsField(multiClass: false, PreviewScenario.Rain);
        Assert.True(rainField.Count(row => row.TireCompound?.Name == "Wet") > rainField.Count / 2);

        Assert.All(PreviewData.RelativeRows(3, PreviewScenario.RaceOneClass).Cast<RelativeRow>(), row => Assert.False(row.IsMultiClass));
        Assert.Contains(PreviewData.RelativeRows(3).Cast<RelativeRow>(), row => row.IsMultiClass);

        var spectated = PreviewData.StandingsField(multiClass: false, PreviewScenario.Spectating).Single(row => row.IsPlayer);
        Assert.NotEqual("You", spectated.Name);
    }
}

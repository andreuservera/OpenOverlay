using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

/// <summary>Lists split into shown and hidden, "changed from default", section reset, typed values.</summary>
public class SettingsPolishTests
{
    private enum Column { A, B, C, D }

    private static (ReorderListSetting List, List<IReadOnlyList<Enum>> Applied) List(params (Column Key, bool Visible)[] rows)
    {
        var applied = new List<IReadOnlyList<Enum>>();
        var list = new ReorderListSetting(
            "Columns",
            null,
            rows.Select(row => new ReorderListItem(row.Key, new ChipSetting(row.Key.ToString(), null, row.Visible, _ => { }))),
            applied.Add,
            [Column.A, Column.B, Column.C, Column.D]);
        return (list, applied);
    }

    [Fact]
    public void List_ShowsTheShownRowsInOrder_AndTheHiddenOnesApart()
    {
        var (list, _) = List((Column.C, true), (Column.A, false), (Column.B, true), (Column.D, false));

        Assert.Equal([Column.C, Column.B], list.VisibleItems.Select(row => (Column)row.Key));
        Assert.Equal([Column.A, Column.D], list.HiddenItems.Select(row => (Column)row.Key));

        list.Items[1].Visible.Value = true;

        Assert.Equal([Column.C, Column.A, Column.B], list.VisibleItems.Select(row => (Column)row.Key));
        Assert.Equal([Column.D], list.HiddenItems.Select(row => (Column)row.Key));
    }

    /// <summary>A drag counts shown rows only; hidden rows between keep their place in the order.</summary>
    [Fact]
    public void DraggingAmongShownRows_LeavesTheHiddenOnesWhereTheyWere()
    {
        var (list, applied) = List((Column.A, true), (Column.B, false), (Column.C, true), (Column.D, true));

        list.MoveVisibleLive(0, 2);
        list.CommitOrder();

        Assert.Equal([Column.C, Column.D], list.VisibleItems.Take(2).Select(row => (Column)row.Key));
        Assert.Equal([Column.B, Column.C, Column.D, Column.A], Assert.Single(applied).Cast<Column>());
    }

    [Fact]
    public void ShowAll_HideAll_AndResetOrder()
    {
        var (list, applied) = List((Column.D, true), (Column.B, false), (Column.A, true), (Column.C, false));

        list.ShowAllCommand.Execute(null);
        Assert.Empty(list.HiddenItems);

        list.HideAllCommand.Execute(null);
        Assert.Empty(list.VisibleItems);

        list.ResetOrderCommand.Execute(null);
        Assert.Equal([Column.A, Column.B, Column.C, Column.D], list.Items.Select(row => (Column)row.Key));
        Assert.Equal([Column.A, Column.B, Column.C, Column.D], applied.Last().Cast<Column>());
    }

    [Theory]
    [InlineData("73", 0.73)]
    [InlineData("73 %", 0.73)]
    [InlineData("0,5", 0.01)]
    [InlineData("250", 1.0)]
    [InlineData("abc", 0.4)]
    public void TypedOpacity_TakesTheNumber_AsAPercentage(string typed, double expected)
    {
        var slider = new SliderSetting("Opacity", null, 0.4, 0, 1, 0.01, _ => { });

        slider.Text = typed;

        Assert.Equal(expected, slider.Value, 3);
        Assert.Equal(slider.Display, slider.Text);
    }

    /// <summary>A widget's rows know how they came: a change lights its row and its section, and the
    /// section's Reset puts everything back, through the same setters a change by hand uses.</summary>
    [Fact]
    public void ChangedRows_AreFlagged_AndSectionResetPutsThemBack()
    {
        var context = NewContext(out var standings);
        var groups = WidgetSettings.For(WidgetCatalog.Standings, context);
        var defaults = WidgetSettings.For(WidgetCatalog.Standings, NewContext(out _)).ToDictionary(group => group.Title);
        foreach (var group in groups)
        {
            group.AttachDefaults(defaults[group.Title]);
        }

        var content = groups.Single(group => group.Title == WidgetSettings.Content);
        var behavior = groups.Single(group => group.Title == WidgetSettings.Behavior);
        Assert.False(content.HasModified);
        Assert.False(behavior.HasModified);

        var columns = content.Items.OfType<ReorderListSetting>().Single();
        columns.Move(0, 3);
        columns.Items.First(row => !row.Visible.Value).Visible.Value = true;
        var infoBar = content.Items.OfType<InfoBarSetting>().Single();
        infoBar.Move(infoBar.Chips.First(chip => infoBar.Available.Contains(chip)), TableSlot.TopCenter);
        var split = behavior.Items.OfType<ToggleSetting>().Single();
        split.Value = !split.Value;

        Assert.True(columns.IsModified);
        Assert.True(infoBar.IsModified);
        Assert.True(content.HasModified);
        Assert.True(behavior.HasModified);

        content.ResetCommand.Execute(null);

        Assert.False(content.HasModified);
        Assert.Equal(new DriverTableOptions(DriverTable.Standings).ColumnOrder, standings.ColumnOrder);
        Assert.True(behavior.HasModified); // only the section reset
    }

    private static WidgetSettingsContext NewContext(out DriverTableOptions standings)
    {
        standings = new DriverTableOptions(DriverTable.Standings);
        return new WidgetSettingsContext(standings, new DriverTableOptions(DriverTable.Relative), new FuelCalculatorOptions(),
            new FlagOptions(), new CockpitOptions(), new WeatherOptions(), new TrackInfoOptions(), new DeltaOptions(), new PedalTraceOptions(), SaveToStores: false);
    }
}

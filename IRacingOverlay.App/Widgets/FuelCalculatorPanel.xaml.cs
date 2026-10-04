using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FuelCalculatorPanel : UserControl
{
    // Same reasoning as StandingsPanel.ColumnVisibilityProperty: XAML visibility bindings latch onto
    // whatever object this returns during InitializeComponent, so swapping in MainWindow's persisted
    // instance later has to be a DependencyProperty change to be noticed.
    // The order of the cells and which group leads are arranged here, so that follows the instance
    // too.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(FuelCalculatorOptions), typeof(FuelCalculatorPanel),
        new PropertyMetadata(new FuelCalculatorOptions(), (d, e) => ((FuelCalculatorPanel)d).OnOptionsChanged(e)));

    private readonly Dictionary<FuelCell, UIElement> _cells;

    public FuelCalculatorOptions Options
    {
        get => (FuelCalculatorOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public FuelCalculatorPanel()
    {
        InitializeComponent();
        _cells = new Dictionary<FuelCell, UIElement>
        {
            [FuelCell.LastLap] = LastLapCell,
            [FuelCell.Average] = AverageCell,
            [FuelCell.Minimum] = MinimumCell,
            [FuelCell.Maximum] = MaximumCell,
            [FuelCell.FuelRemaining] = FuelRemainingCell,
            [FuelCell.FuelToFinish] = FuelToFinishCell,
            [FuelCell.Refuel] = RefuelCell,
        };
        PropertyChangedEventManager.AddHandler(Options, OnOptionChanged, string.Empty);
        ArrangeCells();
    }

    /// <summary>Puts each group's cells in the options' order, and the leading group above the gap.</summary>
    private void ArrangeCells()
    {
        Fill(UsageCells, Options.UsageOrder);
        Fill(StrategyCells, Options.StrategyOrder);
        Grid.SetRow(UsageCells, Options.UsageOnTop ? 0 : 2);
        Grid.SetRow(StrategyCells, Options.UsageOnTop ? 2 : 0);
    }

    private void Fill(Panel group, IReadOnlyList<FuelCell> order)
    {
        if (group.Children.Cast<UIElement>().SequenceEqual(order.Select(cell => _cells[cell])))
        {
            return;
        }

        group.Children.Clear();
        foreach (var cell in order)
        {
            group.Children.Add(_cells[cell]);
        }
    }

    private void OnOptionsChanged(DependencyPropertyChangedEventArgs e)
    {
        // Weak, so a thrown-away preview panel isn't kept alive by the long-lived shared options.
        if (e.OldValue is FuelCalculatorOptions old)
        {
            PropertyChangedEventManager.RemoveHandler(old, OnOptionChanged, string.Empty);
        }

        if (e.NewValue is FuelCalculatorOptions current)
        {
            PropertyChangedEventManager.AddHandler(current, OnOptionChanged, string.Empty);
        }

        ArrangeCells();
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FuelCalculatorOptions.UsageOrder) or nameof(FuelCalculatorOptions.StrategyOrder) or
            nameof(FuelCalculatorOptions.UsageOnTop))
        {
            ArrangeCells();
        }
    }

    public void UpdateState(FuelCalculatorState state)
    {
        LevelText.Text = state.LevelDisplay;
        LastLapText.Text = state.LastLapDisplay;
        AverageText.Text = state.AverageDisplay;
        MinText.Text = state.MinDisplay;
        MaxText.Text = state.MaxDisplay;
        // No unit after a "—": there's no amount for it to qualify yet.
        ShowUnit(LevelUnitText, state.LevelDisplay, state.VolumeUnit);
        ShowUnit(LastLapUnitText, state.LastLapDisplay, state.VolumeUnit);
        ShowUnit(AverageUnitText, state.AverageDisplay, state.VolumeUnit);
        ShowUnit(MinUnitText, state.MinDisplay, state.VolumeUnit);
        ShowUnit(MaxUnitText, state.MaxDisplay, state.VolumeUnit);
        LapsRemainingText.Text = state.LapsRemainingDisplay;
        FuelDeltaText.Text = state.FuelDeltaValueDisplay;
        FuelDeltaUnitText.Text = state.FuelDeltaUnitDisplay;
        RefuelText.Text = state.RefuelValueDisplay;
        RefuelUnitText.Text = state.RefuelUnitDisplay;

        // The header spells out which window the average covers, so the number is never ambiguous.
        AverageLabel.Text = Options.AverageSource.Label();

        var trackWidth = LevelTrack.ActualWidth;
        LevelFill.Width = trackWidth > 0 ? Math.Clamp(state.LevelPct, 0, 1) * trackWidth : 0;
    }

    private static void ShowUnit(TextBlock unit, string value, string volumeUnit) =>
        unit.Text = value == "—" ? "" : volumeUnit;
}

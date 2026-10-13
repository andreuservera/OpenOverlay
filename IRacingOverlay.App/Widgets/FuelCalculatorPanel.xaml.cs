using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.Overlay;
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
        };
        PropertyChangedEventManager.AddHandler(Options, OnOptionChanged, string.Empty);
        ArrangeCells();
    }

    /// <summary>The per-lap columns in the options' order. The other blocks have fixed places.</summary>
    private void ArrangeCells()
    {
        var order = Options.UsageOrder.Select(cell => _cells[cell]).ToList();
        if (UsageCells.Children.Cast<UIElement>().SequenceEqual(order))
        {
            return;
        }

        UsageCells.Children.Clear();
        foreach (var cell in order)
        {
            UsageCells.Children.Add(cell);
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
        if (e.PropertyName is nameof(FuelCalculatorOptions.UsageOrder))
        {
            ArrangeCells();
        }
    }

    public void UpdateState(FuelCalculatorState state)
    {
        LevelText.Text = state.LevelDisplay;
        // No unit after a "—": there's no amount for it to qualify yet.
        LevelUnitText.Text = UnitAfter(state.LevelDisplay, state.VolumeUnit);
        LapsRemainingText.Text = state.LapsRemainingDisplay;
        LastLapText.Text = state.LastLapDisplay;
        AverageText.Text = state.AverageDisplay;
        MinText.Text = state.MinDisplay;
        MaxText.Text = state.MaxDisplay;
        FuelToFinishText.Text = state.FuelToFinishValueDisplay;
        FuelToFinishUnitText.Text = UnitAfter(state.FuelToFinishValueDisplay, state.VolumeUnit);

        // What to add is an amount with a "+", in the warning amber; "not needed" and "full" stay text.
        RefuelText.Text = state.NeedsRefuel ? $"+{state.RefuelValueDisplay}" : state.RefuelValueDisplay;
        RefuelUnitText.Text = state.RefuelUnitDisplay.Length > 0 ? $" {state.RefuelUnitDisplay}" : "";
        RefuelLine.Foreground = state.NeedsRefuel ? StatePalette.Warning : StatePalette.TextPrimary;

        // The caption spells out which window the average covers, so the number is never ambiguous.
        AverageLabel.Text = Options.AverageSource.Label();

        var trackWidth = LevelTrack.ActualWidth;
        LevelFill.Width = trackWidth > 0 ? Math.Clamp(state.LevelPct, 0, 1) * trackWidth : 0;
        var neededPct = state.CanProjectToFinish && state.TankCapacityLiters > 0
            ? state.FuelToFinishLiters / state.TankCapacityLiters
            : double.NaN;
        if (trackWidth > 0 && neededPct is > 0 and <= 1)
        {
            NeededMark.Margin = new Thickness(Math.Max(0, neededPct * trackWidth - 1), 0, 0, 0);
            NeededMark.Visibility = Visibility.Visible;
        }
        else
        {
            NeededMark.Visibility = Visibility.Collapsed;
        }
    }

    private static string UnitAfter(string value, string volumeUnit) => value == "—" ? "" : $" {volumeUnit}";
}

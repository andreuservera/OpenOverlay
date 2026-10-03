using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FuelCalculatorPanel : UserControl
{
    // Same reasoning as StandingsPanel.ColumnVisibilityProperty: XAML visibility bindings latch onto
    // whatever object this returns during InitializeComponent, so swapping in MainWindow's persisted
    // instance later has to be a DependencyProperty change to be noticed.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(FuelCalculatorOptions), typeof(FuelCalculatorPanel),
        new PropertyMetadata(new FuelCalculatorOptions()));

    public FuelCalculatorOptions Options
    {
        get => (FuelCalculatorOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public FuelCalculatorPanel()
    {
        InitializeComponent();
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

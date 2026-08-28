using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FuelCalculatorPanel : UserControl
{
    private static readonly Brush Ample = StatePalette.Positive;
    private static readonly Brush Short = StatePalette.Critical;
    private static readonly Brush Neutral = StatePalette.TextPrimary;
    private static readonly Brush BarAmple = StatePalette.Warning;

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
        LapsRemainingText.Text = state.LapsRemainingDisplay;
        FuelDeltaText.Text = state.FuelDeltaDisplay;
        RefuelText.Text = state.RefuelDisplay;

        // The header spells out which window the average covers, so the number is never ambiguous.
        AverageLabel.Text = Options.AverageSource.Label();

        var trackWidth = LevelTrack.ActualWidth;
        LevelFill.Width = trackWidth > 0 ? Math.Clamp(state.LevelPct, 0, 1) * trackWidth : 0;

        // Only color the strategy figures once both sides of the comparison are known — in an open
        // practice session there's no finish to be short of, so red/green would be meaningless.
        if (state.CanProjectToFinish)
        {
            var makingIt = state.FuelDeltaLiters >= 0;
            FuelDeltaText.Foreground = makingIt ? Ample : Short;
            LapsRemainingText.Foreground = makingIt ? Ample : Short;
            RefuelText.Foreground = state.IsShortOfFuel ? Short : Ample;
            LevelFill.Fill = makingIt ? BarAmple : Short;
        }
        else
        {
            FuelDeltaText.Foreground = Neutral;
            LapsRemainingText.Foreground = Neutral;
            RefuelText.Foreground = Neutral;
            LevelFill.Fill = BarAmple;
        }
    }
}

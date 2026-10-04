using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TrackInfoPanel : UserControl
{
    private static readonly Brush UsageEmpty = StatePalette.TrackEmpty;
    private static readonly Brush UsageClean = StatePalette.Info;
    private static readonly Brush UsageLow = StatePalette.Positive;
    private static readonly Brush UsageMedium = StatePalette.Warning;
    private static readonly Brush UsageHigh = StatePalette.Critical;

    // The XAML binds Options' fields straight off this property, so swapping in the persisted
    // instance is a DependencyProperty change and every binding follows it.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(TrackInfoOptions), typeof(TrackInfoPanel), new PropertyMetadata(new TrackInfoOptions()));

    public TrackInfoPanel()
    {
        InitializeComponent();
    }

    public TrackInfoOptions Options
    {
        get => (TrackInfoOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public void UpdateState(TrackInfoState state)
    {
        TrackNameText.Text = state.TrackNameDisplay;
        SessionLabelText.Text = state.SessionLabelDisplay;
        AirTempText.Text = state.AirTempDisplay;
        SkyIcon.Condition = state.Condition;
        TrackTempText.Text = state.TrackTempDisplay;
        WindText.Text = state.WindDisplay;
        HumidityText.Text = state.HumidityDisplay;
        TrackUsageText.Text = state.TrackUsageDisplay;
        SetUsageBar(state.TrackUsageLevel);
        TimeRemainingText.Text = state.TimeRemainingDisplay;
        LapText.Text = state.LapDisplay;
    }

    private void SetUsageBar(int? level)
    {
        // Unknown states leave every segment dark; "clean" is level 0 and still lights the first one.
        var filled = level is { } value ? value + 1 : 0;
        var fill = level switch
        {
            <= 1 => UsageClean,
            <= 3 => UsageLow,
            <= 5 => UsageMedium,
            _ => UsageHigh,
        };

        for (var i = 0; i < TrackUsageBar.Children.Count; i++)
        {
            ((Rectangle)TrackUsageBar.Children[i]).Fill = i < filled ? fill : UsageEmpty;
        }
    }
}

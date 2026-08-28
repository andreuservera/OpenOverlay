using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TrackInfoPanel : UserControl
{
    private static readonly Brush UsageEmpty = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly Brush UsageClean = new SolidColorBrush(Color.FromRgb(0x8F, 0xD3, 0xFF));
    private static readonly Brush UsageLow = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x7A));
    private static readonly Brush UsageMedium = new SolidColorBrush(Color.FromRgb(0xFF, 0xB2, 0x38));
    private static readonly Brush UsageHigh = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D));

    public TrackInfoPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(TrackInfoState state)
    {
        TrackNameText.Text = state.TrackNameDisplay;
        SessionLabelText.Text = state.SessionLabelDisplay;
        AirTempText.Text = state.AirTempDisplay;
        TrackTempText.Text = state.TrackTempDisplay;
        WindText.Text = state.WindDisplay;
        HumidityText.Text = state.HumidityDisplay;
        TrackUsageText.Text = state.TrackUsageDisplay;
        SetUsageBar(state.TrackUsageLevel);
        TimeRemainingText.Text = state.TimeRemainingDisplay;
        LapsRemainingText.Text = state.LapsRemainingDisplay;
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

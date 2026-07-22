using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class PedalTracePanel : UserControl
{
    private readonly Polyline _throttleLine = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x7A)),
        StrokeThickness = 2,
    };

    private readonly Polyline _brakeLine = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D)),
        StrokeThickness = 2,
    };

    public PedalTracePanel()
    {
        InitializeComponent();
        TraceCanvas.Children.Add(_throttleLine);
        TraceCanvas.Children.Add(_brakeLine);
    }

    public void UpdateState(PedalTraceState state)
    {
        SetBar(ThrottleTrack, ThrottleFill, state.Throttle);
        SetBar(BrakeTrack, BrakeFill, state.Brake);
        SetBar(ClutchTrack, ClutchFill, state.Clutch);

        DrawTrace(_throttleLine, state.ThrottleHistory);
        DrawTrace(_brakeLine, state.BrakeHistory);
    }

    private static void SetBar(Border track, Rectangle fill, double value)
    {
        var height = track.ActualHeight;
        fill.Height = height > 0 ? Math.Clamp(value, 0, 1) * height : 0;
    }

    private void DrawTrace(Polyline line, IReadOnlyList<double> history)
    {
        var width = TraceCanvas.ActualWidth;
        var height = TraceCanvas.ActualHeight;
        if (width <= 0 || height <= 0 || history.Count < 2)
        {
            line.Points = [];
            return;
        }

        var points = new PointCollection(history.Count);
        for (var i = 0; i < history.Count; i++)
        {
            var x = width * i / (history.Count - 1);
            var y = height * (1 - Math.Clamp(history[i], 0, 1));
            points.Add(new Point(x, y));
        }

        line.Points = points;
    }
}

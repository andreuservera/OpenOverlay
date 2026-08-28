using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class PedalTracePanel : UserControl
{
    private static readonly Brush BrakeStroke = StatePalette.Critical;
    private static readonly Brush AbsStroke = StatePalette.Accent;

    private readonly Polyline _throttleLine = new()
    {
        Stroke = StatePalette.Positive,
        StrokeThickness = 2,
    };

    // The brake trace is drawn as one polyline per run of constant ABS state, so only the stretch
    // where ABS was actually intervening turns yellow. Pooled and reused across ticks.
    private readonly List<Polyline> _brakeSegments = [];

    public PedalTracePanel()
    {
        InitializeComponent();
        TraceCanvas.Children.Add(_throttleLine);
    }

    public void UpdateState(PedalTraceState state)
    {
        SetBar(ThrottleTrack, ThrottleFill, state.Throttle);
        SetBar(BrakeTrack, BrakeFill, state.Brake);
        SetBar(ClutchTrack, ClutchFill, state.Clutch);

        DrawTrace(_throttleLine, state.ThrottleHistory);
        DrawBrakeTrace(state.BrakeHistory, state.AbsHistory);
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

        line.Points = BuildPoints(history, 0, history.Count - 1, width, height);
    }

    private void DrawBrakeTrace(IReadOnlyList<double> history, IReadOnlyList<bool> absHistory)
    {
        var width = TraceCanvas.ActualWidth;
        var height = TraceCanvas.ActualHeight;
        var used = 0;

        if (width > 0 && height > 0 && history.Count >= 2)
        {
            var runStart = 0;
            while (runStart < history.Count)
            {
                var absActive = runStart < absHistory.Count && absHistory[runStart];

                var runEnd = runStart;
                while (runEnd + 1 < history.Count && (runEnd + 1 < absHistory.Count && absHistory[runEnd + 1]) == absActive)
                {
                    runEnd++;
                }

                // Start one sample early so consecutive runs join with no gap; the connecting
                // segment takes the new run's color, marking exactly where ABS kicked in.
                var from = runStart > 0 ? runStart - 1 : runStart;
                if (runEnd > from)
                {
                    var segment = GetBrakeSegment(used++);
                    segment.Stroke = absActive ? AbsStroke : BrakeStroke;
                    segment.Points = BuildPoints(history, from, runEnd, width, height);
                    segment.Visibility = Visibility.Visible;
                }

                runStart = runEnd + 1;
            }
        }

        for (var i = used; i < _brakeSegments.Count; i++)
        {
            _brakeSegments[i].Visibility = Visibility.Collapsed;
        }
    }

    private Polyline GetBrakeSegment(int index)
    {
        while (_brakeSegments.Count <= index)
        {
            var segment = new Polyline { StrokeThickness = 2 };
            _brakeSegments.Add(segment);
            TraceCanvas.Children.Add(segment);
        }

        return _brakeSegments[index];
    }

    // x is scaled against the full history length, not the run, so segments line up end to end.
    private static PointCollection BuildPoints(IReadOnlyList<double> history, int from, int to, double width, double height)
    {
        var points = new PointCollection(to - from + 1);
        for (var i = from; i <= to; i++)
        {
            var x = width * i / (history.Count - 1);
            var y = height * (1 - Math.Clamp(history[i], 0, 1));
            points.Add(new Point(x, y));
        }

        return points;
    }
}

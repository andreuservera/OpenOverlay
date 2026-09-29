using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// The throttle/brake trace, drawn straight into the visual: one geometry per line and no child
/// shapes, so a new frame costs a single re-render instead of a layout pass over polylines. The
/// brake line changes colour only where ABS was intervening.
/// </summary>
public sealed class PedalTraceGraph : FrameworkElement
{
    private const double Thickness = 2;

    public static readonly DependencyProperty ThrottleBrushProperty = Register(nameof(ThrottleBrush), Brushes.LimeGreen);
    public static readonly DependencyProperty BrakeBrushProperty = Register(nameof(BrakeBrush), Brushes.Red);
    public static readonly DependencyProperty AbsBrushProperty = Register(nameof(AbsBrush), Brushes.Gold);
    public static readonly DependencyProperty ClutchBrushProperty = Register(nameof(ClutchBrush), Brushes.DeepSkyBlue);

    private PedalTraceState _state = PedalTraceState.Empty;

    public Brush ThrottleBrush { get => (Brush)GetValue(ThrottleBrushProperty); set => SetValue(ThrottleBrushProperty, value); }

    public Brush BrakeBrush { get => (Brush)GetValue(BrakeBrushProperty); set => SetValue(BrakeBrushProperty, value); }

    public Brush AbsBrush { get => (Brush)GetValue(AbsBrushProperty); set => SetValue(AbsBrushProperty, value); }

    public Brush ClutchBrush { get => (Brush)GetValue(ClutchBrushProperty); set => SetValue(ClutchBrushProperty, value); }

    public void SetTrace(PedalTraceState state)
    {
        if (ReferenceEquals(state, _state))
        {
            return;
        }

        _state = state;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var state = _state;
        if (width <= 0 || height <= 0 || state.Positions.Count < 2)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        var last = state.Positions.Count - 1;
        // Clutch goes underneath: it's the least-used pedal, and throttle/brake must stay on top.
        if (state.ClutchHistory.Any(v => v > 0))
        {
            dc.DrawGeometry(null, Pen(ClutchBrush), Line(state.ClutchHistory, state.Positions, 0, last, width, height));
        }

        dc.DrawGeometry(null, Pen(ThrottleBrush), Line(state.ThrottleHistory, state.Positions, 0, last, width, height));

        // One run per stretch of constant ABS state; each run starts on the previous run's last
        // sample so the line stays joined, and the join takes the new colour.
        var brake = state.BrakeHistory;
        var abs = state.AbsHistory;
        var runStart = 0;
        while (runStart < brake.Count)
        {
            var active = runStart < abs.Count && abs[runStart];
            var runEnd = runStart;
            while (runEnd + 1 < brake.Count && (runEnd + 1 < abs.Count && abs[runEnd + 1]) == active)
            {
                runEnd++;
            }

            var from = runStart > 0 ? runStart - 1 : runStart;
            if (runEnd > from)
            {
                dc.DrawGeometry(null, Pen(active ? AbsBrush : BrakeBrush), Line(brake, state.Positions, from, runEnd, width, height));
            }

            runStart = runEnd + 1;
        }

        dc.Pop();
    }

    private static Geometry Line(IReadOnlyList<double> values, IReadOnlyList<double> positions, int from, int to, double width, double height)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Point(values, positions, from, width, height), false, false);
            for (var i = from + 1; i <= to; i++)
            {
                context.LineTo(Point(values, positions, i, width, height), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point Point(IReadOnlyList<double> values, IReadOnlyList<double> positions, int i, double width, double height) =>
        new(width * positions[i], height * (1 - Math.Clamp(values[i], 0, 1)));

    private static Pen Pen(Brush brush) => new(brush, Thickness) { LineJoin = PenLineJoin.Round };

    private static DependencyProperty Register(string name, Brush defaultValue) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(PedalTraceGraph),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}

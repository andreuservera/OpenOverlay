using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IRacingOverlay.Coaching.Reports;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>What <see cref="LapCompareChart"/> draws: two laps between two points of the lap, the
/// time between them, and where the corners are.</summary>
public sealed record LapCompareData(StoredLap A, StoredLap B, int FromM, int ToM, float[] Delta, IReadOnlyList<(int StartM, int EndM, string Name)> Corners);

/// <summary>
/// Two laps overlaid on distance, the way telemetry tools show them: speed on top, the time the
/// second lap is behind (above the line) or ahead underneath it, then throttle and brake, then
/// gear. The first lap is drawn solid, the second dashed.
/// </summary>
public sealed class LapCompareChart : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(LapCompareData), typeof(LapCompareChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush ThrottleBrush = Frozen(Color.FromRgb(0x3D, 0xD6, 0x8C));
    private static readonly Brush BrakeBrush = Frozen(Color.FromRgb(0xF2, 0x6D, 0x6D));
    private static readonly Brush GridBrush = Frozen(Color.FromArgb(0x40, 0x8B, 0x95, 0xA3));
    private static readonly Brush CornerBrush = Frozen(Color.FromArgb(0x18, 0x8B, 0x95, 0xA3));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x8B, 0x95, 0xA3));
    private static readonly Brush SlowerBrush = Frozen(Color.FromRgb(0xF2, 0x6D, 0x6D));
    private static readonly Brush QuickerBrush = Frozen(Color.FromRgb(0x3D, 0xD6, 0x8C));
    private static readonly Typeface Face = new("Segoe UI");

    public LapCompareData? Data
    {
        get => (LapCompareData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 700 : availableSize.Width, 360);

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is not { } d || d.ToM <= d.FromM)
        {
            return;
        }

        const double left = 52, right = 8, top = 8, gap = 14, deltaHeight = 60, pedalHeight = 56, gearHeight = 34, bottomAxis = 18;
        var width = Math.Max(10, ActualWidth - left - right);
        var speedHeight = Math.Max(60, ActualHeight - top - 3 * gap - deltaHeight - pedalHeight - gearHeight - bottomAxis);
        var speedTop = top;
        var deltaTop = speedTop + speedHeight + gap;
        var pedalTop = deltaTop + deltaHeight + gap;
        var gearTop = pedalTop + pedalHeight + gap;
        var bottom = gearTop + gearHeight;

        var from = d.FromM;
        var to = Math.Min(d.ToM, Math.Min(d.A.Length, d.B.Length) - 1);
        double X(double m) => left + (m - from) / Math.Max(1, to - from) * width;
        // One point per pixel or so is all a line needs.
        var step = Math.Max(1, (int)((to - from) / (width * 1.5)));

        var pen = new Pen(GridBrush, 1);

        // Corners as shaded bands, named at the top; a name that would run into the previous one is left out.
        var nameEnd = double.MinValue;
        foreach (var (s, e, name) in d.Corners)
        {
            if (e < from || s > to)
            {
                continue;
            }

            var x0 = X(Math.Max(s, from));
            var x1 = X(Math.Min(e, to));
            dc.DrawRectangle(CornerBrush, null, new Rect(x0, speedTop, Math.Max(1, x1 - x0), bottom - speedTop));
            var label = Text(name);
            if (x1 - x0 > 24 && x0 + 3 >= nameEnd)
            {
                dc.DrawText(label, new Point(x0 + 3, speedTop + 1));
                nameEnd = x0 + 3 + label.Width + 6;
            }
        }

        // Speed.
        var speeds = d.A.SpeedKmh[from..(to + 1)].Concat(d.B.SpeedKmh[from..(to + 1)]).ToList();
        var lo = Math.Floor((speeds.Min() - 5) / 20) * 20;
        var hi = Math.Ceiling((speeds.Max() + 5) / 20) * 20;
        double Ys(double v) => speedTop + (hi - v) / Math.Max(1, hi - lo) * speedHeight;
        var stepKmh = hi - lo > 160 ? 50 : hi - lo > 80 ? 40 : 20;
        for (var v = lo; v <= hi + 0.1; v += stepKmh)
        {
            dc.DrawLine(pen, new Point(left, Ys(v)), new Point(left + width, Ys(v)));
            DrawText(dc, $"{v:0}", new Point(left - 6, Ys(v)), true, true);
        }

        // Inside the plot at the bottom right, clear of the axis ticks: a lap or a corner ends fast, so the lines are high there.
        DrawText(dc, "km/h", new Point(left + width - 4, speedTop + speedHeight - 8), true, true);
        dc.DrawGeometry(null, new Pen(CornerTraceChart.TypicalBrush, 1.6) { DashStyle = new DashStyle([4, 2], 0) }, Line(d.B.SpeedKmh, from, to, step, X, Ys));
        dc.DrawGeometry(null, new Pen(CornerTraceChart.BestBrush, 1.8), Line(d.A.SpeedKmh, from, to, step, X, Ys));

        // Time delta: above the middle line the second lap is behind.
        var maxDelta = Math.Max(0.05, d.Delta.Select(Math.Abs).DefaultIfEmpty(0).Max());
        var mid = deltaTop + deltaHeight / 2;
        double Yd(double v) => mid - v / maxDelta * (deltaHeight / 2);
        dc.DrawLine(pen, new Point(left, mid), new Point(left + width, mid));
        DrawText(dc, $"+{maxDelta:0.00}", new Point(left - 6, deltaTop + 4), true, true);
        DrawText(dc, $"−{maxDelta:0.00}", new Point(left - 6, deltaTop + deltaHeight - 4), true, true);
        DrawText(dc, "Δ s", new Point(left - 6, mid), true, true);
        var last = d.Delta.Length > 0 ? d.Delta[^1] : 0;
        dc.DrawGeometry(null, new Pen(last >= 0 ? SlowerBrush : QuickerBrush, 1.6), Line(d.Delta, 0, d.Delta.Length - 1, step, i => X(from + i), Yd));

        // Pedals.
        double Yp(double v) => pedalTop + (1 - v) * pedalHeight;
        dc.DrawLine(pen, new Point(left, pedalTop), new Point(left + width, pedalTop));
        dc.DrawLine(pen, new Point(left, pedalTop + pedalHeight), new Point(left + width, pedalTop + pedalHeight));
        DrawText(dc, CoachingText.T("coaching.chart.pedals"), new Point(left - 6, pedalTop + pedalHeight / 2), true, true);
        var dashed = new DashStyle([4, 2], 0);
        dc.PushOpacity(0.6);
        dc.DrawGeometry(null, new Pen(ThrottleBrush, 1.3) { DashStyle = dashed }, Line(d.B.Throttle, from, to, step, X, Yp));
        dc.DrawGeometry(null, new Pen(BrakeBrush, 1.3) { DashStyle = dashed }, Line(d.B.Brake, from, to, step, X, Yp));
        dc.Pop();
        dc.DrawGeometry(null, new Pen(ThrottleBrush, 1.5), Line(d.A.Throttle, from, to, step, X, Yp));
        dc.DrawGeometry(null, new Pen(BrakeBrush, 1.5), Line(d.A.Brake, from, to, step, X, Yp));

        // Gear.
        var gears = d.A.Gear[from..(to + 1)].Concat(d.B.Gear[from..(to + 1)]).Select(g => (int)g).Where(g => g > 0).DefaultIfEmpty(1).ToList();
        var gMax = Math.Max(2, gears.Max());
        double Yg(double g) => gearTop + gearHeight - Math.Max(0, g) / gMax * gearHeight;
        dc.DrawLine(pen, new Point(left, gearTop + gearHeight), new Point(left + width, gearTop + gearHeight));
        DrawText(dc, CoachingText.T("coaching.chart.gear"), new Point(left - 6, gearTop + gearHeight / 2), true, true);
        var gb = d.B.Gear.Select(g => (float)g).ToArray();
        var ga = d.A.Gear.Select(g => (float)g).ToArray();
        dc.DrawGeometry(null, new Pen(CornerTraceChart.TypicalBrush, 1.2) { DashStyle = dashed }, Line(gb, from, to, 1, X, Yg));
        dc.DrawGeometry(null, new Pen(CornerTraceChart.BestBrush, 1.4), Line(ga, from, to, 1, X, Yg));

        // Distance labels.
        foreach (var m in new[] { from, (from + to) / 2, to })
        {
            var ft = Text($"{m} m");
            var x = Math.Clamp(X(m) - ft.Width / 2, left, left + width - ft.Width);
            dc.DrawText(ft, new Point(x, bottom + 3));
        }
    }

    private static StreamGeometry Line(float[] values, int from, int to, int step, Func<double, double> x, Func<double, double> y)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x(from), y(values[from])), isFilled: false, isClosed: false);
            for (var i = from + step; i <= to; i += step)
            {
                ctx.LineTo(new Point(x(i), y(values[i])), isStroked: true, isSmoothJoin: true);
            }

            ctx.LineTo(new Point(x(to), y(values[to])), isStroked: true, isSmoothJoin: true);
        }

        g.Freeze();
        return g;
    }

    private void DrawText(DrawingContext dc, string s, Point at, bool alignRight, bool middle)
    {
        var ft = Text(s);
        dc.DrawText(ft, new Point(alignRight ? at.X - ft.Width : at.X, middle ? at.Y - ft.Height / 2 : at.Y));
    }

    private FormattedText Text(string s) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

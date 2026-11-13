using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IRacingOverlay.Coaching.Reports;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>
/// Speed through a corner on two passes, best against typical, with the pedals underneath: where
/// the typical pass brakes earlier, carries less speed or gets back on the throttle later is where
/// the lines part. Distance along the lap runs left to right.
/// </summary>
public sealed class CornerTraceChart : FrameworkElement
{
    public static readonly DependencyProperty BestProperty = DependencyProperty.Register(
        nameof(Best), typeof(CornerTrace), typeof(CornerTraceChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TypicalProperty = DependencyProperty.Register(
        nameof(Typical), typeof(CornerTrace), typeof(CornerTraceChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ApexMProperty = DependencyProperty.Register(
        nameof(ApexM), typeof(int), typeof(CornerTraceChart), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly Brush BestBrush = Frozen(Color.FromRgb(0xFF, 0xD2, 0x4D));
    public static readonly Brush TypicalBrush = Frozen(Color.FromRgb(0x6C, 0xB4, 0xFF));
    private static readonly Brush ThrottleBrush = Frozen(Color.FromRgb(0x3D, 0xD6, 0x8C));
    private static readonly Brush BrakeBrush = Frozen(Color.FromRgb(0xF2, 0x6D, 0x6D));
    private static readonly Brush GridBrush = Frozen(Color.FromArgb(0x40, 0x8B, 0x95, 0xA3));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x8B, 0x95, 0xA3));
    private static readonly Typeface Face = new("Segoe UI");

    public CornerTrace? Best
    {
        get => (CornerTrace?)GetValue(BestProperty);
        set => SetValue(BestProperty, value);
    }

    public CornerTrace? Typical
    {
        get => (CornerTrace?)GetValue(TypicalProperty);
        set => SetValue(TypicalProperty, value);
    }

    /// <summary>Where the corner's apex is, metres from the line; -1 for none.</summary>
    public int ApexM
    {
        get => (int)GetValue(ApexMProperty);
        set => SetValue(ApexMProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, 260);

    protected override void OnRender(DrawingContext dc)
    {
        var traces = new[] { Best, Typical }.OfType<CornerTrace>().Where(t => t.SpeedKmh.Length > 1).ToList();
        if (traces.Count == 0)
        {
            return;
        }

        const double left = 44, right = 8, top = 8, gap = 22, pedalHeight = 56, bottomAxis = 18;
        var width = Math.Max(10, ActualWidth - left - right);
        var speedHeight = Math.Max(40, ActualHeight - top - gap - pedalHeight - bottomAxis);
        var speedTop = top;
        var pedalTop = speedTop + speedHeight + gap;

        var startM = traces.Min(t => t.StartM);
        var endM = traces.Max(t => t.StartM + (t.SpeedKmh.Length - 1) * t.StepM);
        if (endM <= startM)
        {
            return;
        }

        var min = traces.Min(t => t.SpeedKmh.Min());
        var max = traces.Max(t => t.SpeedKmh.Max());
        var lo = Math.Floor((min - 5) / 20) * 20;
        var hi = Math.Ceiling((max + 5) / 20) * 20;
        double X(double m) => left + (m - startM) / (endM - startM) * width;
        double Ys(double kmh) => speedTop + (hi - kmh) / (hi - lo) * speedHeight;
        double Yp(double v) => pedalTop + (1 - v) * pedalHeight;

        // Grid and speed labels every 20 or 40 km/h.
        var pen = new Pen(GridBrush, 1);
        var stepKmh = (hi - lo) > 120 ? 40 : 20;
        for (var v = lo; v <= hi + 0.1; v += stepKmh)
        {
            dc.DrawLine(pen, new Point(left, Ys(v)), new Point(left + width, Ys(v)));
            DrawText(dc, $"{v:0}", new Point(left - 6, Ys(v)), alignRight: true, middle: true);
        }

        dc.DrawLine(pen, new Point(left, pedalTop), new Point(left + width, pedalTop));
        dc.DrawLine(pen, new Point(left, pedalTop + pedalHeight), new Point(left + width, pedalTop + pedalHeight));
        DrawText(dc, "km/h", new Point(left - 6, speedTop + speedHeight + 9), alignRight: true, middle: true);
        DrawText(dc, CoachingText.T("coaching.chart.pedals"), new Point(left - 6, pedalTop + pedalHeight / 2), alignRight: true, middle: true);

        if (ApexM >= startM && ApexM <= endM)
        {
            var dashed = new Pen(GridBrush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(dashed, new Point(X(ApexM), speedTop), new Point(X(ApexM), pedalTop + pedalHeight));
            DrawText(dc, CoachingText.T("coaching.chart.apex"), new Point(X(ApexM) + 4, speedTop + 2), alignRight: false, middle: false);
        }

        // Distance labels at both ends and the middle.
        foreach (var m in new[] { startM, (startM + endM) / 2, endM })
        {
            var ft = Text($"{m} m");
            var x = Math.Clamp(X(m) - ft.Width / 2, left, left + width - ft.Width);
            dc.DrawText(ft, new Point(x, pedalTop + pedalHeight + 3));
        }

        // Typical first, so the best pass is drawn on top.
        if (Typical is { } typical)
        {
            DrawPass(dc, typical, X, Ys, Yp, TypicalBrush, dashed: true);
        }

        if (Best is { } best)
        {
            DrawPass(dc, best, X, Ys, Yp, BestBrush, dashed: false);
        }
    }

    private static void DrawPass(DrawingContext dc, CornerTrace t, Func<double, double> x, Func<double, double> ys, Func<double, double> yp, Brush speedBrush, bool dashed)
    {
        var style = dashed ? new DashStyle([4, 2], 0) : DashStyles.Solid;
        dc.DrawGeometry(null, new Pen(speedBrush, 2) { DashStyle = style, LineJoin = PenLineJoin.Round }, Line(t, t.SpeedKmh, x, ys));
        var opacity = dashed ? 0.55 : 1;
        dc.PushOpacity(opacity);
        dc.DrawGeometry(null, new Pen(ThrottleBrush, 1.5) { DashStyle = style }, Line(t, t.Throttle, x, yp));
        dc.DrawGeometry(null, new Pen(BrakeBrush, 1.5) { DashStyle = style }, Line(t, t.Brake, x, yp));
        dc.Pop();
    }

    private static StreamGeometry Line(CornerTrace t, float[] values, Func<double, double> x, Func<double, double> y)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x(t.StartM), y(values[0])), isFilled: false, isClosed: false);
            for (var i = 1; i < values.Length; i++)
            {
                ctx.LineTo(new Point(x(t.StartM + i * t.StepM), y(values[i])), isStroked: true, isSmoothJoin: true);
            }
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

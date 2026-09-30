using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// The forecast icon, drawn straight into the visual like <see cref="FlagIcon"/>: flat two-tone
/// shapes in a 48-unit box, crisp at every scale. Colours are dependency properties so the panel can
/// hand it the design system's (theme-overridable) brushes. Only re-renders when the condition or a
/// brush actually changes.
/// </summary>
public sealed class WeatherIcon : FrameworkElement
{
    private const double Box = 48;

    private static readonly Geometry Cloud = CreateCloud();

    public static readonly DependencyProperty ConditionProperty = Register(nameof(Condition), WeatherCondition.Unknown);
    public static readonly DependencyProperty SunBrushProperty = Register<Brush>(nameof(SunBrush), Brushes.Gold);
    public static readonly DependencyProperty MoonBrushProperty = Register<Brush>(nameof(MoonBrush), Brushes.WhiteSmoke);
    public static readonly DependencyProperty CloudBrushProperty = Register<Brush>(nameof(CloudBrush), Brushes.LightGray);
    public static readonly DependencyProperty CloudShadeBrushProperty = Register<Brush>(nameof(CloudShadeBrush), Brushes.Gray);
    public static readonly DependencyProperty RainBrushProperty = Register<Brush>(nameof(RainBrush), Brushes.DeepSkyBlue);

    public WeatherCondition Condition
    {
        get => (WeatherCondition)GetValue(ConditionProperty);
        set => SetValue(ConditionProperty, value);
    }

    public Brush SunBrush { get => (Brush)GetValue(SunBrushProperty); set => SetValue(SunBrushProperty, value); }

    public Brush MoonBrush { get => (Brush)GetValue(MoonBrushProperty); set => SetValue(MoonBrushProperty, value); }

    public Brush CloudBrush { get => (Brush)GetValue(CloudBrushProperty); set => SetValue(CloudBrushProperty, value); }

    public Brush CloudShadeBrush { get => (Brush)GetValue(CloudShadeBrushProperty); set => SetValue(CloudShadeBrushProperty, value); }

    public Brush RainBrush { get => (Brush)GetValue(RainBrushProperty); set => SetValue(RainBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var scale = size / Box;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, (ActualWidth - size) / 2, (ActualHeight - size) / 2));

        switch (Condition)
        {
            case WeatherCondition.Sunny:
                Sun(dc, new Point(24, 24), 9);
                break;
            case WeatherCondition.PartlyCloudy:
                Sun(dc, new Point(30, 16), 7);
                CloudAt(dc, CloudBrush, 0.82, -3, 6);
                break;
            case WeatherCondition.Cloudy:
                CloudAt(dc, CloudShadeBrush, 0.72, 8, -6);
                CloudAt(dc, CloudBrush, 0.9, -2, 4);
                break;
            case WeatherCondition.Rain:
                CloudAt(dc, CloudBrush, 0.9, 0, -6);
                Drops(dc);
                break;
            case WeatherCondition.NightClear:
                Moon(dc, new Point(22, 25), 12);
                dc.DrawEllipse(MoonBrush, null, new Point(37, 11), 1.6, 1.6);
                dc.DrawEllipse(MoonBrush, null, new Point(40, 20), 1.1, 1.1);
                break;
            case WeatherCondition.NightCloudy:
                Moon(dc, new Point(30, 15), 8.5);
                CloudAt(dc, CloudBrush, 0.82, -3, 6);
                break;
            case WeatherCondition.NightRain:
                Moon(dc, new Point(35, 10), 7.5);
                CloudAt(dc, CloudBrush, 0.84, -2, -4);
                Drops(dc);
                break;
            default:
                dc.PushOpacity(0.5);
                CloudAt(dc, CloudShadeBrush, 0.9, 2, 3);
                dc.Pop();
                break;
        }

        dc.Pop();
    }

    private void Sun(DrawingContext dc, Point center, double radius)
    {
        var rays = new Pen(SunBrush, radius * 0.28) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            var (sin, cos) = Math.SinCos(angle);
            dc.DrawLine(rays,
                new Point(center.X + (cos * radius * 1.45), center.Y + (sin * radius * 1.45)),
                new Point(center.X + (cos * radius * 1.95), center.Y + (sin * radius * 1.95)));
        }

        dc.DrawEllipse(SunBrush, null, center, radius, radius);
    }

    private void Moon(DrawingContext dc, Point center, double radius)
    {
        var crescent = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new EllipseGeometry(center, radius, radius),
            new EllipseGeometry(new Point(center.X + (radius * 0.55), center.Y - (radius * 0.4)), radius * 0.85, radius * 0.85));
        dc.DrawGeometry(MoonBrush, null, crescent);
    }

    private static void CloudAt(DrawingContext dc, Brush brush, double scale, double x, double y)
    {
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, x + (Box * (1 - scale) / 2), y + (Box * (1 - scale) / 2)));
        dc.DrawGeometry(brush, null, Cloud);
        dc.Pop();
    }

    private void Drops(DrawingContext dc)
    {
        var pen = new Pen(RainBrush, 2.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var x in (double[])[17, 26, 35])
        {
            dc.DrawLine(pen, new Point(x, 35), new Point(x - 3, 43));
        }
    }

    // A cumulus in the 48 box: a rounded base with three overlapping puffs, filled as one shape.
    private static Geometry CreateCloud()
    {
        var cloud = new GeometryGroup { FillRule = FillRule.Nonzero };
        cloud.Children.Add(new RectangleGeometry(new Rect(6, 26, 36, 12), 6, 6));
        cloud.Children.Add(new EllipseGeometry(new Point(16, 27), 8, 8));
        cloud.Children.Add(new EllipseGeometry(new Point(27, 22), 10.5, 10.5));
        cloud.Children.Add(new EllipseGeometry(new Point(36, 28), 7, 7));
        cloud.Freeze();
        return cloud;
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(WeatherIcon),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// Wind dial drawn from the driver's seat: a top-down car sits in the middle with its nose always
/// up, the N/E/S/W ring turns with the car's heading, and the arrow comes in from the side the wind
/// blows from and stops just short of the car, so it shows where the wind hits it. Both angles
/// animate the short way round.
/// </summary>
public sealed class WindCompass : FrameworkElement
{
    private static readonly Duration Turn = new(TimeSpan.FromMilliseconds(350));
    private static readonly IEasingFunction Ease = CreateEase();
    private static readonly (string Letter, double Bearing)[] Cardinals = [("N", 0), ("E", 90), ("S", 180), ("W", 270)];
    private static readonly ConcurrentDictionary<(string, double, Brush), FormattedText> LetterCache = new();

    // The car, in units of its own length, centred on the origin with the nose at -Y.
    private const double CarLength = 0.34;
    private const double CarHalfWidth = 0.31;
    private static readonly Geometry CarBody = CreateCarBody();
    private static readonly Geometry CarWheels = CreateCarWheels();

    public static readonly DependencyProperty WindAngleProperty = Register(nameof(WindAngle), 0.0);
    public static readonly DependencyProperty CompassAngleProperty = Register(nameof(CompassAngle), 0.0);
    public static readonly DependencyProperty RingBrushProperty = Register<Brush>(nameof(RingBrush), Brushes.DimGray);
    public static readonly DependencyProperty LetterBrushProperty = Register<Brush>(nameof(LetterBrush), Brushes.Gray);
    public static readonly DependencyProperty NorthBrushProperty = Register<Brush>(nameof(NorthBrush), Brushes.OrangeRed);
    public static readonly DependencyProperty NoseBrushProperty = Register<Brush>(nameof(NoseBrush), Brushes.White);
    public static readonly DependencyProperty ArrowBrushProperty = Register<Brush>(nameof(ArrowBrush), Brushes.DeepSkyBlue);
    public static readonly DependencyProperty CarBrushProperty = Register<Brush>(nameof(CarBrush), Brushes.LightGray);

    private double? _windTarget;
    private double? _compassTarget;

    /// <summary>Where the wind comes from, degrees clockwise from the car's nose (animated).</summary>
    public double WindAngle { get => (double)GetValue(WindAngleProperty); set => SetValue(WindAngleProperty, value); }

    /// <summary>Where north sits, degrees clockwise from the car's nose (animated).</summary>
    public double CompassAngle { get => (double)GetValue(CompassAngleProperty); set => SetValue(CompassAngleProperty, value); }

    public Brush RingBrush { get => (Brush)GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    public Brush LetterBrush { get => (Brush)GetValue(LetterBrushProperty); set => SetValue(LetterBrushProperty, value); }

    public Brush NorthBrush { get => (Brush)GetValue(NorthBrushProperty); set => SetValue(NorthBrushProperty, value); }

    public Brush NoseBrush { get => (Brush)GetValue(NoseBrushProperty); set => SetValue(NoseBrushProperty, value); }

    public Brush ArrowBrush { get => (Brush)GetValue(ArrowBrushProperty); set => SetValue(ArrowBrushProperty, value); }

    public Brush CarBrush { get => (Brush)GetValue(CarBrushProperty); set => SetValue(CarBrushProperty, value); }

    /// <summary>Points the dial. Null wind hides the arrow; null heading hides the compass letters.
    /// An animated turn redraws at most <paramref name="frameRate"/> times a second.</summary>
    public void Point(double? windFromRelativeDeg, double? headingDeg, bool animate, int frameRate = 60)
    {
        _windTarget = Aim(WindAngleProperty, _windTarget, windFromRelativeDeg, animate, frameRate);
        _compassTarget = Aim(CompassAngleProperty, _compassTarget, headingDeg is { } heading ? -heading : null, animate, frameRate);
        InvalidateVisual();
    }

    private double? Aim(DependencyProperty property, double? current, double? target, bool animate, int frameRate)
    {
        if (target is not { } goal)
        {
            return null;
        }

        if (!animate || current is not { } from)
        {
            BeginAnimation(property, null);
            SetValue(property, goal);
            return goal;
        }

        var shortest = ((((goal - from) % 360) + 540) % 360) - 180;
        if (Math.Abs(shortest) < 0.5)
        {
            return from;
        }

        var to = from + shortest;
        var turn = new DoubleAnimation(to, Turn) { EasingFunction = Ease };
        Timeline.SetDesiredFrameRate(turn, frameRate);
        BeginAnimation(property, turn);
        return to;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size / 2) - 1;
        var ring = Math.Max(1.5, size * 0.035);

        dc.DrawEllipse(null, new Pen(RingBrush, ring), center, radius, radius);

        if (_compassTarget is not null)
        {
            DrawCompass(dc, center, radius, size);
        }

        // The car's nose: fixed at the top, so the whole dial reads from the driver's seat.
        var nose = size * 0.09;
        var noseGeometry = new StreamGeometry();
        using (var g = noseGeometry.Open())
        {
            g.BeginFigure(new Point(center.X, center.Y - radius + (nose * 1.1)), true, true);
            g.LineTo(new Point(center.X - (nose * 0.7), center.Y - radius - (nose * 0.15)), true, false);
            g.LineTo(new Point(center.X + (nose * 0.7), center.Y - radius - (nose * 0.15)), true, false);
        }

        dc.DrawGeometry(NoseBrush, null, noseGeometry);

        var length = size * CarLength;
        dc.PushTransform(new MatrixTransform(length, 0, 0, length, center.X, center.Y));
        dc.DrawGeometry(LetterBrush, null, CarWheels);
        dc.DrawGeometry(CarBrush, null, CarBody);
        dc.Pop();

        if (_windTarget is not null)
        {
            DrawWind(dc, center, radius, size);
        }
    }

    private void DrawCompass(DrawingContext dc, Point center, double radius, double size)
    {
        var tick = new Pen(LetterBrush, Math.Max(1, size * 0.02));
        for (var bearing = 45.0; bearing < 360; bearing += 90)
        {
            var angle = CompassAngle + bearing;
            dc.DrawLine(tick, OnCircle(center, radius - (size * 0.03), angle), OnCircle(center, radius - (size * 0.1), angle));
        }

        var fontSize = Math.Max(7, size * 0.17);
        var family = TextElement.GetFontFamily(this);
        foreach (var (letter, bearing) in Cardinals)
        {
            var brush = letter == "N" ? NorthBrush : LetterBrush;
            var text = LetterCache.GetOrAdd((letter, fontSize, brush), key => new FormattedText(
                key.Item1, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                key.Item2, key.Item3, VisualTreeHelper.GetDpi(this).PixelsPerDip));
            var at = OnCircle(center, radius - (fontSize * 0.72), CompassAngle + bearing);
            dc.DrawText(text, new Point(at.X - (text.Width / 2), at.Y - (text.Height / 2)));
        }
    }

    private void DrawWind(DrawingContext dc, Point center, double radius, double size)
    {
        var from = WindAngle;

        // One broad dart: its notched base just inside the ring on the upwind side, its tip stopping
        // short of the car's outline there, so it lands on the part of the car the wind hits.
        var tipReach = CarReach(from, size) + (size * 0.03);
        var baseReach = Math.Max(tipReach + (size * 0.12), radius - (size * 0.04));
        var length = baseReach - tipReach;
        var half = Math.Min(length * 0.5, size * 0.11);
        var tip = OnCircle(center, tipReach, from);
        var notch = OnCircle(center, baseReach - (length * 0.28), from);
        var back = OnCircle(center, baseReach, from);

        var (sin, cos) = Math.SinCos(from * Math.PI / 180);
        var dart = new StreamGeometry();
        using (var g = dart.Open())
        {
            g.BeginFigure(tip, true, true);
            g.LineTo(new Point(back.X + (cos * half), back.Y + (sin * half)), true, true);
            g.LineTo(notch, true, true);
            g.LineTo(new Point(back.X - (cos * half), back.Y - (sin * half)), true, true);
        }

        dc.DrawGeometry(ArrowBrush, null, dart);
    }

    /// <summary>Distance from the centre to the car's bounding box along a bearing.</summary>
    private static double CarReach(double degrees, double size)
    {
        var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
        var length = size * CarLength;
        var acrossSide = Math.Abs(sin) > 1e-6 ? (CarHalfWidth * length) / Math.Abs(sin) : double.MaxValue;
        var acrossEnd = Math.Abs(cos) > 1e-6 ? (0.5 * length) / Math.Abs(cos) : double.MaxValue;
        return Math.Min(acrossSide, acrossEnd);
    }

    // Top-down race car: body with the windscreen and rear window cut out so the panel shows through
    // them, and a rear wing on two struts spanning past the wheels.
    private static Geometry CreateCarBody()
    {
        var windows = new GeometryGroup();
        windows.Children.Add(Polygon(new(-0.19, -0.2), new(0.19, -0.2), new(0.16, -0.05), new(-0.16, -0.05)));
        windows.Children.Add(Polygon(new(-0.16, 0.14), new(0.16, 0.14), new(0.18, 0.25), new(-0.18, 0.25)));
        var car = new GeometryGroup { FillRule = FillRule.Nonzero };
        car.Children.Add(new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(-0.23, -0.5, 0.46, 0.88), 0.14, 0.16),
            windows));
        car.Children.Add(new RectangleGeometry(new Rect(-0.11, 0.36, 0.05, 0.06)));
        car.Children.Add(new RectangleGeometry(new Rect(0.06, 0.36, 0.05, 0.06)));
        car.Children.Add(new RectangleGeometry(new Rect(-CarHalfWidth, 0.41, CarHalfWidth * 2, 0.09), 0.02, 0.02));
        car.Freeze();
        return car;
    }

    private static Geometry CreateCarWheels()
    {
        var wheels = new GeometryGroup();
        foreach (var x in (double[])[-0.29, 0.17])
        {
            foreach (var y in (double[])[-0.38, 0.1])
            {
                wheels.Children.Add(new RectangleGeometry(new Rect(x, y, 0.12, 0.2), 0.03, 0.03));
            }
        }

        wheels.Freeze();
        return wheels;
    }

    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], true, true);
            g.PolyLineTo(points[1..], true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>A point on a circle, angle in degrees clockwise from straight up.</summary>
    private static Point OnCircle(Point center, double radius, double degrees)
    {
        var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
        return new Point(center.X + (sin * radius), center.Y - (cos * radius));
    }

    private static IEasingFunction CreateEase()
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(WindCompass),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// Wind dial drawn from the driver's seat: the car's nose is always up, the N/E/S/W ring turns
/// with the car's heading, and the arrow runs from where the wind comes from (tail) to where it
/// blows (head). Both angles animate the short way round.
/// </summary>
public sealed class WindCompass : FrameworkElement
{
    private static readonly Duration Turn = new(TimeSpan.FromMilliseconds(350));
    private static readonly IEasingFunction Ease = CreateEase();
    private static readonly (string Letter, double Bearing)[] Cardinals = [("N", 0), ("E", 90), ("S", 180), ("W", 270)];
    private static readonly ConcurrentDictionary<(string, double, Brush), FormattedText> LetterCache = new();

    public static readonly DependencyProperty WindAngleProperty = Register(nameof(WindAngle), 0.0);
    public static readonly DependencyProperty CompassAngleProperty = Register(nameof(CompassAngle), 0.0);
    public static readonly DependencyProperty RingBrushProperty = Register<Brush>(nameof(RingBrush), Brushes.DimGray);
    public static readonly DependencyProperty LetterBrushProperty = Register<Brush>(nameof(LetterBrush), Brushes.Gray);
    public static readonly DependencyProperty NorthBrushProperty = Register<Brush>(nameof(NorthBrush), Brushes.OrangeRed);
    public static readonly DependencyProperty NoseBrushProperty = Register<Brush>(nameof(NoseBrush), Brushes.White);
    public static readonly DependencyProperty ArrowBrushProperty = Register<Brush>(nameof(ArrowBrush), Brushes.DeepSkyBlue);

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

    /// <summary>Points the dial. Null wind hides the arrow; null heading hides the compass letters.</summary>
    public void Point(double? windFromRelativeDeg, double? headingDeg, bool animate)
    {
        _windTarget = Aim(WindAngleProperty, _windTarget, windFromRelativeDeg, animate);
        _compassTarget = Aim(CompassAngleProperty, _compassTarget, headingDeg is { } heading ? -heading : null, animate);
        InvalidateVisual();
    }

    private double? Aim(DependencyProperty property, double? current, double? target, bool animate)
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
        BeginAnimation(property, new DoubleAnimation(to, Turn) { EasingFunction = Ease });
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
        var width = Math.Max(2, size * 0.065);

        // Shaft from the upwind side to the downwind side, head where the wind blows to.
        var reach = radius * 0.5;
        var tail = OnCircle(center, reach, from);
        var headTip = OnCircle(center, reach + (size * 0.04), from + 180);
        var headLength = size * 0.19;
        var headBase = OnCircle(center, reach + (size * 0.04) - headLength, from + 180);
        var shaftPen = new Pen(ArrowBrush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Flat };
        dc.DrawLine(shaftPen, tail, headBase);

        var (sin, cos) = Math.SinCos(from * Math.PI / 180);
        var half = headLength * 0.62;
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(headTip, true, true);
            g.LineTo(new Point(headBase.X + (cos * half), headBase.Y + (sin * half)), true, false);
            g.LineTo(new Point(headBase.X - (cos * half), headBase.Y - (sin * half)), true, false);
        }

        dc.DrawGeometry(ArrowBrush, null, head);
        dc.DrawEllipse(ArrowBrush, null, tail, width * 0.95, width * 0.95);
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

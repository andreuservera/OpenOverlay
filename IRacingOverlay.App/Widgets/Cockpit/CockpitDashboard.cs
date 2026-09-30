using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets.Cockpit;

public enum HAlign
{
    Left,
    Center,
    Right,
}

public enum VAlign
{
    Top,

    /// <summary>Centre of the capitals/digits rather than of the line box, so numerals sit
    /// optically centred in whatever they are placed in.</summary>
    Center,
    Baseline,
}

/// <summary>
/// One cockpit theme: a complete dashboard with its own size, silhouette, layout and drawing
/// language, rendered straight into a DrawingContext. Every theme reads the same
/// <see cref="CockpitState"/> — speed, gear, RPM, 14 shift lights (with the flash), ABS and the two
/// proximity bands — and nothing else; what differs is purely how it is shown.
///
/// Drawing rather than XAML is deliberate: themes differ in geometry (dials, hexagons, skewed
/// blocks, rings), which a DrawingContext expresses directly, and a single visual per theme redraws
/// cheaply at the cockpit's high refresh rate.
/// </summary>
public abstract class CockpitDashboard : FrameworkElement
{
    public const int LampCount = CockpitState.ShiftLightCount;

    private const int ShiftFlashHalfPeriodMs = 90;
    private const int AbsFlashHalfPeriodMs = 150;

    private static readonly ConcurrentDictionary<string, Brush> BrushCache = new();
    private static readonly ConcurrentDictionary<(string, double), Pen> PenCache = new();

    // Keeps the flashes on a wall-clock cadence independent of the telemetry refresh rate; only
    // runs while something is actually flashing.
    private readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    protected CockpitDashboard()
    {
        SnapsToDevicePixels = true;
        _flashTimer.Tick += (_, _) => InvalidateVisual();
        Unloaded += (_, _) => _flashTimer.Stop();
        Loaded += (_, _) => _flashTimer.IsEnabled = IsFlashing;
    }

    protected CockpitState State { get; private set; } = CockpitState.Empty;

    /// <summary>The theme's footprint at scale level M. Fixed per theme, so every theme has its own
    /// silhouette and the widget never changes size while driving.</summary>
    protected abstract Size DesignSize { get; }

    public void Update(CockpitState state)
    {
        State = state;
        _flashTimer.IsEnabled = IsFlashing && IsLoaded;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => DesignSize;

    protected override void OnRender(DrawingContext dc) => Draw(dc);

    protected abstract void Draw(DrawingContext dc);

    private bool IsFlashing => ShiftFlashing || State.AbsActive;

    /// <summary>Whether this theme flashes its shift lights at the shift point. Period-style
    /// instruments turn the override off and simply stay lit.</summary>
    protected virtual bool FlashesAtShiftPoint => true;

    private bool ShiftFlashing => State.ShiftBlink && FlashesAtShiftPoint;

    // ===== Data, formatted the same way in every theme =====

    protected string Speed => State.SpeedKph > 0
        ? Units.Speed(State.SpeedKph, State.UnitSystem).ToString("0", CultureInfo.InvariantCulture)
        : "—";

    /// <summary>"km/h" or "mph"; themes upper-case it where their labels are capitals.</summary>
    protected string SpeedUnit => Units.SpeedUnit(State.UnitSystem);

    protected string Rpm => State.Rpm > 0 ? State.Rpm.ToString("0", CultureInfo.InvariantCulture) : "—";

    protected string Gear => State.Gear;

    /// <summary>Fuel in the tank, to one decimal, in litres or gallons.</summary>
    protected string Fuel => State.FuelLiters is { } liters
        ? Units.Volume(liters, State.UnitSystem).ToString("0.0", CultureInfo.InvariantCulture)
        : "—";

    protected string FuelUnit => Units.VolumeUnit(State.UnitSystem);

    /// <summary>A temperature to whole degrees in °C or °F, or a dash when unknown.</summary>
    protected string Temperature(double? celsius) => celsius is { } c
        ? Units.Temperature(c, State.UnitSystem).ToString("0", CultureInfo.InvariantCulture)
        : "—";

    protected string TemperatureUnit => Units.TemperatureUnit(State.UnitSystem);

    protected int LampsLit => Math.Clamp(State.ShiftLightsLit, 0, LampCount);

    /// <summary>False during the "off" half of the shift-point flash; always true otherwise.</summary>
    protected bool ShiftLampsOn => !ShiftFlashing || Environment.TickCount64 / ShiftFlashHalfPeriodMs % 2 == 0;

    protected bool AtShiftPoint => State.ShiftBlink;

    protected bool AbsActive => State.AbsActive;

    /// <summary>"ABS 3" — the configured level, or plain "ABS" when the car doesn't report one.
    /// Never "OFF": intervention is shown by colour/flash, not by this text.</summary>
    protected string AbsLabel => State.AbsLevel is { } level ? $"ABS {level}" : "ABS";

    /// <summary>The configured level alone, for themes that label the value separately.</summary>
    protected string AbsLevel => State.AbsLevel is { } level ? level.ToString(CultureInfo.InvariantCulture) : "—";

    /// <summary>Bright half of the ABS flash while active.</summary>
    protected bool AbsFlashOn => State.AbsActive && Environment.TickCount64 / AbsFlashHalfPeriodMs % 2 == 0;

    protected bool IsLampLit(int index) => ShiftLampsOn && index < LampsLit;

    /// <summary>Stage of a shift lamp: 0 for the first five, 1 for the next five, 2 for the last four
    /// — the same banding every theme colours, in its own palette.</summary>
    protected static int LampStage(int index) => index < 5 ? 0 : index < 10 ? 1 : 2;

    protected static bool HasCar(ProximitySide side) => side.BandEnd > side.BandStart;

    /// <summary>Human-readable overlap band, for themes that show it as text.</summary>
    protected static string BandText(ProximitySide side) => HasCar(side)
        ? $"{Math.Round(side.BandStart * 100):0}–{Math.Round(side.BandEnd * 100):0}%"
        : "CLEAR";

    // ===== Drawing helpers =====

    protected static Brush B(string hex) => BrushCache.GetOrAdd(hex, static value =>
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    });

    protected static Pen P(string hex, double thickness) => PenCache.GetOrAdd((hex, thickness), static key =>
    {
        var pen = new Pen(B(key.Item1), key.Item2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    });

    protected static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    protected static LinearGradientBrush Vertical(string top, string bottom) => (LinearGradientBrush)Frozen(
        new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 90));

    protected static LinearGradientBrush Horizontal(string left, string right) => (LinearGradientBrush)Frozen(
        new LinearGradientBrush((Color)ColorConverter.ConvertFromString(left), (Color)ColorConverter.ConvertFromString(right), 0));

    protected static Typeface Face(string family, FontWeight weight, FontStyle? style = null) =>
        new(new FontFamily(family), style ?? FontStyles.Normal, weight, FontStretches.Normal);

    protected FormattedText Measure(string text, Typeface face, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Draws text anchored at (x, y) by the given alignment and returns its bounds.</summary>
    protected Rect Text(DrawingContext dc, string text, Typeface face, double size, Brush brush,
        double x, double y, HAlign h = HAlign.Left, VAlign v = VAlign.Top)
    {
        var formatted = Measure(text, face, size, brush);
        var origin = Anchor(formatted, size, x, y, h, v);
        dc.DrawText(formatted, origin);
        return new Rect(origin, new Size(formatted.WidthIncludingTrailingWhitespace, formatted.Height));
    }

    private static Point Anchor(FormattedText formatted, double size, double x, double y, HAlign h, VAlign v)
    {
        var left = h switch
        {
            HAlign.Center => x - (formatted.WidthIncludingTrailingWhitespace / 2),
            HAlign.Right => x - formatted.WidthIncludingTrailingWhitespace,
            _ => x,
        };
        var top = v switch
        {
            // Cap height ≈ 0.7em across the faces used here.
            VAlign.Center => y - formatted.Baseline + (size * 0.35),
            VAlign.Baseline => y - formatted.Baseline,
            _ => y,
        };
        return new Point(left, top);
    }

    protected static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: true, isClosed: true);
            context.PolyLineTo(points[1..], isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Arc band between two radii, angles in degrees clockwise from 12 o'clock.</summary>
    protected static Geometry ArcBand(Point center, double innerRadius, double outerRadius, double fromDeg, double toDeg)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var large = Math.Abs(toDeg - fromDeg) > 180;
            context.BeginFigure(OnCircle(center, outerRadius, fromDeg), isFilled: true, isClosed: true);
            context.ArcTo(OnCircle(center, outerRadius, toDeg), new Size(outerRadius, outerRadius), 0, large, SweepDirection.Clockwise, true, false);
            context.LineTo(OnCircle(center, innerRadius, toDeg), true, false);
            context.ArcTo(OnCircle(center, innerRadius, fromDeg), new Size(innerRadius, innerRadius), 0, large, SweepDirection.Counterclockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    protected static Point OnCircle(Point center, double radius, double degreesFromTop)
    {
        var radians = (degreesFromTop - 90) * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }

    /// <summary>A vertical proximity meter: track, plus the overlapping band lit from front (top) to
    /// rear (bottom) — the one shape every theme needs, drawn in each theme's colours.</summary>
    protected static void VerticalBand(DrawingContext dc, ProximitySide side, Rect track, Brush trackBrush, Brush litBrush, double radius)
    {
        dc.DrawRoundedRectangle(trackBrush, null, track, radius, radius);
        if (HasCar(side))
        {
            var lit = new Rect(track.X, track.Y + (track.Height * side.BandStart), track.Width,
                Math.Max(radius * 2, track.Height * (side.BandEnd - side.BandStart)));
            dc.DrawRoundedRectangle(litBrush, null, lit, radius, radius);
        }
    }
}

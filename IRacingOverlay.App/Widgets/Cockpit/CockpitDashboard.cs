using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// The cockpit, styled after a GT3 car's display: a black screen with chamfered corners holding a
/// row of module panels (<see cref="CockpitLayout"/>), the shift lights as a strip of slanted LEDs
/// across the top and a proximity bar down each side — drawn straight into a DrawingContext so the
/// whole thing redraws cheaply at the cockpit's high refresh rate.
///
/// Each panel has its top-right corner cut and a stripe down its left edge: grey, or for the in-car
/// adjusters the colour of their rotary on a GT3 wheel (ABS yellow, TC blue, brake bias orange).
/// Values are heavy condensed italic in tabular figures, so a number never shifts sideways as its
/// digits change. Otherwise colour marks a state: the shift point, ABS intervening, low fuel, a hot
/// temperature, incidents near the limit, gaining or losing time.
///
/// The screen and panels follow the widget's opacity setting (<see cref="Overlay.BackgroundOpacity"/>,
/// read off the faded panel brush), so the background fades like any other widget's while the
/// values stay fully readable.
/// </summary>
public sealed class CockpitDashboard : FrameworkElement
{
    public static readonly DependencyProperty PanelBackgroundProperty = DependencyProperty.Register(
        nameof(PanelBackground), typeof(Brush), typeof(CockpitDashboard),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueFontProperty = DependencyProperty.Register(
        nameof(ValueFont), typeof(FontFamily), typeof(CockpitDashboard),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((CockpitDashboard)d)._valueFace = null));

    public static readonly DependencyProperty LabelFontProperty = DependencyProperty.Register(
        nameof(LabelFont), typeof(FontFamily), typeof(CockpitDashboard),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((CockpitDashboard)d)._labelFace = null));

    private const int ShiftFlashHalfPeriodMs = 90;
    private const double Padding = 7;
    private const double LabelSize = 9.5;
    private const double SmallValueSize = 19;

    // Space the label takes at the top of a card.
    private const double LabelBand = 12;

    private const string ValueColor = "#F2F5F8";
    private const string LabelColor = "#8E99A5";
    private const string TrackColor = "#232931";
    private const string NeutralGauge = "#C4CCD4";
    private const string RadarColor = "#F5A524";
    private const string ScreenFill = "#F5050607";
    private const string ScreenEdge = "#5A626C";
    private const string PanelFill = "#FF14171B";
    private const string PanelEdge = "#3A4149";
    private const string StripeColor = "#5A646D";
    private const double ScreenCut = 9;
    private const double PanelCut = 8;
    private const double StripeWidth = 3;
    private const double ItalicSkewDegrees = -12;

    // 5 green, 5 yellow, 4 red; every lamp red at the shift point.
    private static readonly string[] LampColors = ["#34D399", "#FFD24D", "#F04438"];
    private const string ShiftPointColor = "#F04438";

    private static readonly ConcurrentDictionary<string, Brush> BrushCache = new();
    private static readonly ConcurrentDictionary<string, Pen> PenCache = new();

    // Keeps the shift-point flash on a wall-clock cadence independent of the telemetry refresh
    // rate; only runs while the lights are flashing.
    private readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    private readonly Dictionary<(string Text, double X, double Y), Drawing> _labels = new();

    private CockpitState _state = CockpitState.Empty;
    private CockpitLayout _layout = CockpitLayout.Empty;
    private IReadOnlyList<CockpitModule> _modules = [];
    private bool _shiftLights;
    private bool _radar;
    private Typeface? _valueFace;
    private Typeface? _labelFace;

    public CockpitDashboard()
    {
        SnapsToDevicePixels = true;
        SetResourceReference(PanelBackgroundProperty, "Theme.PanelBackground");
        SetResourceReference(ValueFontProperty, "Theme.GearFontFamily");
        SetResourceReference(LabelFontProperty, "Theme.LabelFontFamily");
        _flashTimer.Tick += (_, _) => InvalidateVisual();
        Unloaded += (_, _) => _flashTimer.Stop();
        Loaded += (_, _) => _flashTimer.IsEnabled = _state.ShiftBlink;
    }

    /// <summary>The app's panel brush. Not drawn: its opacity is how far the widget's opacity
    /// setting has faded the background, which the screen and panels follow.</summary>
    public Brush PanelBackground
    {
        get => (Brush)GetValue(PanelBackgroundProperty);
        set => SetValue(PanelBackgroundProperty, value);
    }

    public FontFamily ValueFont
    {
        get => (FontFamily)GetValue(ValueFontProperty);
        set => SetValue(ValueFontProperty, value);
    }

    public FontFamily LabelFont
    {
        get => (FontFamily)GetValue(LabelFontProperty);
        set => SetValue(LabelFontProperty, value);
    }

    public void Update(CockpitState state)
    {
        var recompose = !state.Unsupported.SetEquals(_state.Unsupported);
        _state = state;
        if (recompose)
        {
            Compose();
        }

        _flashTimer.IsEnabled = state.ShiftBlink && IsLoaded;
        InvalidateVisual();
    }

    /// <summary>Which modules to show, in order, and whether the lights and the radar are on. The
    /// dashboard takes the composed size, so the window follows.</summary>
    public void SetLayout(IReadOnlyList<CockpitModule> modules, bool shiftLights, bool radar)
    {
        _modules = modules;
        _shiftLights = shiftLights;
        _radar = radar;
        Compose();
    }

    /// <summary>Lays out the chosen modules the car can feed.</summary>
    private void Compose()
    {
        var modules = _state.Unsupported.Count == 0 ? _modules : _modules.Where(module => !_state.Unsupported.Contains(module)).ToList();
        _layout = CockpitLayout.Compose(modules, _shiftLights, _radar);
        _labels.Clear();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => _layout.Size;

    // Cached labels were laid out for the old pixels-per-dip.
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _labels.Clear();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_layout.Size.Width <= 0)
        {
            return;
        }

        dc.DrawGeometry(Faded(ScreenFill), FadedPen(ScreenEdge), Chamfer(Inset(new Rect(_layout.Size)), ScreenCut, 0, ScreenCut, 0));

        if (_layout.LeftRadar is { } left && _layout.RightRadar is { } right)
        {
            Radar(dc, _state.LeftProximity, left);
            Radar(dc, _state.RightProximity, right);
        }

        if (_layout.ShiftLights is { } strip)
        {
            ShiftLights(dc, strip);
        }

        foreach (var cell in _layout.Cells)
        {
            Module(dc, cell);
        }
    }

    /// <summary>How far the opacity setting has faded the background, 0 to 1.</summary>
    private double Fade => PanelBackground.Opacity;

    private Typeface ValueFace => _valueFace ??= new Typeface(ValueFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // The label family is already a semibold, semi-condensed cut.
    private Typeface LabelFace => _labelFace ??= new Typeface(LabelFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    // ===== Fixed elements =====

    private void Radar(DrawingContext dc, ProximitySide side, Rect track)
    {
        dc.DrawRectangle(Faded(PanelFill), FadedPen(PanelEdge), Inset(track));
        if (side.BandEnd > side.BandStart)
        {
            var lit = new Rect(track.X, track.Y + (track.Height * side.BandStart), track.Width,
                Math.Max(track.Width, track.Height * (side.BandEnd - side.BandStart)));
            dc.DrawRectangle(B(RadarColor), null, lit);
        }
    }

    /// <summary>Slanted LEDs, grouped by stage; an unlit one is a faint tint of its stage.</summary>
    private void ShiftLights(DrawingContext dc, Rect strip)
    {
        const int count = CockpitState.ShiftLightCount;
        const double gap = 3;
        const double stageGap = 3;
        var lean = strip.Height * 0.45;
        var lit = ShiftLampsOn ? Math.Clamp(_state.ShiftLightsLit, 0, count) : 0;
        var width = (strip.Width - lean - ((count - 3) * gap) - (2 * stageGap)) / count;
        var x = strip.X;
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                x += Stage(i) != Stage(i - 1) ? stageGap : gap;
            }

            var color = _state.ShiftBlink ? ShiftPointColor : LampColors[Stage(i)];
            var brush = i < lit ? B(color) : B(Tint(color, 0x2E));
            dc.DrawGeometry(brush, null, Slanted(new Rect(x, strip.Y, width + lean, strip.Height), lean));
            x += width;
        }
    }

    private static int Stage(int index) => index < 5 ? 0 : index < 10 ? 1 : 2;

    /// <summary>False during the "off" half of the shift-point flash; always true otherwise.</summary>
    private bool ShiftLampsOn => !_state.ShiftBlink || Environment.TickCount64 / ShiftFlashHalfPeriodMs % 2 == 0;

    // ===== Modules =====

    /// <summary>The colour of an in-car adjuster's rotary, as GT3 wheels mark them.</summary>
    private static string? AdjusterColor(CockpitModule module) => module switch
    {
        CockpitModule.Abs => "#FFD24D",
        CockpitModule.TractionControl => "#4C9AFF",
        CockpitModule.BrakeBias => "#FF8A3D",
        _ => null,
    };

    private void Module(DrawingContext dc, CockpitCell cell)
    {
        var spec = CockpitModules.Of(cell.Module);
        var reading = spec.Read(_state);
        var r = cell.Bounds;
        var alarm = reading.Tone is CockpitTone.Warning or CockpitTone.Critical;
        var tone = ToneColor(reading.Tone);
        var adjuster = AdjusterColor(cell.Module);

        // An alarm washes the panel and takes over its stripe and label.
        var shape = Chamfer(r, 0, PanelCut, 0, 0);
        dc.DrawGeometry(Faded(PanelFill), null, shape);
        if (alarm)
        {
            dc.DrawGeometry(B(Tint(tone, 0x40)), null, shape);
        }

        dc.DrawRectangle(B(alarm ? tone : adjuster ?? StripeColor), null, new Rect(r.X, r.Y, StripeWidth, r.Height));
        Label(dc, spec.Label(_state.UnitSystem), r.X + Padding + 1, r.Y + 3, alarm ? tone : adjuster ?? LabelColor);
        var valueColor = reading.Tone == CockpitTone.Normal ? ValueColor : tone;

        if (reading.Bars is { } bars)
        {
            Bars(dc, bars, new Rect(r.X + Padding, r.Y + LabelBand + 4, r.Width - (2 * Padding), r.Height - LabelBand - Padding - 2), cell.FullHeight);
            return;
        }

        var maxWidth = r.Width - (2 * Padding);
        if (!cell.FullHeight)
        {
            Figures(dc, reading.Value, SmallValueSize, B(valueColor), r.Right - Padding, r.Y + 21, centered: false, maxWidth);
            return;
        }

        var middle = r.Y + LabelBand + ((r.Height - LabelBand) / 2);
        var x = spec.Centered ? r.X + (r.Width / 2) : r.Right - Padding;
        Figures(dc, reading.Value, spec.FullSize, B(valueColor), x, middle, spec.Centered, maxWidth);

        if (reading.Gauge is { } gauge)
        {
            // Segmented, like a dash's fuel bar.
            const int segments = 10;
            var track = new Rect(r.X + Padding, r.Bottom - Padding - 4, r.Width - (2 * Padding), 4);
            var w = (track.Width - ((segments - 1) * 2)) / segments;
            var on = (int)Math.Ceiling(Math.Clamp(gauge, 0, 1) * segments);
            for (var i = 0; i < segments; i++)
            {
                var fill = i < on ? (alarm ? tone : NeutralGauge) : TrackColor;
                dc.DrawRectangle(B(fill), null, new Rect(track.X + (i * (w + 2)), track.Y, w, track.Height));
            }
        }
    }

    /// <summary>Vertical bars side by side when the module has a full column, horizontal bars
    /// stacked when it shares one.</summary>
    private static void Bars(DrawingContext dc, IReadOnlyList<CockpitBar> bars, Rect area, bool vertical)
    {
        const double gap = 3;
        var span = ((vertical ? area.Width : area.Height) - ((bars.Count - 1) * gap)) / bars.Count;
        for (var i = 0; i < bars.Count; i++)
        {
            var offset = i * (span + gap);
            var track = vertical
                ? new Rect(area.X + offset, area.Y, span, area.Height)
                : new Rect(area.X, area.Y + offset, area.Width, span);
            dc.DrawRectangle(B(TrackColor), null, track);
            var value = Math.Clamp(bars[i].Value, 0, 1);
            if (value <= 0)
            {
                continue;
            }

            var fill = vertical
                ? new Rect(track.X, track.Bottom - (track.Height * value), track.Width, track.Height * value)
                : new Rect(track.X, track.Y, track.Width * value, track.Height);
            dc.DrawRectangle(B(ToneColor(bars[i].Tone)), null, fill);
        }
    }

    /// <summary>A rectangle with its corners cut at 45°: top-left, top-right, bottom-right, bottom-left.</summary>
    private static Geometry Chamfer(Rect r, double topLeft, double topRight, double bottomRight, double bottomLeft) => Polygon(
        new Point(r.Left + topLeft, r.Top), new Point(r.Right - topRight, r.Top), new Point(r.Right, r.Top + topRight),
        new Point(r.Right, r.Bottom - bottomRight), new Point(r.Right - bottomRight, r.Bottom), new Point(r.Left + bottomLeft, r.Bottom),
        new Point(r.Left, r.Bottom - bottomLeft), new Point(r.Left, r.Top + topLeft));

    /// <summary>A parallelogram leaning right by <paramref name="lean"/> over its height.</summary>
    private static Geometry Slanted(Rect r, double lean) => Polygon(
        new Point(r.Left + lean, r.Top), new Point(r.Right, r.Top), new Point(r.Right - lean, r.Bottom), new Point(r.Left, r.Bottom));

    private static Geometry Polygon(params Point[] points)
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

    private static string ToneColor(CockpitTone tone) => tone switch
    {
        CockpitTone.Positive => "#34D399",
        CockpitTone.Negative => "#FF6B6B",
        CockpitTone.Warning => "#F5A524",
        CockpitTone.Critical => "#F04438",
        _ => ValueColor,
    };

    // ===== Text =====

    /// <summary>Labels never change between frames at the same place: laid out once, then redrawn
    /// from a frozen drawing.</summary>
    private void Label(DrawingContext dc, string text, double x, double y, string color = LabelColor)
    {
        var key = ($"{text}|{color}", x, y);
        if (!_labels.TryGetValue(key, out var drawing))
        {
            var group = new DrawingGroup();
            using (var context = group.Open())
            {
                context.DrawText(Formatted(text, LabelFace, LabelSize, B(color)), new Point(x, y));
            }

            group.Freeze();
            drawing = group;
            _labels[key] = drawing;
        }

        dc.DrawDrawing(drawing);
    }

    /// <summary>
    /// A value in tabular figures: every digit advances by the widest digit's width, so "1" and "8"
    /// take the same room and a number never shifts as it changes. Anchored by the right edge (or
    /// the centre), centred vertically on the capitals, and shrunk to fit if it would overflow.
    /// Leaned into an italic around its own centre line, so it stays where it was anchored. Drawn as
    /// one glyph run; a face that can't give one falls back to a slot per character.
    /// </summary>
    private void Figures(DrawingContext dc, string text, double size, Brush brush, double x, double capCenterY, bool centered, double maxWidth)
    {
        if (text.Length == 0)
        {
            return;
        }

        dc.PushTransform(new SkewTransform(ItalicSkewDegrees, 0, x, capCenterY));
        UprightFigures(dc, text, size, brush, x - (size * 0.08), capCenterY, centered, maxWidth);
        dc.Pop();
    }

    private void UprightFigures(DrawingContext dc, string text, double size, Brush brush, double x, double capCenterY, bool centered, double maxWidth)
    {
        if (!ValueFace.TryGetGlyphTypeface(out var glyphs) || !text.All(c => glyphs.CharacterToGlyphMap.ContainsKey(c)))
        {
            FiguresBySlot(dc, text, size, brush, x, capCenterY, centered, maxWidth);
            return;
        }

        var indices = new ushort[text.Length];
        var advances = new double[text.Length];
        var offsets = new Point[text.Length];
        var slot = Enumerable.Range('0', 10).Max(digit => glyphs.AdvanceWidths[glyphs.CharacterToGlyphMap[digit]]);
        for (var i = 0; i < text.Length; i++)
        {
            indices[i] = glyphs.CharacterToGlyphMap[text[i]];
            var own = glyphs.AdvanceWidths[indices[i]];
            advances[i] = char.IsAsciiDigit(text[i]) ? slot : own;
            offsets[i] = new Point((advances[i] - own) / 2, 0);
        }

        size = Math.Min(size, maxWidth / advances.Sum());
        for (var i = 0; i < text.Length; i++)
        {
            advances[i] *= size;
            offsets[i].X *= size;
        }

        var total = advances.Sum();
        var left = centered ? x - (total / 2) : x - total;
        var baseline = capCenterY + (glyphs.CapsHeight * size / 2);
        var run = new GlyphRun(glyphs, 0, false, size, (float)PixelsPerDip, indices, new Point(left, baseline),
            advances, offsets, null, null, null, null, null);
        dc.DrawGlyphRun(brush, run);
    }

    private void FiguresBySlot(DrawingContext dc, string text, double size, Brush brush, double x, double capCenterY, bool centered, double maxWidth)
    {
        var characters = text.Select(c => Formatted(c.ToString(), ValueFace, size, brush)).ToArray();
        var slot = Enumerable.Range('0', 10).Max(digit => Formatted(((char)digit).ToString(), ValueFace, size, brush).WidthIncludingTrailingWhitespace);
        var widths = text.Select((c, i) => char.IsAsciiDigit(c) ? slot : characters[i].WidthIncludingTrailingWhitespace).ToArray();
        var scale = Math.Min(1, maxWidth / widths.Sum());
        var total = widths.Sum() * scale;
        var left = centered ? x - (total / 2) : x - total;
        if (scale < 1)
        {
            dc.PushTransform(new ScaleTransform(scale, scale, left, capCenterY));
        }

        var position = left;
        for (var i = 0; i < characters.Length; i++)
        {
            var character = characters[i];
            dc.DrawText(character, new Point(position + ((widths[i] - character.WidthIncludingTrailingWhitespace) / 2), capCenterY - character.Baseline + (size * 0.35)));
            position += widths[i];
        }

        if (scale < 1)
        {
            dc.Pop();
        }
    }

    private FormattedText Formatted(string text, Typeface face, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, PixelsPerDip);

    // ===== Brushes =====

    /// <summary>Half a pixel in, so a 1px outline lands on whole pixels.</summary>
    private static Rect Inset(Rect rect) => new(rect.X + 0.5, rect.Y + 0.5, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));

    private static string Tint(string hex, int alpha) => $"#{alpha:X2}{hex[^6..]}";

    private static Brush B(string hex) => BrushCache.GetOrAdd(hex, static value =>
        Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(value))));

    private static Pen Pen(string hex) => PenCache.GetOrAdd(hex, static value => Frozen(new Pen(B(value), 1)));

    private Brush Faded(string hex) => B(FadedHex(hex));

    private Pen FadedPen(string hex) => Pen(FadedHex(hex));

    /// <summary>The colour with its alpha scaled by the panel's fade, in 1% steps so the brush cache
    /// stays small.</summary>
    private string FadedHex(string hex) =>
        Tint(hex, (int)Math.Round(Convert.ToInt32(hex[1..3], 16) * Math.Round(Math.Clamp(Fade, 0, 1), 2)));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

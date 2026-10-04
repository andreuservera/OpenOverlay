using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// A car's make: its logo in one ink, fitted to the box and centred, or the brand's monogram when
/// the app has no logo for it. Drawn rather than templated so a table full of rows costs one shared,
/// frozen geometry per make.
/// </summary>
public sealed class CarBrandIcon : FrameworkElement
{
    private static readonly ConcurrentDictionary<string, LogoShape?> LogoCache = new();
    private static readonly Brush FallbackInk = Frozen(Color.FromRgb(0xC4, 0xCC, 0xD4));
    private static readonly Regex PathData = new(@"\sd=""([^""]+)""", RegexOptions.CultureInvariant);
    private static readonly Regex ScaleData = new(@"\sdata-scale=""([0-9.]+)""", RegexOptions.CultureInvariant);

    /// <summary>A logo's shape, and how much of the box it fills (an SVG's optional
    /// <c>data-scale</c>), for the odd logo that reads too big filling it.</summary>
    internal sealed record LogoShape(Geometry Geometry, double Scale);

    public static readonly DependencyProperty LogoProperty = DependencyProperty.Register(
        nameof(Logo), typeof(string), typeof(CarBrandIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MonogramProperty = DependencyProperty.Register(
        nameof(Monogram), typeof(string), typeof(CarBrandIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The logo's file name in Assets/CarBrands, without the extension.</summary>
    public string Logo
    {
        get => (string)GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    /// <summary>Shown when there is no logo, or it fails to load.</summary>
    public string Monogram
    {
        get => (string)GetValue(MonogramProperty);
        set => SetValue(MonogramProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var ink = TryFindResource("Text.Secondary") as Brush ?? FallbackInk;
        if (LogoOf(Logo) is { } logo)
        {
            dc.PushTransform(new MatrixTransform(Fit(logo.Geometry.Bounds, new Size(ActualWidth * logo.Scale, ActualHeight * logo.Scale), new Point(ActualWidth / 2, ActualHeight / 2))));
            dc.DrawGeometry(ink, null, logo.Geometry);
            dc.Pop();
            return;
        }

        if (string.IsNullOrEmpty(Monogram))
        {
            return;
        }

        var family = TryFindResource("Theme.NumericFontFamily") as FontFamily ?? new FontFamily("Bahnschrift");
        var text = new FormattedText(
            Monogram, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Condensed),
            Math.Min(11, ActualHeight * 0.62), ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
    }

    /// <summary>Scales <paramref name="ink"/> to fill <paramref name="box"/> without distorting it,
    /// and centres it on <paramref name="center"/>.</summary>
    internal static Matrix Fit(Rect ink, Size box, Point center)
    {
        var scale = Math.Min(box.Width / ink.Width, box.Height / ink.Height);
        var matrix = Matrix.Identity;
        matrix.Translate(-(ink.X + (ink.Width / 2)), -(ink.Y + (ink.Height / 2)));
        matrix.Scale(scale, scale);
        matrix.Translate(center.X, center.Y);
        return matrix;
    }

    /// <summary>The logo's shape, read once from its embedded SVG; null for no logo or one that
    /// can't be read.</summary>
    internal static LogoShape? LogoOf(string? key) =>
        string.IsNullOrEmpty(key) ? null : LogoCache.GetOrAdd(key, static name =>
        {
            try
            {
                using var stream = typeof(CarBrandIcon).Assembly.GetManifestResourceStream($"OpenOverlay.CarBrands.{name}.svg");
                if (stream is null)
                {
                    return null;
                }

                var svg = new StreamReader(stream).ReadToEnd();
                if (PathData.Match(svg) is not { Success: true } data)
                {
                    return null;
                }

                // SVG fills nonzero unless the file says even-odd; WPF's path markup defaults to
                // even-odd, so the rule is always spelled out.
                var fillRule = svg.Contains("fill-rule=\"evenodd\"", StringComparison.Ordinal) ? "F0 " : "F1 ";
                var geometry = Geometry.Parse(fillRule + data.Groups[1].Value);
                if (geometry.Bounds.IsEmpty)
                {
                    return null;
                }

                geometry.Freeze();
                var scale = ScaleData.Match(svg) is { Success: true } s && double.TryParse(s.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? Math.Clamp(value, 0.1, 1)
                    : 1;
                return new LogoShape(geometry, scale);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or IOException)
            {
                AppLog.Warn("Widgets", $"Car brand logo '{name}' could not be read", ex);
                return null;
            }
        });

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

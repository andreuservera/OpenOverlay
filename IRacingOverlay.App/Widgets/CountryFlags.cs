using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// The country flags in Assets/Flags, found by the name iRacing gives a member's flair. Each flag is
/// read from its SVG once, off the UI thread, and kept as a bitmap per size, so a table full of rows
/// draws small images rather than every path of every coat of arms.
/// </summary>
internal static class CountryFlags
{
    private const string Prefix = "OpenOverlay.Flags.Flag_";

    // Flair names that don't spell the file's name: iRacing's spelling, or a common alternative, to
    // the file's, both normalised (see Normalize).
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["turkiye"] = "turkey",
        ["czechia"] = "czechrepublic",
        ["northmacedonia"] = "macedonia",
        ["eswatini"] = "swaziland",
        ["timorleste"] = "easttimor",
        ["ivorycoast"] = "cotedivoire",
        ["holysee"] = "vaticancity",
        ["vatican"] = "vaticancity",
        ["russianfederation"] = "russia",
        ["capeverde"] = "caboverde",
        ["burma"] = "myanmar",
        ["greatbritain"] = "unitedkingdom",
        ["unitedstatesofamerica"] = "unitedstates",
        ["usa"] = "unitedstates",
        ["korea"] = "southkorea",
        ["republicofkorea"] = "southkorea",
        ["korearepublicof"] = "southkorea",
        ["democraticrepublicofthecongo"] = "congodemocraticrepublicofthe",
        ["republicofthecongo"] = "congorepublicofthe",
        ["congo"] = "congorepublicofthe",
        ["stateofpalestine"] = "palestine",
        ["chinesetaipei"] = "taiwan",
        ["bosniaherzegovina"] = "bosniaandherzegovina",
        ["trinidadtobago"] = "trinidadandtobago",
    };

    private static readonly Lazy<Dictionary<string, string>> Resources = new(() =>
        typeof(CountryFlags).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".svg", StringComparison.Ordinal))
            .ToDictionary(name => Normalize(name[Prefix.Length..^4]), name => name));

    private static readonly ConcurrentDictionary<string, string?> KeyCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Drawing?> DrawingCache = new(StringComparer.Ordinal);
    // Null for a flag that couldn't be drawn, so it isn't tried again.
    private static readonly ConcurrentDictionary<(string Key, int Width, int Height), ImageSource?> BitmapCache = new();
    private static readonly Dictionary<(string Key, int Width, int Height), List<WeakReference<UIElement>>> Waiters = [];
    private static readonly Lazy<BlockingCollection<(string Key, int Width, int Height)>> Worker = new(StartWorker);

    /// <summary>The flag for a flair name, as its resource name; null for no flair ("-none-") or a
    /// country there's no flag for. "Global" has a flag of its own, a globe.</summary>
    internal static string? KeyOf(string? flairName) =>
        string.IsNullOrWhiteSpace(flairName) ? null : KeyCache.GetOrAdd(flairName, static name =>
        {
            var key = Normalize(name);
            key = Aliases.GetValueOrDefault(key, key);
            return Resources.Value.GetValueOrDefault(key);
        });

    /// <summary>Lower case letters only, accents dropped: "Côte d'Ivoire" and "Cote_d'Ivoire" meet
    /// as "cotedivoire".</summary>
    internal static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>The flag as read from its SVG; null when it can't be read.</summary>
    internal static Drawing? DrawingOf(string key) => DrawingCache.GetOrAdd(key, static name =>
    {
        try
        {
            using var stream = typeof(CountryFlags).Assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                return null;
            }

            var reader = new FileSvgReader(new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true }, false);
            var drawing = reader.Read(stream);
            if (drawing is null || drawing.Bounds.IsEmpty)
            {
                return null;
            }

            drawing.Freeze();
            return drawing;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Widgets", $"Flag '{name}' could not be read", ex);
            return null;
        }
    });

    /// <summary>The flag fitted to a <paramref name="boxWidth"/> × <paramref name="boxHeight"/>
    /// device-pixel box, if it's ready. If not, it is prepared on a background thread (reading the
    /// biggest coats of arms takes a few hundred milliseconds, too long for the UI thread mid-race)
    /// and <paramref name="requester"/> is redrawn once it is.</summary>
    internal static ImageSource? TryGetBitmap(string key, int boxWidth, int boxHeight, UIElement requester)
    {
        var request = (key, boxWidth, boxHeight);
        if (BitmapCache.TryGetValue(request, out var ready))
        {
            return ready;
        }

        bool queue;
        lock (Waiters)
        {
            queue = !Waiters.TryGetValue(request, out var waiting);
            if (queue)
            {
                Waiters[request] = waiting = [];
            }

            waiting!.Add(new WeakReference<UIElement>(requester));
        }

        if (queue)
        {
            Worker.Value.Add(request);
        }

        return null;
    }

    /// <summary>The flag drawn as large as its proportions allow inside the box, in device pixels;
    /// null when it can't be read.</summary>
    internal static ImageSource? RenderBitmap(string key, int boxWidth, int boxHeight)
    {
        if (boxWidth <= 0 || boxHeight <= 0 || DrawingOf(key) is not { } drawing)
        {
            return null;
        }

        var bounds = drawing.Bounds;
        var fit = Fit(bounds.Size, new Size(boxWidth, boxHeight));
        var width = Math.Max(1, (int)Math.Round(fit.Width));
        var height = Math.Max(1, (int)Math.Round(fit.Height));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(width / bounds.Width, height / bounds.Height));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawDrawing(drawing);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BlockingCollection<(string Key, int Width, int Height)> StartWorker()
    {
        var queue = new BlockingCollection<(string Key, int Width, int Height)>();
        // STA: SharpVectors and RenderTargetBitmap both need it. What it makes is frozen, so the
        // UI thread can draw it.
        var thread = new Thread(() =>
        {
            foreach (var request in queue.GetConsumingEnumerable())
            {
                ImageSource? bitmap = null;
                try
                {
                    bitmap = RenderBitmap(request.Key, request.Width, request.Height);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Widgets", $"Flag '{request.Key}' could not be drawn", ex);
                }

                BitmapCache[request] = bitmap;
                List<WeakReference<UIElement>>? waiting;
                lock (Waiters)
                {
                    Waiters.Remove(request, out waiting);
                }

                foreach (var reference in waiting ?? [])
                {
                    if (bitmap is not null && reference.TryGetTarget(out var element))
                    {
                        element.Dispatcher.BeginInvoke(element.InvalidateVisual);
                    }
                }
            }
        })
        {
            IsBackground = true,
            Name = "Country flags",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return queue;
    }

    /// <summary>The largest rectangle of the flag's own proportions that fits in <paramref name="box"/>,
    /// centred there.</summary>
    internal static Rect Fit(Size flag, Size box)
    {
        var scale = Math.Min(box.Width / flag.Width, box.Height / flag.Height);
        var size = new Size(flag.Width * scale, flag.Height * scale);
        return new Rect(new Point((box.Width - size.Width) / 2, (box.Height - size.Height) / 2), size);
    }
}

/// <summary>A driver's country flag, from their iRacing flair; blank when there's none to show.</summary>
public sealed class CountryFlagIcon : FrameworkElement
{
    private static readonly Pen Outline = FrozenPen();

    public static readonly DependencyProperty CountryProperty = DependencyProperty.Register(
        nameof(Country), typeof(string), typeof(CountryFlagIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The flair name as iRacing sends it ("Spain", "United Kingdom").</summary>
    public string Country
    {
        get => (string)GetValue(CountryProperty);
        set => SetValue(CountryProperty, value);
    }

    // The panels are scaled by a LayoutTransform (the widget's S/M/L… size) that the screen's DPI
    // doesn't include. Each flag is therefore drawn for the largest size and scaled down, smoothly,
    // to whatever size the widget is at: sharp at every size, and through a size change, without
    // redrawing it.
    private static readonly double Oversample = ScaleLevels.FactorOf(ScaleLevel.XXXL);

    public CountryFlagIcon()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || CountryFlags.KeyOf(Country) is not { } key)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX * Oversample;
        var scaleY = dpi.DpiScaleY * Oversample;
        var bitmap = CountryFlags.TryGetBitmap(key, (int)Math.Round(ActualWidth * scaleX), (int)Math.Round(ActualHeight * scaleY), this);
        if (bitmap is not BitmapSource pixels)
        {
            return;
        }

        var size = new Size(pixels.PixelWidth / scaleX, pixels.PixelHeight / scaleY);
        var rect = new Rect(new Point((ActualWidth - size.Width) / 2, (ActualHeight - size.Height) / 2), size);
        dc.DrawImage(pixels, rect);
        // A hairline edge, so a mostly white or dark flag still reads as a flag on the row.
        dc.DrawRectangle(null, Outline, rect);
    }

    private static Pen FrozenPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), 1);
        pen.Freeze();
        return pen;
    }
}

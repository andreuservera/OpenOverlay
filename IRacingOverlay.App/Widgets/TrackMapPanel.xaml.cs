using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TrackMapPanel : UserControl
{
    // Every car is the same disc, the player included: the aura behind them is what marks "you",
    // not a bigger badge pushing the neighbours out of sight.
    private const double MarkerDiameter = 18;
    private const double MarkerFontSize = 10.5;
    private const double NumberSlantDegrees = 12;

    // A light rim so a disc reads against the dark tunnel and against the disc it overlaps.
    private static readonly Brush MarkerRim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    // The player's disc gets a full white ring on top of the aura, so "you" holds up mid-pack.
    private static readonly Brush PlayerRim = StatePalette.TextPrimary;
    private static readonly Brush CarNumberInk = StatePalette.TextOnAccent;

    // Number outlines, keyed by car number. Built once per number and reused every tick, so the
    // 60 Hz update allocates nothing (see the pooling note in UpdateState).
    private readonly Dictionary<string, Geometry> _numberGlyphs = new();

    public TrackMapPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(IReadOnlyList<TrackMapMarker> markers)
    {
        var width = MapArea.ActualWidth;
        if (width <= 0)
        {
            // Hide all pooled badges when the panel has no layout yet.
            for (var i = 0; i < MarkerLayer.Children.Count; i++)
                MarkerLayer.Children[i].Visibility = Visibility.Collapsed;
            PlayerAura.Visibility = Visibility.Collapsed;
            return;
        }

        // Centres run from half an aura in from each end, so neither the discs nor the player's
        // aura ever hang out of the tunnel at start/finish.
        var inset = PlayerAura.Width / 2;
        var span = Math.Max(0, width - 2 * inset);
        var top = (MapArea.ActualHeight - MarkerDiameter) / 2;
        var playerSeen = false;

        // Reuse existing badge visuals — creating new elements every tick generated enough garbage
        // to trigger visible GC pauses (the reported stutter).
        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            Border badge;
            if (i < MarkerLayer.Children.Count)
            {
                badge = (Border)MarkerLayer.Children[i];
            }
            else
            {
                badge = BuildBadge();
                MarkerLayer.Children.Add(badge);
            }

            UpdateBadge(badge, marker);
            badge.Visibility = Visibility.Visible;

            var centreX = inset + Math.Clamp(marker.LapDistPct, 0, 1) * span;
            Canvas.SetLeft(badge, centreX - MarkerDiameter / 2);
            Canvas.SetTop(badge, top);
            Panel.SetZIndex(badge, marker.IsPlayer ? 10 : 1);

            if (marker.IsPlayer)
            {
                PlayerAura.Margin = new Thickness(centreX - inset, 0, 0, 0);
                playerSeen = true;
            }
        }

        // Hide surplus pooled elements instead of removing them.
        for (var i = markers.Count; i < MarkerLayer.Children.Count; i++)
            MarkerLayer.Children[i].Visibility = Visibility.Collapsed;

        PlayerAura.Visibility = playerSeen ? Visibility.Visible : Visibility.Collapsed;
    }

    // The map runs edge to edge inside the panel, so round its corners to the panel's own (minus
    // the frame's border) — the theme decides that radius, from square to well rounded.
    private void OnMapAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var radius = Math.Max(0, Frame.CornerRadius.TopLeft - Frame.BorderThickness.Left - Frame.Padding.Top);
        var clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
        clip.Freeze();
        MapArea.Clip = clip;
    }

    private void UpdateBadge(Border badge, TrackMapMarker marker)
    {
        var classColor = ColorConverter.ConvertFromString(marker.ClassColor) is Color color ? color : Colors.White;
        if (badge.Background is not SolidColorBrush fill || fill.Color != classColor)
        {
            badge.Background = new SolidColorBrush(classColor);
        }

        badge.BorderBrush = marker.IsPlayer ? PlayerRim : MarkerRim;
        badge.BorderThickness = new Thickness(marker.IsPlayer ? 1.5 : 1);

        var number = (Path)badge.Child;
        if (number.Tag as string != marker.CarNumber)
        {
            number.Tag = marker.CarNumber;
            number.Data = NumberGlyph(marker.CarNumber);
        }
    }

    private static Border BuildBadge() => new()
    {
        Width = MarkerDiameter,
        Height = MarkerDiameter,
        CornerRadius = new CornerRadius(MarkerDiameter / 2),
        BorderThickness = new Thickness(1),
        BorderBrush = MarkerRim,
        Child = new Path
        {
            Fill = CarNumberInk,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    // The number as an outline shifted so its ink starts at 0,0. Centring that box puts the
    // digits dead in the middle of the disc; centring a TextBlock would centre the font's line box
    // instead, whose ascender/descender space leaves the digits visibly high or low.
    private Geometry NumberGlyph(string carNumber)
    {
        if (_numberGlyphs.TryGetValue(carNumber, out var cached))
        {
            return cached;
        }

        var family = TryFindResource("Theme.NumericFontFamily") as FontFamily ?? new FontFamily("Bahnschrift");
        var text = new FormattedText(
            carNumber,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Condensed),
            MarkerFontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Slanted by hand: the condensed numeric faces have no italic cut, and WPF draws them
        // upright even when asked for one.
        var slanted = Geometry.Combine(text.BuildGeometry(new Point(0, 0)), Geometry.Empty, GeometryCombineMode.Union,
            new SkewTransform(-NumberSlantDegrees, 0));
        var bounds = slanted.Bounds;
        if (!bounds.IsEmpty)
        {
            slanted.Transform = new TranslateTransform(-bounds.X, -bounds.Y);
        }

        slanted.Freeze();
        _numberGlyphs[carNumber] = slanted;
        return slanted;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

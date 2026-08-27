using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TrackMapPanel : UserControl
{
    private const double MarkerWidth = 22;
    private const double MarkerHeight = 15;
    private const double PlayerMarkerWidth = 34;
    private const double PlayerMarkerHeight = 22;

    // Regular cars are semi-transparent so overlapping badges blend rather than fully hide each
    // other — the whole point of a single-line bar where cars are left to overlap instead of being
    // spread across lanes. The player stays fully opaque so they're unmistakable at a glance.
    private const byte RegularCarAlpha = 0xA8;

    private static readonly Brush PlayerBorder = Brushes.White;
    private static readonly Brush PitBorder = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x1A));
    private static readonly Brush CarNumberText = Brushes.Black;

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
            return;
        }

        var centerY = MapArea.ActualHeight / 2;

        // Reuse existing Badge visuals — creating new Border/TextBlock/DropShadowEffect objects
        // every tick generated enough garbage to trigger visible GC pauses (the reported stutter).
        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            Border badge;
            if (i < MarkerLayer.Children.Count)
            {
                badge = (Border)MarkerLayer.Children[i];
                UpdateBadge(badge, marker);
            }
            else
            {
                badge = BuildBadge(marker);
                MarkerLayer.Children.Add(badge);
            }

            badge.Visibility = Visibility.Visible;

            var size = marker.IsPlayer ? PlayerMarkerWidth : MarkerWidth;
            var height = marker.IsPlayer ? PlayerMarkerHeight : MarkerHeight;
            var x = Math.Clamp(marker.LapDistPct, 0, 1) * width - size / 2;
            var y = centerY - height / 2;

            Canvas.SetLeft(badge, x);
            Canvas.SetTop(badge, y);
            Panel.SetZIndex(badge, marker.IsPlayer ? 10 : 1);
        }

        // Hide surplus pooled elements instead of removing them.
        for (var i = markers.Count; i < MarkerLayer.Children.Count; i++)
            MarkerLayer.Children[i].Visibility = Visibility.Collapsed;
    }

    private static void UpdateBadge(Border badge, TrackMapMarker marker)
    {
        var baseColor = ColorConverter.ConvertFromString(marker.ClassColor) is Color color ? color : Colors.White;
        var alpha = marker.IsPlayer ? (byte)0xFF : RegularCarAlpha;

        badge.Width = marker.IsPlayer ? PlayerMarkerWidth : MarkerWidth;
        badge.Height = marker.IsPlayer ? PlayerMarkerHeight : MarkerHeight;
        badge.CornerRadius = new CornerRadius(marker.IsPlayer ? 4 : 3);
        badge.Background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        badge.BorderThickness = new Thickness(marker.IsPlayer ? 2.5 : marker.OnPitRoad ? 1.5 : 0);
        badge.BorderBrush = marker.IsPlayer ? PlayerBorder : marker.OnPitRoad ? PitBorder : Brushes.Transparent;

        if (marker.IsPlayer && badge.Effect is not DropShadowEffect)
            badge.Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 };
        else if (!marker.IsPlayer && badge.Effect is not null)
            badge.Effect = null;

        var text = (TextBlock)badge.Child;
        text.Text = marker.CarNumber;
        text.FontSize = marker.IsPlayer ? 12 : 9;
    }

    private static Border BuildBadge(TrackMapMarker marker)
    {
        var baseColor = ColorConverter.ConvertFromString(marker.ClassColor) is Color color ? color : Colors.White;
        var alpha = marker.IsPlayer ? (byte)0xFF : RegularCarAlpha;
        var background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

        return new Border
        {
            Width = marker.IsPlayer ? PlayerMarkerWidth : MarkerWidth,
            Height = marker.IsPlayer ? PlayerMarkerHeight : MarkerHeight,
            CornerRadius = new CornerRadius(marker.IsPlayer ? 4 : 3),
            Background = background,
            BorderThickness = new Thickness(marker.IsPlayer ? 2.5 : marker.OnPitRoad ? 1.5 : 0),
            BorderBrush = marker.IsPlayer ? PlayerBorder : marker.OnPitRoad ? PitBorder : Brushes.Transparent,
            Effect = marker.IsPlayer ? new DropShadowEffect { Color = Colors.White, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 } : null,
            Child = new TextBlock
            {
                Text = marker.CarNumber,
                FontSize = marker.IsPlayer ? 12 : 9,
                FontWeight = FontWeights.Bold,
                Foreground = CarNumberText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}

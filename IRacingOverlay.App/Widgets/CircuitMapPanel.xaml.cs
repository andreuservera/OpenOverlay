using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.Localization;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.TrackMaps;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class CircuitMapPanel : UserControl
{
    private const double MarkerDiameter = 16;
    private const double MarkerFontSize = 9.5;
    private const double NumberSlantDegrees = 12;
    private const double Inset = 12;

    private static readonly Brush MarkerRim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush PlayerRim = StatePalette.TextPrimary;
    private static readonly Brush CarNumberInk = StatePalette.TextOnAccent;

    private readonly Dictionary<string, Geometry> _numberGlyphs = new();

    // The shape as last fitted, so the geometry is only rebuilt when the track or the size changes.
    private TrackOutline? _fitted;
    private Size _fittedSize;
    private double _scale;
    private double _midX;
    private double _midY;
    private string? _learningText;

    public CircuitMapPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(CircuitMapState state)
    {
        var size = new Size(MapArea.ActualWidth, MapArea.ActualHeight);
        if (size.Width <= 0 || size.Height <= 0)
        {
            HideMarkers(0);
            return;
        }

        if (!ReferenceEquals(state.Outline, _fitted) || size != _fittedSize)
        {
            Fit(state.Outline, size);
        }

        SetLearningText(state.Outline is not null
            ? ""
            : state.LearningProgress is { } progress
                ? Loc.F("widget.CircuitMap.learningLap", (int)Math.Round(progress * 100))
                : Loc.T("widget.CircuitMap.learning"));

        if (state.Outline is not { } outline)
        {
            HideMarkers(0);
            return;
        }

        var markers = state.Markers;
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
            var at = ToScreen(outline.At(marker.LapDistPct), size);
            Canvas.SetLeft(badge, at.X - MarkerDiameter / 2);
            Canvas.SetTop(badge, at.Y - MarkerDiameter / 2);
            Panel.SetZIndex(badge, marker.IsPlayer ? 10 : 1);
            badge.Opacity = marker.OnPitRoad ? 0.5 : 1;
        }

        HideMarkers(markers.Count);
    }

    private void HideMarkers(int from)
    {
        for (var i = from; i < MarkerLayer.Children.Count; i++)
            MarkerLayer.Children[i].Visibility = Visibility.Collapsed;
    }

    private void SetLearningText(string text)
    {
        if (text == _learningText)
        {
            return;
        }

        _learningText = text;
        Learning.Text = text;
        Learning.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Fit(TrackOutline? outline, Size size)
    {
        _fitted = outline;
        _fittedSize = size;
        if (outline is null)
        {
            TrackBed.Data = null;
            TrackLine.Data = null;
            StartFinish.Visibility = Visibility.Collapsed;
            return;
        }

        var (minX, minY, maxX, maxY) = outline.Bounds();
        _midX = (minX + maxX) / 2;
        _midY = (minY + maxY) / 2;
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        _scale = Math.Min((size.Width - 2 * Inset) / spanX, (size.Height - 2 * Inset) / spanY);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToScreen((outline.X[0], outline.Y[0]), size), isFilled: false, isClosed: true);
            for (var i = 1; i < outline.Count; i++)
            {
                context.LineTo(ToScreen((outline.X[i], outline.Y[i]), size), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        TrackBed.Data = geometry;
        TrackLine.Data = geometry;

        // Start/finish: a short bar across the track at lap 0, square to the direction of travel.
        var start = ToScreen(outline.At(0), size);
        var ahead = ToScreen(outline.At(2.0 / outline.Count), size);
        var direction = ahead - start;
        if (direction.Length > 0)
        {
            direction.Normalize();
            var across = new Vector(-direction.Y, direction.X) * 7;
            StartFinish.X1 = start.X - across.X;
            StartFinish.Y1 = start.Y - across.Y;
            StartFinish.X2 = start.X + across.X;
            StartFinish.Y2 = start.Y + across.Y;
            StartFinish.Visibility = Visibility.Visible;
        }
    }

    // East to the right, north up.
    private Point ToScreen((double X, double Y) p, Size size) =>
        new(size.Width / 2 + (p.X - _midX) * _scale, size.Height / 2 - (p.Y - _midY) * _scale);

    private void OnMapAreaSizeChanged(object sender, SizeChangedEventArgs e) => _fittedSize = default;

    private void UpdateBadge(Border badge, TrackMapMarker marker)
    {
        var classColor = ColorConverter.ConvertFromString(marker.ClassColor) is Color color ? color : Colors.White;
        if (badge.Background is not SolidColorBrush fill || fill.Color != classColor)
        {
            badge.Background = new SolidColorBrush(classColor);
        }

        badge.BorderBrush = marker.IsPlayer ? PlayerRim : MarkerRim;
        badge.BorderThickness = new Thickness(marker.IsPlayer ? 2 : 1);

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

    // Same outlined, hand-slanted numbers as the bar map, so the digits sit dead centre in the disc.
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

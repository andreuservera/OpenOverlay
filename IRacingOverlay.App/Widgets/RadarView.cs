using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// The radar drawn straight into a DrawingContext: the player's car in the middle pointing up, other
/// cars ahead above and behind below at their real gap, cars alongside to the side iRacing names,
/// with an amber bar down that side of the player's car while one is there.
/// </summary>
public sealed class RadarView : FrameworkElement
{
    private const double CarWidth = 20;
    private const double LaneOffset = 28;

    private static readonly Brush PlayerFill = StatePalette.TextPrimary;
    private static readonly Brush Alongside = StatePalette.Warning;
    private static readonly Pen CarRim = FrozenPen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), 1);
    private static readonly Pen Guide = FrozenPen(Color.FromArgb(0x33, 0xB4, 0xBC, 0xC8), 1);

    private readonly Dictionary<string, Brush> _fills = new();
    private RadarState _state = RadarState.Empty;

    public void Update(RadarState state)
    {
        _state = state;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(110, 180);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var cx = w / 2;
        var cy = h / 2;
        var perMetre = h / (2 * RadarBuilder.RangeMeters);
        var carLength = RadarBuilder.CarLengthMeters * perMetre;

        // Faint guides: the band level with the player, where a car alongside is.
        dc.DrawLine(Guide, new Point(cx - LaneOffset - CarWidth, cy - carLength), new Point(cx + LaneOffset + CarWidth, cy - carLength));
        dc.DrawLine(Guide, new Point(cx - LaneOffset - CarWidth, cy + carLength), new Point(cx + LaneOffset + CarWidth, cy + carLength));

        var leftBusy = false;
        var rightBusy = false;
        foreach (var car in _state.Cars)
        {
            var x = car.Lane switch
            {
                RadarLane.Left => cx - LaneOffset,
                RadarLane.Right => cx + LaneOffset,
                _ => cx,
            };
            var y = cy - car.GapMeters * perMetre;
            var rect = new Rect(x - CarWidth / 2, y - carLength / 2, CarWidth, carLength);
            // Cars further off fade, so the eye goes to the near ones.
            var near = 1 - Math.Clamp(Math.Abs(car.GapMeters) / RadarBuilder.RangeMeters, 0, 1);
            dc.PushOpacity(car.Alongside ? 1 : 0.35 + 0.65 * near);
            dc.DrawRoundedRectangle(Fill(car.ClassColor), CarRim, rect, 4, 4);
            dc.Pop();
            leftBusy |= car.Lane == RadarLane.Left;
            rightBusy |= car.Lane == RadarLane.Right;
        }

        var player = new Rect(cx - CarWidth / 2, cy - carLength / 2, CarWidth, carLength);
        dc.DrawRoundedRectangle(PlayerFill, null, player, 4, 4);
        if (leftBusy)
        {
            dc.DrawRoundedRectangle(Alongside, null, new Rect(player.Left - 6, player.Top - 4, 3, player.Height + 8), 1.5, 1.5);
        }

        if (rightBusy)
        {
            dc.DrawRoundedRectangle(Alongside, null, new Rect(player.Right + 3, player.Top - 4, 3, player.Height + 8), 1.5, 1.5);
        }
    }

    private Brush Fill(string color)
    {
        if (!_fills.TryGetValue(color, out var brush))
        {
            brush = ColorConverter.ConvertFromString(color) is Color c ? Frozen(c) : StatePalette.TextSecondary;
            _fills[color] = brush;
        }

        return brush;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Widgets;

/// <summary>Segmented vertical meter — how much of the nearest car on this side overlaps us, and
/// where — top of the track represents our own front bumper, bottom our rear bumper. Each segment
/// lights amber if the band (BandStart/BandEnd, both 0-1 fractions of our own car length) overlaps
/// that segment's slice of the bar, matching the reference design's LED-style side gauges.</summary>
public partial class ProximityBarPanel : UserControl
{
    private const int SegmentCount = 12;

    private static readonly Brush Lit = StatePalette.Warning;
    private static readonly Brush Off = StatePalette.TrackEmptyWarm;

    private readonly Border[] _segments = new Border[SegmentCount];

    public ProximityBarPanel()
    {
        InitializeComponent();

        for (var i = 0; i < SegmentCount; i++)
        {
            Track.RowDefinitions.Add(new RowDefinition());

            var segment = new Border { Background = Off, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 1, 0, 1) };
            Grid.SetRow(segment, i);
            Track.Children.Add(segment);
            _segments[i] = segment;
        }
    }

    /// <summary>start/end are fractions of the track (0 = our front/top, 1 = our rear/bottom)
    /// marking the band the other car currently occupies.</summary>
    public void SetBand(double start, double end)
    {
        start = Math.Clamp(start, 0, 1);
        end = Math.Clamp(end, 0, 1);

        for (var i = 0; i < SegmentCount; i++)
        {
            var segmentStart = (double)i / SegmentCount;
            var segmentEnd = (double)(i + 1) / SegmentCount;
            var lit = end > start && segmentStart < end && segmentEnd > start;
            _segments[i].Background = lit ? Lit : Off;
        }
    }
}

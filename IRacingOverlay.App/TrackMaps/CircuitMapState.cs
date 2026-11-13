using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.TrackMaps;

/// <summary>What the circuit map draws: the track's shape once known (or how far the lap that is
/// learning it has got), and every car on it.</summary>
public sealed record CircuitMapState(TrackOutline? Outline, double? LearningProgress, IReadOnlyList<TrackMapMarker> Markers)
{
    public static CircuitMapState Empty { get; } = new(null, null, []);
}

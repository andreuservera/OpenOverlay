namespace IRacingOverlay.App.ViewModels;

public sealed class TrackMapMarker
{
    public required int CarIdx { get; init; }
    public required string CarNumber { get; init; }
    /// <summary>0.0 (start/finish line) to 1.0 (back at start/finish) — the car's raw progress
    /// around the current lap, independent of lap count or gap/time.</summary>
    public required double LapDistPct { get; init; }
    public required bool IsPlayer { get; init; }
    public required bool OnPitRoad { get; init; }
    public string ClassColor { get; init; } = "#FFFFFF";
}

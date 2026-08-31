namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// The break between the pinned top three and the block around the player. Carries how many cars
/// fall in the gap so the row can say so, rather than silently pretending P3 and the car above the
/// player are neighbours.
/// </summary>
public sealed class StandingsSeparatorRow
{
    public required int SkippedCount { get; init; }

    public bool HasSkipped => SkippedCount > 0;

    public string Label => SkippedCount > 0 ? $"+{SkippedCount}" : "";
}

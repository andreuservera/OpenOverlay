namespace IRacingOverlay.App.ViewModels;

public sealed class IncidentState
{
    public required int MyIncidentCount { get; init; }
    /// <summary>null when not in a team session (solo racing has no separate team total).</summary>
    public required int? TeamIncidentCount { get; init; }

    public static IncidentState Empty { get; } = new() { MyIncidentCount = 0, TeamIncidentCount = null };
}

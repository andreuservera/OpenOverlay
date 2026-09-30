namespace IRacingOverlay.App.ViewModels;

public enum IncidentSeverity
{
    Normal,
    Warning,
    Critical,
}

public sealed class IncidentState
{
    public required int MyIncidentCount { get; init; }
    /// <summary>null when not in a team race (solo racing has no separate team total).</summary>
    public required int? TeamIncidentCount { get; init; }

    /// <summary>Incident points before disqualification; null when the session has no limit.</summary>
    public int? Limit { get; init; }

    public static IncidentState Empty { get; } = new() { MyIncidentCount = 0, TeamIncidentCount = null };

    /// <summary>The total the limit applies to: the team's in a team race, otherwise your own.</summary>
    public int CountedTotal => TeamIncidentCount ?? MyIncidentCount;

    /// <summary>Colour only means something against a limit: amber from half of it, red from 80%.</summary>
    public IncidentSeverity Severity => Limit is not { } limit || limit <= 0
        ? IncidentSeverity.Normal
        : CountedTotal >= limit * 0.8 ? IncidentSeverity.Critical
        : CountedTotal >= limit * 0.5 ? IncidentSeverity.Warning
        : IncidentSeverity.Normal;
}

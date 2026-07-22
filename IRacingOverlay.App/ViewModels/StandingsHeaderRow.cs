namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Pseudo-row inserted between class groups in the multiclass Standings display. Not part of
/// BuildStandings' own output (that stays a flat, accurate race order) — only
/// <see cref="StandingsBuilder.GroupForDisplay"/> adds these, purely for the UI to render a title
/// bar naming each class group.
/// </summary>
public sealed class StandingsHeaderRow
{
    public required string ClassName { get; init; }
    public string ClassColor { get; init; } = "#FFFFFF";

    // Stronger tint than a regular row's background so the title bar reads as a separator, not just
    // another row.
    public string HeaderBackground => $"#55{ClassColor.TrimStart('#')}";
}

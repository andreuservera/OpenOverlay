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

    /// <summary>Every driver in the class, not just the ones the focused view shows.</summary>
    public int DriverCount { get; init; }

    /// <summary>The class's own strength of field; 0 when nobody in it is rated.</summary>
    public double Sof { get; init; }

    public string DriverCountDisplay => DriverCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string SofDisplay => SofFormat.Format(Sof);
}

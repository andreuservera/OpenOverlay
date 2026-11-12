namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// One corner (or a complex such as a chicane, merged into one) as a window of the lap, in metres from
/// the line. The window runs from where the driver starts braking or lifting to where they are back
/// to full throttle, so the time inside it is everything the corner costs.
/// </summary>
/// <param name="Name">The corner's stable id: T1, T2, … in lap order. Once a track's map is saved
/// (<see cref="CornerMapStore"/>) the same corner keeps the same id in every session.</param>
/// <param name="StartM">Start of the braking/lift zone.</param>
/// <param name="ApexM">Lowest speed in the corner.</param>
/// <param name="EndM">Back to full throttle and out of the turn.</param>
public sealed record Corner(string Name, int StartM, int ApexM, int EndM)
{
    public int LengthM => EndM - StartM;

    /// <summary>The corner's real name (La Source, Eau Rouge…), when one has been set in the saved
    /// track map; null otherwise.</summary>
    public string? Label { get; init; }

    /// <summary>What to show or say: the real name when known, else the T-number.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Name : Label!;
}

/// <summary>The corners of one track, in lap order.</summary>
public sealed class CornerMap
{
    public CornerMap(int trackLengthMetres, IReadOnlyList<Corner> corners, int lapsUsed = 1)
    {
        TrackLengthMetres = trackLengthMetres;
        Corners = corners;
        LapsUsed = lapsUsed;
    }

    public int TrackLengthMetres { get; }

    public IReadOnlyList<Corner> Corners { get; }

    /// <summary>How many clean laps the map was agreed from (1 = a provisional, single-lap map).</summary>
    public int LapsUsed { get; }

    /// <summary>The corner whose window contains <paramref name="metres"/>, or null on a straight.</summary>
    public Corner? At(double metres) => Corners.FirstOrDefault(c => metres >= c.StartM && metres <= c.EndM);
}

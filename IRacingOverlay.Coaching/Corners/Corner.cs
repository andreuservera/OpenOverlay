namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// One corner (or a complex such as a chicane, merged into one) as a window of the lap, in metres from
/// the line. The window runs from where the driver starts braking or lifting to where they are back
/// to full throttle, so the time inside it is everything the corner costs.
/// </summary>
/// <param name="Name">T1, T2, … in lap order.</param>
/// <param name="StartM">Start of the braking/lift zone.</param>
/// <param name="ApexM">Lowest speed in the corner.</param>
/// <param name="EndM">Back to full throttle and out of the turn.</param>
public sealed record Corner(string Name, int StartM, int ApexM, int EndM)
{
    public int LengthM => EndM - StartM;
}

/// <summary>The corners of one track as driven in one car, in lap order.</summary>
public sealed class CornerMap
{
    public CornerMap(int trackLengthMetres, IReadOnlyList<Corner> corners)
    {
        TrackLengthMetres = trackLengthMetres;
        Corners = corners;
    }

    public int TrackLengthMetres { get; }

    public IReadOnlyList<Corner> Corners { get; }
}

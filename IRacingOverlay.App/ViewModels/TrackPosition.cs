namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Where a car physically is on the circuit: laps started (CarIdxLap) plus how far round the current
/// one it is (CarIdxLapDistPct). Relative and the Cockpit's proximity bars read track position from
/// here. It carries no time: a distance on track says nothing about how long it takes to cover, which
/// depends on where on the lap it is — Relative reads seconds off CarIdxEstTime instead.
///
/// It answers two different questions, kept apart deliberately. <see cref="RaceDistance"/> is the
/// absolute one — who is a lap up or down. <see cref="OnTrackGapTo"/> is folded around one car and
/// only says who is near it on the road; folding throws whole laps away, so it must never count a lap.
/// </summary>
internal readonly record struct TrackPosition(int Lap, double LapDistPct)
{
    /// <summary>Continuous race distance in laps, e.g. 10.5 halfway round lap 10.</summary>
    public double RaceDistance => Lap + LapDistPct;

    /// <summary>How far ahead in the race <paramref name="other"/> is, in laps, whole laps included:
    /// a car a lap down reads about -1 wherever it is on the road. A car still on lap 0 counts as on
    /// the start line (<see cref="LapCountingDistance"/>).</summary>
    public double RaceGapTo(TrackPosition other) => other.LapCountingDistance - LapCountingDistance;

    /// <summary>
    /// <see cref="RaceDistance"/> for counting laps up or down. A car on lap 0 hasn't crossed the start
    /// line since the green, so it counts as on the line: where iRacing places it before then says
    /// nothing about laps. Rallycross grids sit off the lap path — at Phoenix the grid reads ~0.54 on
    /// lap 0 and jumps to ~0.97 as the car joins the circuit (seen in an .ibt, 2026-11-09) — so a car
    /// still on the grid read half a lap behind the player who had just crossed, and the Relative
    /// showed it as lapped for the seconds it took to get going.
    /// </summary>
    private double LapCountingDistance => Lap == 0 ? 1 : RaceDistance;

    /// <summary>
    /// How far up the road <paramref name="other"/> is, in laps, folded into [-0.5, 0.5): positive
    /// ahead, negative behind. Lap counts are ignored — a car a lap down running right behind is still
    /// right behind.
    /// </summary>
    public double OnTrackGapTo(TrackPosition other) => FoldToHalfLap(other.LapDistPct - LapDistPct);

    /// <summary>A separation in laps folded into [-0.5, 0.5): the shortest way round, across the
    /// start/finish line if that's nearer.</summary>
    public static double FoldToHalfLap(double laps) => laps - Math.Floor(laps + 0.5);

    /// <summary>A car's position this tick, or null when iRacing can't place it: both arrays read -1
    /// for a car that isn't in the world (in the garage, or mid-tow).</summary>
    public static TrackPosition? Read(int[] laps, float[] lapDistPct, int carIdx)
    {
        if (carIdx < 0 || carIdx >= laps.Length || carIdx >= lapDistPct.Length ||
            laps[carIdx] < 0 || lapDistPct[carIdx] < 0)
        {
            return null;
        }

        return new TrackPosition(laps[carIdx], lapDistPct[carIdx]);
    }
}

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Where a car physically is on the circuit: laps started (CarIdxLap) plus how far round the current
/// one it is (CarIdxLapDistPct). The app's one definition of track position — Standings, Relative and
/// the Cockpit's proximity bars all read it from here. It carries no lap time on purpose, so an order
/// taken from it can't depend on how fast any class runs; turning a separation into seconds is a
/// separate, later step (<see cref="ReferencePace"/>).
///
/// It answers two different questions, kept apart deliberately. <see cref="RaceDistance"/> is the
/// absolute one — running order, laps up and down, gaps to a leader. <see cref="OnTrackGapTo"/> is
/// folded around one car and only says who is near it on the road; folding throws whole laps away, so
/// it must never decide an order or count a lap.
/// </summary>
internal readonly record struct TrackPosition(int Lap, double LapDistPct)
{
    /// <summary>Continuous race distance in laps, e.g. 10.5 halfway round lap 10.</summary>
    public double RaceDistance => Lap + LapDistPct;

    /// <summary>How far ahead in the race <paramref name="other"/> is, in laps, whole laps included:
    /// a car a lap down reads about -1 wherever it is on the road.</summary>
    public double RaceGapTo(TrackPosition other) => other.RaceDistance - RaceDistance;

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

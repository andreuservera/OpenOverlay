namespace IRacingOverlay.App.TrackMaps;

/// <summary>
/// One track's shape, learned from a lap driven there: <see cref="Count"/> points evenly spaced by
/// lap fraction (point i sits at i / Count of the lap), in metres east (<see cref="X"/>) and north
/// (<see cref="Y"/>) of the shape's centre. Any car's place on it follows from its LapDistPct alone.
/// </summary>
public sealed record TrackOutline(string Track, float[] X, float[] Y, double LengthM, double ClosureShare)
{
    public int Count => X.Length;

    /// <summary>The point at a lap fraction, between the two stored points either side of it.</summary>
    public (double X, double Y) At(double lapDistPct)
    {
        var n = Count;
        var position = (lapDistPct - Math.Floor(lapDistPct)) * n;
        var i = (int)position % n;
        var next = (i + 1) % n;
        var t = position - Math.Floor(position);
        return (X[i] + (X[next] - X[i]) * t, Y[i] + (Y[next] - Y[i]) * t);
    }

    /// <summary>The track's extent, for fitting it into a panel.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i < Count; i++)
        {
            minX = Math.Min(minX, X[i]);
            maxX = Math.Max(maxX, X[i]);
            minY = Math.Min(minY, Y[i]);
            maxY = Math.Max(maxY, Y[i]);
        }

        return (minX, minY, maxX, maxY);
    }
}

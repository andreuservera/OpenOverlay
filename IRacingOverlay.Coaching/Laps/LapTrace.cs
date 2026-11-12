namespace IRacingOverlay.Coaching.Laps;

/// <summary>
/// One lap resampled onto a fixed distance grid: index i is i metres past the start/finish line, for
/// i in [0, <see cref="Length"/>). Every array has <see cref="Length"/> entries. Two laps of the same
/// track line up point for point, which is what lets corners, deltas and braking points be compared.
/// </summary>
public sealed class LapTrace
{
    public LapTrace(int length)
    {
        if (length <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Length = length;
        Time = new float[length];
        Speed = new float[length];
        Throttle = new float[length];
        Brake = new float[length];
        Steering = new float[length];
        LatAccel = new float[length];
        LongAccel = new float[length];
        YawRate = new float[length];
        Gear = new sbyte[length];
        Abs = new bool[length];
    }

    /// <summary>Grid points (metres); the track length rounded down.</summary>
    public int Length { get; }

    /// <summary>Seconds since the lap started (crossing the line), at each metre.</summary>
    public float[] Time { get; }

    /// <summary>m/s.</summary>
    public float[] Speed { get; }

    public float[] Throttle { get; }
    public float[] Brake { get; }

    /// <summary>Steering wheel angle, rad.</summary>
    public float[] Steering { get; }

    /// <summary>m/s².</summary>
    public float[] LatAccel { get; }

    /// <summary>m/s².</summary>
    public float[] LongAccel { get; }

    /// <summary>rad/s.</summary>
    public float[] YawRate { get; }

    public sbyte[] Gear { get; }
    public bool[] Abs { get; }

    /// <summary>GPS latitude per metre (degrees), when the source had it (.ibt only).</summary>
    public double[]? Latitude { get; set; }

    public double[]? Longitude { get; set; }

    /// <summary>Time at a fractional distance, interpolated between grid points.</summary>
    public double TimeAt(double metres)
    {
        if (metres <= 0)
        {
            return Time[0];
        }

        if (metres >= Length - 1)
        {
            return Time[Length - 1];
        }

        var i = (int)metres;
        var f = metres - i;
        return Time[i] + (Time[i + 1] - Time[i]) * f;
    }
}

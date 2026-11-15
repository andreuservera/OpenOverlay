using System.IO.Compression;
using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Reports;

/// <summary>
/// One lap's channels on the 1 m grid, as kept for comparing laps on the Coaching page: time,
/// speed, pedals and gear at every metre from the line.
/// </summary>
public sealed record StoredLap(int LapNumber, double LapTime, float[] Time, float[] SpeedKmh, float[] Throttle, float[] Brake, sbyte[] Gear)
{
    public int Length => Time.Length;

    public static StoredLap From(int lapNumber, double lapTime, LapTrace t) =>
        new(lapNumber, lapTime, (float[])t.Time.Clone(), t.Speed.Select(v => v * 3.6f).ToArray(), (float[])t.Throttle.Clone(), (float[])t.Brake.Clone(), (sbyte[])t.Gear.Clone());
}

/// <summary>
/// Every measured lap of a saved session, kept beside its report so any two laps can be overlaid
/// later: one small gzip'd binary file per lap (about 60 KB for a 7 km lap), in
/// <c>&lt;sessions folder&gt;\traces\&lt;report id&gt;\</c>. Written once per lap; a live
/// session adds its laps as they are driven.
/// </summary>
public sealed class LapTraceStore
{
    private const int Magic = 0x544C4F4F; // "OOLT"
    private const int Version = 1;

    public LapTraceStore(string directory)
    {
        Directory = directory;
    }

    /// <summary>The traces folder next to a report store's files.</summary>
    public static LapTraceStore For(SessionReportStore reports) => new(Path.Combine(reports.Directory, "traces"));

    public string Directory { get; }

    private string FolderFor(string reportId) => Path.Combine(Directory, SessionReportStore.FileName(reportId));

    private string PathFor(string reportId, int lap) => Path.Combine(FolderFor(reportId), $"lap-{lap:000}.bin.gz");

    /// <summary>The laps kept for a report, in order.</summary>
    public IReadOnlyList<int> Laps(string reportId)
    {
        var folder = FolderFor(reportId);
        if (!System.IO.Directory.Exists(folder))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(folder, "lap-*.bin.gz")
            .Select(p => Path.GetFileName(p)[4..^7])
            .Select(s => int.TryParse(s, out var n) ? n : -1)
            .Where(n => n >= 0)
            .Order()
            .ToList();
    }

    public bool Has(string reportId, int lap) => File.Exists(PathFor(reportId, lap));

    /// <summary>Keeps a lap unless it is already kept.</summary>
    public void Save(string reportId, StoredLap lap)
    {
        var path = PathFor(reportId, lap.LapNumber);
        if (File.Exists(path))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(FolderFor(reportId));
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var zip = new GZipStream(file, CompressionLevel.Optimal))
        using (var w = new BinaryWriter(zip))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(lap.LapNumber);
            w.Write(lap.LapTime);
            w.Write(lap.Length);
            foreach (var a in new[] { lap.Time, lap.SpeedKmh, lap.Throttle, lap.Brake })
            {
                foreach (var v in a)
                {
                    w.Write(v);
                }
            }

            foreach (var g in lap.Gear)
            {
                w.Write(g);
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Keeps every measured lap of a session that isn't kept yet.</summary>
    public void SaveAll(string reportId, CoachingSession session)
    {
        foreach (var lap in session.AllAnalyses.Select(a => a.Lap))
        {
            if (lap.Trace is { } t && !Has(reportId, lap.LapNumber))
            {
                Save(reportId, StoredLap.From(lap.LapNumber, lap.LapTime, t));
            }
        }
    }

    /// <summary>A kept lap; null when it isn't kept or can't be read.</summary>
    public StoredLap? Load(string reportId, int lap)
    {
        var path = PathFor(reportId, lap);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var file = File.OpenRead(path);
            using var zip = new GZipStream(file, CompressionMode.Decompress);
            using var r = new BinaryReader(zip);
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version)
            {
                return null;
            }

            var number = r.ReadInt32();
            var time = r.ReadDouble();
            var n = r.ReadInt32();
            if (n is <= 1 or > 100_000)
            {
                return null;
            }

            float[] Floats()
            {
                var a = new float[n];
                for (var i = 0; i < n; i++)
                {
                    a[i] = r.ReadSingle();
                }

                return a;
            }

            var t = Floats();
            var speed = Floats();
            var throttle = Floats();
            var brake = Floats();
            var gear = new sbyte[n];
            for (var i = 0; i < n; i++)
            {
                gear[i] = r.ReadSByte();
            }

            return new StoredLap(number, time, t, speed, throttle, brake, gear);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    public void Delete(string reportId)
    {
        var folder = FolderFor(reportId);
        if (System.IO.Directory.Exists(folder))
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }
}

/// <summary>Two laps compared metre by metre.</summary>
public static class LapDelta
{
    /// <summary>
    /// Time <paramref name="b"/> is behind <paramref name="a"/> at each metre from
    /// <paramref name="fromM"/> to <paramref name="toM"/>, seconds (positive = b slower), counted
    /// from <paramref name="fromM"/> so a corner's delta starts at zero.
    /// </summary>
    public static float[] Between(StoredLap a, StoredLap b, int fromM, int toM)
    {
        var n = Math.Min(a.Length, b.Length);
        fromM = Math.Clamp(fromM, 0, n - 1);
        toM = Math.Clamp(toM, fromM, n - 1);
        var d0 = b.Time[fromM] - a.Time[fromM];
        var result = new float[toM - fromM + 1];
        for (var i = fromM; i <= toM; i++)
        {
            result[i - fromM] = b.Time[i] - a.Time[i] - d0;
        }

        return result;
    }
}

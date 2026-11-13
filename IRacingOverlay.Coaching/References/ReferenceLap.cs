using System.IO.Compression;
using System.Text.Json;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.References;

/// <summary>
/// The lap the live coach compares against when it isn't this session's best: the fastest clean
/// lap ever driven (or loaded from a faster driver's .ibt) for one track and car, with its full
/// 1 m trace so its corners can be measured against whatever corner map is in use.
/// </summary>
public sealed record ReferenceLap
{
    public required string TrackKey { get; init; }

    public required string Car { get; init; }

    public required double LapTime { get; init; }

    public required DateTime RecordedUtc { get; init; }

    /// <summary>Where it came from: "live", "ibt", or "imported" (an .ibt chosen as the reference,
    /// maybe someone else's).</summary>
    public required string Source { get; init; }

    /// <summary>The .ibt it came from, when it came from one.</summary>
    public string? SourceFile { get; init; }

    public required LapTrace Trace { get; init; }
}

/// <summary>What the Coaching page lists about a stored reference, without its trace.</summary>
public sealed record ReferenceLapInfo(string TrackKey, string? TrackName, string Car, double LapTime, DateTime RecordedUtc, string Source, string Path);

/// <summary>
/// The reference laps, one gzip'd JSON file per track and car (a few hundred KB each). Default
/// folder: %LOCALAPPDATA%\IRacingOverlay\coaching\references.
/// </summary>
public sealed class ReferenceLapStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ReferenceLapStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public static string DefaultDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay", "coaching", "references");

    public string PathFor(string trackKey, string car) =>
        System.IO.Path.Combine(Directory, CornerMapStore.FileName(trackKey) + "__" + CornerMapStore.FileName(car) + ".json.gz");

    /// <summary>The reference for a track and car; null when there is none or it can't be read.</summary>
    public ReferenceLap? Load(string trackKey, string car)
    {
        var path = PathFor(trackKey, car);
        return File.Exists(path) ? Read(path) : null;
    }

    /// <summary>Every stored reference, newest first, without the traces kept in memory.</summary>
    public IReadOnlyList<ReferenceLapInfo> List(Func<string, string?>? trackName = null)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(Directory, "*.json.gz")
            .Select(p => (Path: p, Lap: Read(p)))
            .Where(x => x.Lap is not null)
            .Select(x => new ReferenceLapInfo(x.Lap!.TrackKey, trackName?.Invoke(x.Lap.TrackKey), x.Lap.Car, x.Lap.LapTime, x.Lap.RecordedUtc, x.Lap.Source, x.Path))
            .OrderByDescending(i => i.RecordedUtc)
            .ToList();
    }

    public void Save(ReferenceLap lap)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(lap.TrackKey, lap.Car);
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var zip = new GZipStream(file, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(zip, Dto.From(lap), Json);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Keeps <paramref name="lap"/> as the track and car's reference if it is clean and quicker
    /// than the one stored (or there is none). Returns the reference in force afterwards.
    /// </summary>
    public ReferenceLap? Offer(string trackKey, string car, RecordedLap lap, string source, string? sourceFile = null, DateTime? recordedUtc = null)
    {
        var current = Load(trackKey, car);
        if (!lap.IsClean || lap.Trace is null || (current is not null && current.LapTime <= lap.LapTime))
        {
            return current;
        }

        var reference = new ReferenceLap
        {
            TrackKey = trackKey,
            Car = car,
            LapTime = lap.LapTime,
            RecordedUtc = recordedUtc ?? DateTime.UtcNow,
            Source = source,
            SourceFile = sourceFile,
            Trace = lap.Trace,
        };
        try
        {
            Save(reference);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not saved: the next quicker lap tries again.
        }

        return reference;
    }

    public void Delete(string trackKey, string car)
    {
        var path = PathFor(trackKey, car);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static ReferenceLap? Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var zip = new GZipStream(file, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<Dto>(zip, Json)?.ToLap();
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class Dto
    {
        public int Version { get; set; } = 1;

        public string TrackKey { get; set; } = "";

        public string Car { get; set; } = "";

        public double LapTime { get; set; }

        public DateTime RecordedUtc { get; set; }

        public string Source { get; set; } = "";

        public string? SourceFile { get; set; }

        public float[] Time { get; set; } = [];

        public float[] Speed { get; set; } = [];

        public float[] Throttle { get; set; } = [];

        public float[] Brake { get; set; } = [];

        public float[] Steering { get; set; } = [];

        public float[] LatAccel { get; set; } = [];

        public float[] LongAccel { get; set; } = [];

        public float[] YawRate { get; set; } = [];

        public sbyte[] Gear { get; set; } = [];

        public bool[] Abs { get; set; } = [];

        public static Dto From(ReferenceLap r) => new()
        {
            TrackKey = r.TrackKey,
            Car = r.Car,
            LapTime = r.LapTime,
            RecordedUtc = r.RecordedUtc,
            Source = r.Source,
            SourceFile = r.SourceFile,
            Time = r.Trace.Time,
            Speed = r.Trace.Speed,
            Throttle = r.Trace.Throttle,
            Brake = r.Trace.Brake,
            Steering = r.Trace.Steering,
            LatAccel = r.Trace.LatAccel,
            LongAccel = r.Trace.LongAccel,
            YawRate = r.Trace.YawRate,
            Gear = r.Trace.Gear,
            Abs = r.Trace.Abs,
        };

        public ReferenceLap? ToLap()
        {
            var n = Time.Length;
            if (n <= 1 || new[] { Speed.Length, Throttle.Length, Brake.Length, Steering.Length, LatAccel.Length, LongAccel.Length, YawRate.Length, Gear.Length, Abs.Length }.Any(l => l != n))
            {
                return null;
            }

            var t = new LapTrace(n);
            Time.CopyTo(t.Time, 0);
            Speed.CopyTo(t.Speed, 0);
            Throttle.CopyTo(t.Throttle, 0);
            Brake.CopyTo(t.Brake, 0);
            Steering.CopyTo(t.Steering, 0);
            LatAccel.CopyTo(t.LatAccel, 0);
            LongAccel.CopyTo(t.LongAccel, 0);
            YawRate.CopyTo(t.YawRate, 0);
            Gear.CopyTo(t.Gear, 0);
            Abs.CopyTo(t.Abs, 0);
            return new ReferenceLap
            {
                TrackKey = TrackKey,
                Car = Car,
                LapTime = LapTime,
                RecordedUtc = RecordedUtc,
                Source = Source,
                SourceFile = SourceFile,
                Trace = t,
            };
        }
    }
}

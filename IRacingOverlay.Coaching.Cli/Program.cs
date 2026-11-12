using System.Globalization;
using System.Text;
using IRacingOverlay.Coaching;
using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Ibt;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Speech;
using IRacingOverlay.Sdk;

// coaching-analyze <file.ibt> [--csv <dir>] [--relearn] [--no-save] [--store <dir>] [--debrief] [--speak]
// coaching-analyze --live [--speak] [--store <dir>]
//
// Runs the Phase 0 coaching pipeline over an iRacing telemetry file and prints what it found: every
// lap (with iRacing's own lap time beside ours, as a check), the corners, each clean lap's time per
// corner, and the best pass through each corner. --csv also writes every clean lap's 1 m trace.
//
// Corners come from the saved map for the track when there is one (same T-numbers and names every
// time). Otherwise they are agreed from up to five of the fastest clean laps and saved. --relearn
// ignores the saved map and agrees a new one; --no-save leaves the saved maps untouched.
//
// --debrief prints the post-lap coaching each clean lap would have got (Phase 1), against the best
// passes up to that lap; --speak also says the last one out loud. --live connects to iRacing and
// gives that debrief after every clean lap while you drive, spoken with --speak.

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("usage: coaching-analyze <file.ibt> [--csv <output dir>] [--relearn] [--no-save] [--store <dir>] [--corners-per-lap] [--crossings] [--debrief] [--speak]");
    Console.WriteLine("       coaching-analyze --live [--speak] [--store <dir>]");
    return args.Length == 0 ? 1 : 0;
}

var live = args[0] == "--live";
var path = args[0];
string? csvDir = null;
var relearn = false;
var save = true;
var perLap = false;
var crossings = false;
var debrief = false;
var speak = false;
var storeDir = CornerMapStore.DefaultDirectory;
for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--csv" when i + 1 < args.Length:
            csvDir = args[++i];
            break;
        case "--store" when i + 1 < args.Length:
            storeDir = args[++i];
            break;
        case "--relearn":
            relearn = true;
            break;
        case "--no-save":
            save = false;
            break;
        case "--corners-per-lap":
            perLap = true;
            break;
        case "--crossings":
            crossings = true;
            break;
        case "--debrief":
            debrief = true;
            break;
        case "--speak":
            speak = true;
            break;
    }
}

if (live)
{
    return RunLive(storeDir, speak);
}

using var ibt = IbtFile.Open(path);
var weekend = ibt.Session?.WeekendInfo;
var driverInfo = ibt.Session?.DriverInfo;
var car = driverInfo?.Drivers.FirstOrDefault(d => d.CarIdx == driverInfo.DriverCarIdx)?.CarScreenName;

Console.WriteLine($"File      {Path.GetFileName(path)}");
Console.WriteLine($"Track     {weekend?.TrackDisplayName ?? weekend?.TrackName ?? "?"} ({weekend?.TrackLength ?? "?"})");
Console.WriteLine($"Car       {car ?? "?"}");
Console.WriteLine($"Recorded  {ibt.SessionStartUtc:yyyy-MM-dd HH:mm} UTC, {ibt.RecordCount} ticks at {ibt.TickRate} Hz, {ibt.Variables.Count} channels");
Console.WriteLine();

if (crossings)
{
    DumpCrossings(ibt);
    return 0;
}

var store = new CornerMapStore(storeDir);
var trackKey = weekend?.TrackName;
var session = new CoachingSession(new CoachingOptions
{
    TrackKey = trackKey,
    Store = trackKey is null ? null : store,
    LoadSavedMap = !relearn, // --relearn: agree a new map (still saved over unless --no-save)
    CornerMapLaps = int.MaxValue, // agree the map below, from the fastest laps of the whole file
});
foreach (var frame in ibt.Frames())
{
    session.Observe(frame);
}

session.Flush();

if (session.CornerMapOrigin != CornerMapOrigin.Saved && session.Analyses.Count > 0)
{
    var fastest = session.Analyses.Select(a => a.Lap).OrderBy(l => l.LapTime).Take(5).ToList();
    // Saving through the session also picks up names already typed into the saved file.
    session.RebuildCornerMap(fastest, save: save);
}

Console.WriteLine("Laps (ours vs iRacing's LapLastLapTime)");
Console.WriteLine("  lap   time        iRacing     diff      status");
foreach (var lap in session.Laps)
{
    var reported = lap.ReportedLapTime is { } r ? Time(r) : "-";
    var diff = lap.ReportedLapTime is { } rr ? $"{(lap.LapTime - rr) * 1000,6:+0;-0;0} ms" : "";
    var status = lap.IsClean ? "clean" : string.Join(", ", Enum.GetValues<LapFlaw>().Where(f => f != LapFlaw.None && lap.Flaws.HasFlag(f)));
    Console.WriteLine($"  {lap.LapNumber,3}   {Time(lap.LapTime),-10}  {reported,-10}  {diff,-9} {status}");
}

Console.WriteLine();
if (session.CornerMap is not { } map)
{
    Console.WriteLine("No clean lap, so no corners were learned.");
    return 0;
}

var origin = session.CornerMapOrigin switch
{
    CornerMapOrigin.Saved => $"saved map for '{trackKey}'",
    _ => $"agreed from {map.LapsUsed} clean laps" + (save && trackKey is not null ? $", saved for '{trackKey}'" : ""),
};
Console.WriteLine($"Corners: {origin} (track {map.TrackLengthMetres} m)");
if (trackKey is not null)
{
    Console.WriteLine($"  edit corner names in {store.PathFor(trackKey)}");
}

Console.WriteLine("  corner  start   apex    end     length  best min speed  name");
foreach (var c in map.Corners)
{
    var minSpeed = session.BestPasses.TryGetValue(c.Name, out var bp) ? $"{Kmh(bp.MinSpeed),5:0} km/h" : "    -     ";
    Console.WriteLine($"  {c.Name,-6}  {c.StartM,5} m {c.ApexM,5} m {c.EndM,5} m {c.LengthM,5} m  {minSpeed}      {c.Label ?? ""}");
}

if (perLap)
{
    // Diagnostic: what each clean lap would give on its own, to see how much detection moves.
    Console.WriteLine();
    Console.WriteLine("Corner apexes found on each clean lap by itself (m)");
    foreach (var a in session.Analyses)
    {
        var single = CornerDetector.Detect(a.Lap.Trace!);
        Console.WriteLine($"  lap {a.Lap.LapNumber,3}: {single.Corners.Count,2} corners  " + string.Join(" ", single.Corners.Select(c => c.ApexM)));
    }
}

Console.WriteLine();
Console.WriteLine("Time per corner on each clean lap (s); * = best pass");
var header = new StringBuilder("  lap   lap time   ");
foreach (var c in map.Corners)
{
    header.Append(c.DisplayName.PadLeft(Width(c)));
}

Console.WriteLine(header);
foreach (var a in session.Analyses)
{
    var line = new StringBuilder($"  {a.Lap.LapNumber,3}   {Time(a.Lap.LapTime),-9}  ");
    foreach (var c in map.Corners)
    {
        var pass = a.Passes.FirstOrDefault(p => p.Corner.Name == c.Name);
        var isBest = pass is not null && ReferenceEquals(session.BestPasses[c.Name], pass);
        line.Append((pass is null ? "-  " : $"{pass.SegmentTime:0.000}{(isBest ? "*" : " ")}").PadLeft(Width(c)));
    }

    Console.WriteLine(line);
}

Console.WriteLine();
Console.WriteLine("Best pass per corner");
Console.WriteLine("  corner        lap  time     brake at  peak  trail  min speed @      throttle at  exit     +100 m");
foreach (var c in map.Corners)
{
    if (!session.BestPasses.TryGetValue(c.Name, out var b))
    {
        continue;
    }

    Console.WriteLine(
        $"  {Short(c.DisplayName, 12),-12}  {b.LapNumber,3}  {b.SegmentTime,6:0.000}   {M(b.BrakePointM),8}  {b.PeakBrake * 100,3:0}%  {b.TrailBrakeM,3} m  {Kmh(b.MinSpeed),4:0} km/h {b.MinSpeedAtM,5} m  {M(b.ThrottlePickupM),8}     {Kmh(b.ExitSpeed),4:0}     {Kmh(b.ExitSpeedPlus100),4:0} km/h");
}

Console.WriteLine();
Console.WriteLine($"Best lap         {(session.BestLap is { } bl ? $"{Time(bl.LapTime)} (lap {bl.LapNumber})" : "-")}");
Console.WriteLine($"Theoretical best {(session.TheoreticalBestLapTime is { } tb ? Time(tb) : "-")}  (best lap with every corner at its best pass)");

if (debrief || speak)
{
    Console.WriteLine();
    Console.WriteLine("Post-lap debrief (each clean lap against the best passes up to it)");
    var coach = new PostLapCoach();
    var bestSoFar = new Dictionary<string, CornerPass>(StringComparer.Ordinal);
    var bestLapSoFar = double.MaxValue;
    LapDebrief? last = null;
    foreach (var a in session.Analyses)
    {
        foreach (var p in a.Passes)
        {
            if (!bestSoFar.TryGetValue(p.Corner.Name, out var b) || p.SegmentTime < b.SegmentTime)
            {
                bestSoFar[p.Corner.Name] = p;
            }
        }

        bestLapSoFar = Math.Min(bestLapSoFar, a.Lap.LapTime);
        last = coach.Debrief(a, bestSoFar, bestLapSoFar);
        foreach (var l in DebriefText.Lines(last))
        {
            Console.WriteLine("  " + l);
        }

        Console.WriteLine($"    spoken: \"{DebriefText.Spoken(last)}\"");
    }

    if (speak && last is not null)
    {
        using var voice = new WindowsSpeech();
        voice.SayAndWait(DebriefText.Spoken(last));
    }
}

if (csvDir is not null)
{
    Directory.CreateDirectory(csvDir);
    foreach (var a in session.Analyses)
    {
        var t = a.Lap.Trace!;
        var file = Path.Combine(csvDir, $"lap{a.Lap.LapNumber:000}.csv");
        using var w = new StreamWriter(file);
        w.WriteLine("dist_m,time_s,speed_kmh,throttle,brake,steering_rad,lat_accel,long_accel,yaw_rate,gear,abs");
        for (var m = 0; m < t.Length; m++)
        {
            w.WriteLine($"{m},{t.Time[m]:0.0000},{Kmh(t.Speed[m]):0.00},{t.Throttle[m]:0.000},{t.Brake[m]:0.000},{t.Steering[m]:0.0000},{t.LatAccel[m]:0.000},{t.LongAccel[m]:0.000},{t.YawRate[m]:0.0000},{t.Gear[m]},{(t.Abs[m] ? 1 : 0)}");
        }
    }

    Console.WriteLine($"Wrote {session.Analyses.Count} lap traces to {csvDir}");
}

return 0;

// Live: coach while driving. The session starts once iRacing says which track this is, and starts
// again if the track changes. Everything runs on the connection's reader thread.
static int RunLive(string storeDir, bool speak)
{
    var store = new CornerMapStore(storeDir);
    using var voice = speak ? new WindowsSpeech() : null;
    using var connection = new IRacingConnection();
    CoachingSession? session = null;
    string? track = null;
    string? pendingTrack = null;

    connection.SessionInfoUpdated += (_, info) => pendingTrack = info.WeekendInfo?.TrackName;
    connection.Connected += (_, _) => Console.WriteLine("Connected to iRacing.");
    connection.Disconnected += (_, _) => Console.WriteLine("Disconnected from iRacing.");
    connection.TelemetryUpdated += (_, snapshot) =>
    {
        if (pendingTrack is { } t && t != track)
        {
            track = t;
            session = new CoachingSession(new CoachingOptions { TrackKey = t, Store = store });
            session.CornerMapChanged += m => Console.WriteLine($"Corners for {t}: {m.Corners.Count} ({session.CornerMapOrigin})");
            PostLapCoach.Attach(session, d =>
            {
                Console.WriteLine();
                foreach (var l in DebriefText.Lines(d))
                {
                    Console.WriteLine(l);
                }

                voice?.Say(DebriefText.Spoken(d));
            });
            Console.WriteLine($"Coaching at {t}. Drive clean laps; the debrief comes as you cross the line.");
        }

        session?.Observe(snapshot);
    };

    using var stop = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.Set();
    };

    Console.WriteLine("Waiting for iRacing... (Ctrl+C to stop)");
    connection.Start();
    stop.Wait();
    return 0;
}

// Diagnostic: the raw channels around every line crossing, to compare where we put the line with
// where iRacing's own lap timer restarts (LapCurrentLapTime).
static void DumpCrossings(IbtFile ibt)
{
    string[] names = ["SessionTime", "Lap", "LapDistPct", "LapDist", "Speed", "OnPitRoad", "LapCurrentLapTime", "LapLastLapTime"];
    var present = names.Where(ibt.HasVariable).ToArray();
    Console.WriteLine("  tick  " + string.Join("  ", present.Select(n => $"{n,18}")));
    for (var i = 1; i < ibt.RecordCount; i++)
    {
        var prev = ibt.ReadRecord(i - 1);
        var cur = ibt.ReadRecord(i);
        if (!(prev.Get("LapDistPct") > 0.75 && cur.Get("LapDistPct") < 0.25))
        {
            continue;
        }

        var before = Math.Max(0, 1 - prev.Get("LapDistPct"));
        var after = Math.Max(0, cur.Get("LapDistPct"));
        var at = prev.Get("SessionTime") + (cur.Get("SessionTime") - prev.Get("SessionTime")) * (before + after > 1e-9 ? before / (before + after) : 0.5);
        Console.WriteLine($"crossing into lap {cur.Get("Lap")}: ours at {at:0.0000}");
        for (var j = Math.Max(0, i - 2); j < Math.Min(ibt.RecordCount, i + 8); j++)
        {
            var r = ibt.ReadRecord(j);
            Console.WriteLine($"  {j - i,4}  " + string.Join("  ", present.Select(n => $"{r.Get(n),18:0.000000}")));
        }
    }
}

static string Time(double seconds) => seconds <= 0 ? "-" : $"{(int)(seconds / 60)}:{seconds % 60:00.000}";

static double Kmh(float metresPerSecond) => metresPerSecond * 3.6;

static int Width(Corner c) => Math.Max(8, c.DisplayName.Length + 2);

static string Short(string s, int max) => s.Length <= max ? s : s[..max];

static string M(int? metres) => metres is { } m ? $"{m} m" : "-";

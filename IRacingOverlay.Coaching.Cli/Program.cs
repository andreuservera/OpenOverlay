using System.Globalization;
using System.Text;
using IRacingOverlay.Coaching;
using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Ibt;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Speech;
using IRacingOverlay.Sdk;

// coaching-analyze <file.ibt> [--csv <dir>] [--relearn] [--no-save] [--store <dir>] [--debrief] [--speak] [--no-report] [--reports <dir>]
// coaching-analyze --live [--speak] [--store <dir>] [--no-report] [--reports <dir>]
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
//
// Both save a session report (best lap, theoretical best, recurring mistakes, every corner) after
// every lap, for the post-session analysis on the app's Coaching page; --no-report doesn't.

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("usage: coaching-analyze <file.ibt> [--csv <output dir>] [--relearn] [--no-save] [--store <dir>] [--corners-per-lap] [--crossings] [--debrief] [--speak] [--voice <name>] [--list-voices] [--no-report] [--reports <dir>]");
    Console.WriteLine("       coaching-analyze --live [--speak] [--voice <name>] [--store <dir>] [--no-report] [--reports <dir>]");
    return args.Length == 0 ? 1 : 0;
}

if (args[0] == "--list-voices")
{
    foreach (var (name, culture) in WindowsSpeech.InstalledVoices())
    {
        Console.WriteLine($"{name} ({culture})");
    }

    return 0;
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
string? voiceName = null;
var storeDir = CornerMapStore.DefaultDirectory;
var saveReport = true;
var reportsDir = SessionReportStore.DefaultDirectory;
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
        case "--no-report":
            saveReport = false;
            break;
        case "--reports" when i + 1 < args.Length:
            reportsDir = args[++i];
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
        case "--voice" when i + 1 < args.Length:
            voiceName = args[++i];
            speak = true;
            break;
        case "--list-voices":
            foreach (var (name, culture) in WindowsSpeech.InstalledVoices())
            {
                Console.WriteLine($"{name} ({culture})");
            }

            return 0;
    }
}

if (live)
{
    return RunLive(storeDir, speak, voiceName, saveReport ? new SessionReportStore(reportsDir) : null);
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
// --relearn: agree a new map (still saved over unless --no-save). Without a saved map the corners
// are agreed from the fastest laps of the whole file; saving through the session also picks up
// names already typed into the saved file.
var (session, report) = IbtSessionAnalyzer.Analyze(path, store, relearn, save);

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
if (saveReport)
{
    var reports = new SessionReportStore(reportsDir);
    reports.Save(report);
    Console.WriteLine($"Session report saved for the app's Coaching page: {reports.PathFor(report.Id)}");
    Console.WriteLine();
}

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
    Console.WriteLine($"  names from Lovely Track Data (CC BY-NC-SA 4.0, lsr.gg); to override one, set its name in {store.PathFor(trackKey)}");
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
Console.WriteLine($"Time per piece of the lap (s): the run to the first corner, each corner from {CornerAnalyzer.LeadInMetres} m before its braking to the next corner's piece or {CornerAnalyzer.MaxStraightMetres} m past its end, and the rest of a long straight (+str); * = best, x = not clean");
var allPasses = session.AllAnalyses.SelectMany(a => a.Passes).ToList();
var bestStraights = BestPasses.RestOfStraight(allPasses);
var bestRunIn = session.AllAnalyses.Where(a => a.OutsideCornersTime is not null).MinBy(a => a.OutsideCornersTime);
var straightAfter = map.Corners.Where(c => allPasses.Any(p => p.Corner.Name == c.Name && p.HasRestOfStraight)).Select(c => c.Name).ToHashSet();
const int StrWidth = 10;
var header = new StringBuilder("  lap   lap time     run-in");
foreach (var c in map.Corners)
{
    header.Append(c.DisplayName.PadLeft(Width(c)));
    if (straightAfter.Contains(c.Name))
    {
        header.Append("+str".PadLeft(StrWidth));
    }
}

Console.WriteLine(header);
foreach (var a in session.AllAnalyses)
{
    var line = new StringBuilder($"  {a.Lap.LapNumber,3}   {Time(a.Lap.LapTime),-9}{(a.IsCleanLap ? ' ' : '!')} ");
    var runIn = a.Lap.Trace!.Time[Math.Clamp(CornerAnalyzer.PieceStart(map, 0), 0, a.Lap.Trace.Length - 1)];
    var runInMark = a.OutsideCornersTime is null ? "x" : ReferenceEquals(a, bestRunIn) ? "*" : " ";
    line.Append($"{runIn:0.000}{runInMark}".PadLeft(8));
    foreach (var c in map.Corners)
    {
        var pass = a.Passes.FirstOrDefault(p => p.Corner.Name == c.Name);
        var isBest = pass is not null && session.BestPasses.TryGetValue(c.Name, out var bc) && ReferenceEquals(bc, pass);
        var mark = isBest ? "*" : pass is { IsClean: false } ? "x" : " ";
        line.Append((pass is null ? "-  " : $"{pass.ScoreTime:0.000}{mark}").PadLeft(Width(c)));
        if (straightAfter.Contains(c.Name))
        {
            var strMark = pass is null ? "" : !pass.RestOfStraightClean ? "x"
                : bestStraights.TryGetValue(c.Name, out var bs) && pass.RestOfStraightTime == bs ? "*" : " ";
            line.Append((pass is null ? "-  " : $"{pass.RestOfStraightTime:0.000}{strMark}").PadLeft(StrWidth));
        }
    }

    Console.WriteLine(line);
}

var unclean = session.AllAnalyses.SelectMany(a => a.UncleanPieces().Select(u => (a.Lap.LapNumber, u.Piece, u.Flaws))).ToList();
if (unclean.Count > 0)
{
    Console.WriteLine("  ! = lap not counted; its clean pieces still count. Pieces not clean:");
    foreach (var (lapNumber, piece, flaws) in unclean)
    {
        Console.WriteLine($"    lap {lapNumber,3} {piece}: {flaws}");
    }
}

// Where the theoretical best beats the best lap, piece by piece, and which lap each better piece came from.
if (session.BestLap is { } bestLapForGain && session.AllAnalyses.FirstOrDefault(a => ReferenceEquals(a.Lap, bestLapForGain)) is { } bestLapAnalysis)
{
    var gains = new List<(string Piece, double Gain, int FromLap)>();
    if (bestRunIn is not null && bestLapAnalysis.OutsideCornersTime is { } blRun)
    {
        gains.Add(("run-in", blRun - bestRunIn.OutsideCornersTime!.Value, bestRunIn.Lap.LapNumber));
    }

    foreach (var p in bestLapAnalysis.Passes)
    {
        if (session.BestPasses.TryGetValue(p.Corner.Name, out var b))
        {
            gains.Add((p.Corner.DisplayName, p.ScoreTime - b.ScoreTime, b.LapNumber));
        }

        if (p.HasRestOfStraight && bestStraights.TryGetValue(p.Corner.Name, out var bs))
        {
            var from = allPasses.First(x => x.Corner.Name == p.Corner.Name && x.RestOfStraightClean && x.RestOfStraightTime == bs).LapNumber;
            gains.Add(($"straight after {p.Corner.DisplayName}", p.RestOfStraightTime - bs, from));
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Where the theoretical best gains on the best lap (lap {bestLapForGain.LapNumber}), largest first");
    foreach (var (piece, gain, fromLap) in gains.Where(g => g.Gain > 0.0005).OrderByDescending(g => g.Gain).Take(10))
    {
        Console.WriteLine($"  {Short(piece, 34),-34} {gain,6:0.000} s  from lap {fromLap}");
    }
}

Console.WriteLine();
Console.WriteLine("Best pass per corner");
Console.WriteLine("  corner        type          lap  time     brake at  peak  trail  min speed @      throttle at  exit     +100 m");
foreach (var c in map.Corners)
{
    if (!session.BestPasses.TryGetValue(c.Name, out var b))
    {
        continue;
    }

    Console.WriteLine(
        $"  {Short(c.DisplayName, 12),-12}  {(session.CornerTypeById.TryGetValue(c.Name, out var ct) ? CornerTypes.Label(ct) : "-"),-12}  {b.LapNumber,3}  {b.ScoreTime,6:0.000}   {M(b.BrakePointM),8}  {b.PeakBrake * 100,3:0}%  {b.TrailBrakeM,3} m  {Kmh(b.MinSpeed),4:0} km/h {b.MinSpeedAtM,5} m  {M(b.ThrottlePickupM),8}     {Kmh(b.ExitSpeed),4:0}     {Kmh(b.ExitSpeedPlus100),4:0} km/h");
}

Console.WriteLine();
if (session.CarChangedAfterLap is { } carChanged)
{
    Console.WriteLine($"Car changed after lap {carChanged}: slower on every straight since (likely damage), so bests only come from later laps.");
}

Console.WriteLine($"Best lap         {(session.BestLap is { } bl ? $"{Time(bl.LapTime)} (lap {bl.LapNumber})" : "-")}");
Console.WriteLine($"Theoretical best {(session.TheoreticalBestLapTime is { } tb ? Time(tb) : "-")}  (best run to the first corner + every corner's best clean pass + the best clean rest of each long straight, from {(session.CarChangedAfterLap is { } cc ? $"laps after lap {cc}" : "any lap")})");

Console.WriteLine();
PrintSummary(report);

if (debrief || speak)
{
    Console.WriteLine();
    Console.WriteLine("Post-lap debrief (each clean lap against the best passes up to it)");
    var coach = new PostLapCoach();
    LapDebrief? last = null;
    var all = session.AllAnalyses;
    for (var i = 0; i < all.Count; i++)
    {
        var a = all[i];
        if (!a.IsCleanLap)
        {
            continue;
        }

        // Same rules as live: every clean piece up to this lap, from any lap since the car last changed.
        var fromIndex = session.CarChangedAfterLap is { } changed && a.Lap.LapNumber > changed
            ? all.ToList().FindIndex(x => x.Lap.LapNumber > changed)
            : 0;
        var slice = all.Skip(fromIndex).Take(i + 1 - fromIndex).ToList();
        var passesSoFar = slice.SelectMany(x => x.Passes).ToList();
        var bestSoFar = BestPasses.Select(passesSoFar);
        var bestLapSoFar = slice.Where(x => x.IsCleanLap).Min(x => x.Lap.LapTime);
        var outsideSoFar = slice.Select(x => x.OutsideCornersTime).Where(t => t is not null).Min();
        var typesSoFar = passesSoFar.GroupBy(x => x.Corner.Name)
            .Select(g => (g.Key, Type: CornerTypes.Consensus(g, bestSoFar.GetValueOrDefault(g.Key))))
            .Where(x => x.Type is not null)
            .ToDictionary(x => x.Key, x => x.Type!.Value);
        last = coach.Debrief(a, bestSoFar, bestLapSoFar, typesSoFar) with
        {
            TheoreticalBestLapTime = BestPasses.TheoreticalLapTime(map, bestSoFar, outsideSoFar, BestPasses.RestOfStraight(passesSoFar)),
        };
        foreach (var l in DebriefText.Lines(last))
        {
            Console.WriteLine("  " + l);
        }

        Console.WriteLine($"    spoken: \"{DebriefText.Spoken(last)}\"");
    }

    if (speak && last is not null)
    {
        using var voice = new WindowsSpeech(voice: voiceName);
        var text = DebriefText.Spoken(last);
        Console.WriteLine($"Speaking with \"{voice.VoiceName}\" on the default audio output: {text}");
        voice.SayAndWait(text);
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
static int RunLive(string storeDir, bool speak, string? voiceName, SessionReportStore? reports)
{
    var store = new CornerMapStore(storeDir);
    var saver = reports is null ? null : new LiveReportSaver(reports);
    using var voice = speak ? new WindowsSpeech(voice: voiceName) : null;
    if (voice is not null)
    {
        Console.WriteLine($"Speaking with \"{voice.VoiceName}\" on the default audio output.");
    }

    using var connection = new IRacingConnection();
    CoachingSession? session = null;
    string? track = null;
    string? pendingTrack = null;
    string? trackName = null;
    IracingSessionInfo? sessionInfo = null;
    SessionReportInfo? reportInfo = null;
    var reportDue = false;

    connection.SessionInfoUpdated += (_, info) =>
    {
        sessionInfo = info;
        trackName = info.WeekendInfo?.TrackDisplayName ?? info.WeekendInfo?.TrackName;
        pendingTrack = info.WeekendInfo?.TrackName;
    };
    connection.Connected += (_, _) => Console.WriteLine("Connected to iRacing.");
    connection.Disconnected += (_, _) => Console.WriteLine("Disconnected from iRacing.");
    connection.TelemetryUpdated += (_, snapshot) =>
    {
        if (pendingTrack is { } t && t != track)
        {
            track = t;
            session = new CoachingSession(new CoachingOptions { TrackKey = t, Store = store, TurnNames = TrackTurnNames.Default });
            var started = DateTime.UtcNow;
            var driver = sessionInfo?.DriverInfo;
            reportInfo = new SessionReportInfo
            {
                Id = SessionReportStore.NewId(started, t),
                Source = "live",
                StartedUtc = started,
                TrackKey = t,
                TrackName = trackName ?? t,
                CarName = driver?.Drivers.FirstOrDefault(d => d.CarIdx == driver.DriverCarIdx)?.CarScreenName,
            };
            // Saved after every lap (once it has been measured, below), so the report is there even
            // if the coach is closed mid-session.
            session.LapRecorded += _ => reportDue = true;
            session.CornerMapChanged += m =>
            {
                Console.WriteLine($"Corners for {t}: {m.Corners.Count} ({session.CornerMapOrigin})");
                if (session.CornerMapOrigin == CornerMapOrigin.Consensus)
                {
                    voice?.Say("Corners learned.");
                }
            };

            // Say why a lap got no debrief when the driver might expect one.
            session.LapRecorded += lap =>
            {
                if (session.CornerMap is not null)
                {
                    return; // FlawedLapAnalyzed says it, with how many corners still count.
                }

                if (lap.IsClean || (lap.Flaws & (LapFlaw.Partial | LapFlaw.PitRoad | LapFlaw.StartLap | LapFlaw.NotOnTrack | LapFlaw.NotRacing)) != 0)
                {
                    return;
                }

                var why = lap.Flaws.HasFlag(LapFlaw.OffTrack) ? "off track" : lap.Flaws.HasFlag(LapFlaw.Incident) ? "incident" : null;
                if (why is not null)
                {
                    Console.WriteLine($"Lap {lap.LapNumber} not counted: {why}.");
                    voice?.Say($"Lap not counted, {why}.");
                }
            };
            session.CarChanged += (lap, drop) =>
            {
                var pct = Math.Round(drop * 100);
                Console.WriteLine($"Car slower on every straight since lap {lap} (top speed -{drop:P1}); comparing with laps from now on.");
                voice?.Say($"The car looks damaged, {pct:0} percent down on top speed. Comparing with laps from now on.");
            };
            session.FlawedLapAnalyzed += a =>
            {
                var why = a.Lap.Flaws.HasFlag(LapFlaw.OffTrack) ? "off track" : a.Lap.Flaws.HasFlag(LapFlaw.Incident) ? "incident" : null;
                if (why is null)
                {
                    return; // pit or start laps: nothing the driver needs to hear
                }

                var kept = a.Passes.Count(p => p.IsClean);
                var where = a.UncleanPieces().Select(u => u.Piece).FirstOrDefault();
                var said = kept == a.Passes.Count && where is not null
                    ? $"Lap not counted, {why} on the {where}. All corners still count."
                    : $"Lap not counted, {why}. {kept} of {a.Passes.Count} corners still count.";
                Console.WriteLine($"Lap {a.Lap.LapNumber}: {said} Not clean: {string.Join(", ", a.UncleanPieces().Select(u => $"{u.Piece} ({u.Flaws})"))}");
                voice?.Say(said);
            };
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
            // So the driver knows the coach is listening without looking at the screen.
            voice?.Say($"Coach ready, {trackName ?? t}.");
        }

        session?.Observe(snapshot);
        if (reportDue && session is { Laps.Count: > 0 } coached && saver is not null && reportInfo is not null)
        {
            reportDue = false;
            var type = sessionInfo?.SessionInfo?.Sessions.FirstOrDefault(s => s.SessionNum == coached.Laps[^1].SessionNum)?.SessionType;
            saver.Save(coached, reportInfo with { SessionType = string.IsNullOrWhiteSpace(type) ? null : type });
        }
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
    saver?.Flush();
    if (saver?.LastPath is { } saved)
    {
        Console.WriteLine($"Session report saved: {saved}");
    }

    return 0;
}

static void PrintSummary(SessionReport report)
{
    Console.WriteLine("Session summary");
    foreach (var line in SessionSummaryText.Headline(report))
    {
        Console.WriteLine("  " + line);
    }

    foreach (var note in report.Notes)
    {
        Console.WriteLine("  " + note);
    }

    if (report.Corners.Any(c => c.LossByPhase.Count > 0))
    {
        Console.WriteLine("  Where a normal lap loses time, per corner (s; median loss = approach / braking / turn-in to throttle / traction / straight), grip used best/typical, spread:");
        foreach (var c in report.Corners.Where(c => c.LossByPhase.Count > 0))
        {
            var phases = string.Join(" / ", c.LossByPhase.Select(p => p.Seconds.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)));
            static string Pct(double? v) => v is { } x ? $"{x * 100:0}%" : "-";
            var grip = c.Grip is { } g ? $"brake {Pct(g.BestBraking)}/{Pct(g.TypicalBraking)}, corner {Pct(g.BestCornering)}/{Pct(g.TypicalCornering)}" : "";
            var points = c.BrakePointSpreadM is { } b ? $", brake ±{b:0} m" : c.LiftPointSpreadM is { } l ? $", lift ±{l:0} m" : "";
            var spread = c.TimeSpread is { } ts ? $"±{ts:0.00} s" + points : "";
            var total = (c.MedianLoss ?? 0).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            Console.WriteLine($"    {c.Name,-18} {total} = {phases}   {grip}   {spread}   {c.DecidedBy}");
        }
    }

    if (report.Mistakes.Count > 0)
    {
        Console.WriteLine("  Recurring mistakes, costliest first:");
        foreach (var m in report.Mistakes)
        {
            Console.WriteLine($"    {m.CornerName}: {m.Problem} {m.Advice}.");
        }
    }
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

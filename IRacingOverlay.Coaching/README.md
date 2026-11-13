# IRacingOverlay.Coaching

Corner-by-corner driving coach. Phase 0 is the data foundation: it records laps, learns a track's
corners, and measures every clean lap corner by corner against the session's best pass through each
corner. Phase 1 turns that into a post-lap debrief: the corners that cost the most time and what to
change in each, written and spoken. There is no UI in the app yet; the command line tool runs it.

It is a separate assembly on purpose. It only reads from `IRacingOverlay.Sdk` (the public
`IrsdkParser`, `SessionInfoParser` and `TelemetrySnapshot`), and nothing in the app references it, so
it cannot change how the overlay behaves today.

## Pieces

| Type | Does |
| --- | --- |
| `Telemetry/TelemetryFrame` | The player-car channels the coach uses, from one tick. One shape for live and file data. |
| `Telemetry/SnapshotFrames` | Live: `TelemetrySnapshot` → frame, so the coach can subscribe to `IRacingConnection.TelemetryUpdated`. |
| `Ibt/IbtFile` | Reads iRacing `.ibt` disk telemetry (Alt+L) with the Sdk's own parsers: header, channels, session YAML, records. |
| `Laps/LapRecorder` | Frames → laps line to line (crossing interpolated between ticks), resampled to a 1 m grid, with clean-lap rules (pit road, off track, incident, non-racing states, start/out laps, gaps). |
| `Corners/CornerDetector` | Finds corners from one clean lap: speed valleys where the track bends and the driver braked or lifted; window from braking/lift to full throttle. |
| `Corners/CornerMapBuilder` | Agrees one corner map from several clean laps (corners found on at least 60% of them, median positions), so one odd lap can't split or merge a corner. |
| `Corners/CornerMapStore` | Saves each track's map as JSON, so T-numbers stay the same every session and corners can be given real names. |
| `Corners/CornerAnalyzer` | Per corner per lap: segment time, brake point, peak brake and ramp, trail-brake distance, minimum speed and where, throttle pick-up, full throttle, exit speed and speed 100 m later, ABS share, gear. |
| `CoachingSession` | Ties it together: best pass per corner, loss to best per lap, theoretical best lap. |
| `Advice/CornerDiagnoser` | Phase 1. One pass against the best pass through the same corner: braked early or late, softer or harder first hit, less trail brake, slower apex, later throttle, slower exit. Causes first, each with a size (metres, km/h, % of pedal) and a threshold so noise isn't reported. |
| `Advice/PostLapCoach` | After each clean lap: the worst corners above a 0.05 s noise floor (3 at most), their findings, and whether the same mistake came up on recent laps. `PostLapCoach.Attach(session, onDebrief)` wires it to a session. |
| `Advice/DebriefText` | The debrief in words: a full written version and a short spoken one (one fix per corner, two corners at most). All phrases in one place for later translation. |
| `Speech/WindowsSpeech` | Offline text-to-speech with the voices built into Windows (System.Speech). No network, a few MB of memory; a new debrief cuts off the last. |

## Running it on your own laps

Record a session with Alt+L in iRacing, then:

```powershell
dotnet run --project IRacingOverlay.Coaching.Cli -- "$env:USERPROFILE\Documents\iRacing\telemetry\<file>.ibt"
dotnet run --project IRacingOverlay.Coaching.Cli -- <file>.ibt --csv out   # also writes each clean lap's 1 m trace
```

```powershell
dotnet run --project IRacingOverlay.Coaching.Cli -- <file>.ibt --debrief          # Phase 1 debrief for every clean lap
dotnet run --project IRacingOverlay.Coaching.Cli -- <file>.ibt --debrief --speak  # ...and say the last one
dotnet run --project IRacingOverlay.Coaching.Cli -- --live --speak                # coach while you drive
```

`--live` is a **developer tool**: drivers use the app's live coaching switch on the Coaching page
(or the shortcut you can assign to it in the app's shortcuts), which runs the same coach. `--live` connects to iRacing the same
way the overlay does (its own connection, nothing in the app changes) and gives the debrief as you
cross the line after each clean lap; it is handy for watching the coach's log while changing it,
and it has not been tested as much as the app's switch.

Speech picks the first installed English voice unless `--voice "<name>"` names one;
`--list-voices` shows what is installed. It plays on the Windows default audio output.

Flags: `--relearn` agrees a new map instead of using the saved one, `--no-save` leaves saved maps
alone, `--store <dir>` uses another folder for them, `--corners-per-lap` shows what each clean lap
would give on its own, `--crossings` prints the raw channels around every line crossing.

## Corner ids and names

Corners are named from **Lovely Track Data** (named turns for 134 iRacing layouts, shipped in
`TrackData/iracing`; © Lovely Sim Racing, CC BY-NC-SA 4.0, see `TrackData/README.md`). A detected
corner takes the names of the turns it covers ("Eau Rouge and Raidillon"). Where a track has no
names, the debrief describes the corner instead ("Turn 5, the slow right").

The first time a track is seen, corners are agreed from up to five clean laps (live) or the five
fastest clean laps (CLI) and saved to
`%LOCALAPPDATA%\IRacingOverlay\coaching\corners\<track>.json`. From then on that map is used as is,
so T1 is always the same corner. Each corner has a `name` field, empty at first: write a name
(`"name": "La Source"`) and it is shown instead of the T-number in every table. Relearning a map keeps the
names of corners whose apex is within 50 m of where it was. Corners whose apexes are less than 80 m
apart (a chicane, Les Combes) are one corner.

Each file records the version of the corner detection that made it (`detectorVersion`). When
detection changes, older maps are relearned automatically on the next clean laps, keeping their names.

Each corner's type (flat, lift, fast braking, slow, complex) decides which checks run. It is agreed
from a corner's first five clean passes and, when at least four of them show the same type, saved
per track **and car** to `%LOCALAPPDATA%\IRacingOverlay\coaching\corner-types\<track>__<car>.json`,
so the corner keeps that type in every later session with that car and reports stay comparable. A
corner whose passes are split (Spoon 2: sometimes a lift, sometimes a brake) isn't saved and is
agreed each session; a saved type is agreed again when at least 80% of a later session's clean
passes show another, and forgotten when most of them disagree with it. To force a type, change its `type` and set `"fixed": true`; delete the file to
have every type agreed again.

It prints every lap with iRacing's own lap time beside the computed one, the corners found, each
clean lap's time per corner and the best pass per corner.

## Progress and focus plan

The Coaching page reads every saved session at the same track with the same car together
(`History/TrackHistory`): per corner, the best pass ever, what a normal lap loses now against in
the first sessions, and whether the recurring mistake there is going away. Corners are matched by
apex, so relearned maps still line up.

From that it proposes a **focus plan** (`History/FocusPlanner`): the one or two corners with the
most time to gain over the last three sessions, each with its recurring mistake and a target (half
the normal loss, e.g. "T1: brake 10 metres later, aim for 0.15 s"). Any corner can be pinned instead
from its detail ("Focus on this corner"), saved per track and car in
`%LOCALAPPDATA%\IRacingOverlay\coaching\focus`. With live coaching on, the debrief talks about the
focus corners first.

## Comparing laps

Every measured lap of a saved session keeps its channels on the 1 m grid (time, speed, pedals,
gear) beside the report, in `<sessions folder>\traces\<report id>\lap-NNN.bin.gz`, about 60 KB
a lap (`Reports/LapTraceStore`); a live session adds its laps as they are driven. The Coaching page
overlays any two of them, or a lap and the best lap ever for the track and car, for the whole lap
or one corner: speed, the time between them, pedals and gear. Sessions saved before this have no
laps kept; analyse their .ibt again to compare their laps.

## Checked against real files

| File | Laps | Computed vs iRacing lap time | Corners found |
| --- | --- | --- | --- |
| Audi R8 GT3, Spa | 17 | within ±2 ms on every flying lap | 11 (Bus Stop as two) |
| FIA F4, Tsukuba 2k Full | 32 | within ±1 ms on every flying lap | 5 |

Laps that start or end in the pit lane differ by up to ~50 ms, because iRacing times those from a
different point; they are never clean laps, so they never become references.

## Known limits (next steps)

- A corner straddling the start/finish line is cut at the line.
- `SnapshotFrames` has no unit test: `TelemetrySnapshot` can only be built inside the Sdk.

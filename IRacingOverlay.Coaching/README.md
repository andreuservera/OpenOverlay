# IRacingOverlay.Coaching

Corner-by-corner driving coach, Phase 0: the data foundation. It records laps, learns a track's
corners, and measures every clean lap corner by corner against the session's best pass through each
corner. It gives no advice yet and has no UI.

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

## Running it on your own laps

Record a session with Alt+L in iRacing, then:

```powershell
dotnet run --project IRacingOverlay.Coaching.Cli -- "$env:USERPROFILE\Documents\iRacing\telemetry\<file>.ibt"
dotnet run --project IRacingOverlay.Coaching.Cli -- <file>.ibt --csv out   # also writes each clean lap's 1 m trace
```

Flags: `--relearn` agrees a new map instead of using the saved one, `--no-save` leaves saved maps
alone, `--store <dir>` uses another folder for them, `--corners-per-lap` shows what each clean lap
would give on its own, `--crossings` prints the raw channels around every line crossing.

## Corner ids and names

The first time a track is seen, corners are agreed from up to five clean laps (live) or the five
fastest clean laps (CLI) and saved to
`%LOCALAPPDATA%\IRacingOverlay\coaching\corners\<track>.json`. From then on that map is used as is,
so T1 is always the same corner. Each corner has a `name` field, empty at first: write the real name
(`"name": "La Source"`) and it is shown instead of the T-number in every table. Relearning a map keeps the
names of corners whose apex is within 50 m of where it was. Corners whose apexes are less than 80 m
apart (a chicane, Les Combes) are one corner.

Each file records the version of the corner detection that made it (`detectorVersion`). When
detection changes, older maps are relearned automatically on the next clean laps, keeping their names.

It prints every lap with iRacing's own lap time beside the computed one, the corners found, each
clean lap's time per corner and the best pass per corner.

## Checked against real files

| File | Laps | Computed vs iRacing lap time | Corners found |
| --- | --- | --- | --- |
| Audi R8 GT3, Spa | 17 | within ±2 ms on every flying lap | 11 (Bus Stop as two) |
| FIA F4, Tsukuba 2k Full | 32 | within ±1 ms on every flying lap | 5 |

Laps that start or end in the pit lane differ by up to ~50 ms, because iRacing times those from a
different point; they are never clean laps, so they never become references.

## Known limits (next steps)

- A corner straddling the start/finish line is cut at the line.
- A lap is clean or not as a whole; one off-track excursion throws away its other corners too.
  Per-corner validity would keep them.
- `SnapshotFrames` has no unit test: `TelemetrySnapshot` can only be built inside the Sdk.

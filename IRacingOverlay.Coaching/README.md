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
| `Corners/CornerAnalyzer` | Per corner per lap: segment time, brake point, peak brake and ramp, trail-brake distance, minimum speed and where, throttle pick-up, full throttle, exit speed and speed 100 m later, ABS share, gear. |
| `CoachingSession` | Ties it together: best pass per corner, loss to best per lap, theoretical best lap. |

## Running it on your own laps

Record a session with Alt+L in iRacing, then:

```powershell
dotnet run --project IRacingOverlay.Coaching.Cli -- "$env:USERPROFILE\Documents\iRacing\telemetry\<file>.ibt"
dotnet run --project IRacingOverlay.Coaching.Cli -- <file>.ibt --csv out   # also writes each clean lap's 1 m trace
```

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
- Corners come from one lap; the CLI uses the best lap, a live session the first clean one.
- `SnapshotFrames` has no unit test: `TelemetrySnapshot` can only be built inside the Sdk.

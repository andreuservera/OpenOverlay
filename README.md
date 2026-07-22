<p align="center">
  <img src="IRacingOverlay.App/Assets/logo.png" width="128" height="128" alt="OpenOverlay logo">
</p>

<h1 align="center">OpenOverlay</h1>

<p align="center">
  A free, open-source telemetry overlay for iRacing — floating widgets on top of the sim, plus a
  full dashboard layout for a second monitor.
</p>

---

OpenOverlay reads iRacing's live telemetry straight from its shared-memory interface (the same
mechanism SimHub, CrewChief, and RaceLab use) and displays it as:

- **Floating widgets** — small, movable, click-through windows that sit on top of the game while
  you drive.
- **A fullscreen dashboard** — a fixed, all-in-one layout meant for a dedicated second monitor.

No telemetry ever leaves your machine — everything is read locally from iRacing's shared memory
and rendered directly by the app.

## Features

- **Cockpit cluster** — speed, gear, RPM, shift lights, ABS indicator, and left/right proximity
  bars for nearby cars.
- **Relative** — cars ahead/behind you on track, gap in seconds, class-colored.
- **Standings** — full running order with position, iRating, iRating delta, safety rating, lap,
  last/best lap time, and gap to leader; automatically grouped by class in multiclass sessions.
- **Delta bar** — live time delta vs. session best, personal best, or optimal lap.
- **Fuel calculator** — average consumption per lap (computed from your own fuel burn across
  completed laps, not a jumpy instantaneous rate), laps of fuel remaining, and whether you'll make
  it to the end of the session.
- **Flags** — current session flags (green, yellow, checkered, etc.).
- **Tire info** — tire temps and pressures.
- **Pedal trace** — a scrolling throttle/brake/clutch trace.
- **Incidents** — your own and your team's incident count.
- **Track info / Track map** — session/weather info bar and a schematic track map with live car
  markers.
- **Three dashboard themes** — Classic, Digital HUD, and Raw DIY.
- Every widget/panel is independently movable, resizable, and zoomable (in the dashboard).

## Requirements

- Windows 10/11 (64-bit).
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — only if you use the
  framework-dependent release build. The self-contained build needs nothing extra.
- [iRacing](https://www.iracing.com) installed. You must actually be in a session (practice,
  qualify, race, or replay) for live data to show — at the main menu there's simply nothing to
  read yet.

## Getting started

1. Download the latest release from the [Releases page](../../releases) and unzip it anywhere.
2. **Set iRacing's display mode to Borderless (or Windowed) — not exclusive Fullscreen.**
   This is the single most important setup step: exclusive Fullscreen mode takes full control of
   the display and won't let *any* other window, including OpenOverlay's widgets, render on top of
   it. In iRacing, go to **Options → Graphics → Display Mode** and pick **Borderless** (or
   **Windowed** if Borderless isn't available for your setup). Borderless is recommended since it
   still fills the screen edge-to-edge with no visible window chrome.
3. Launch `OpenOverlay.exe`. A small **Control Panel** window opens — this is where you turn
   widgets on/off, it also shows the connection status (red = waiting for iRacing, green = live).
4. Load into an iRacing session. The status dot turns green and every widget you've enabled starts
   showing live data automatically.

### Positioning widgets

- Check **"Edit layout (drag/resize widgets)"** in the Control Panel to unlock dragging/resizing.
  While unchecked, widgets are locked and click-through (mouse clicks pass straight to iRacing
  underneath — this is the mode you race in).
- Drag a widget by its body to move it; drag the bottom-right corner to resize it.
- Positions and sizes are saved automatically and restored next launch.

### Fullscreen dashboard (second monitor)

- Pick a monitor from the **"Dashboard monitor"** dropdown and click **"Show dashboard"**.
- Each panel on the dashboard has its own **+ / −** zoom controls (hover over a panel to reveal
  them).
- Pick a **Dashboard theme** from the Control Panel to restyle the whole dashboard at once.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/<your-org>/OpenOverlay.git
cd OpenOverlay
dotnet build
dotnet test
```

To produce an optimized Release build (what the Releases page ships):

```powershell
.\publish-release.ps1                      # framework-dependent, needs .NET 8 Desktop Runtime installed
.\publish-release.ps1 -SelfContained       # bundles the runtime, runs on a bare machine
```

The published executable lands at `publish\OpenOverlay.exe`.

### Project layout

| Project | Purpose |
|---|---|
| `IRacingOverlay.Sdk` | Dependency-free reader for iRacing's shared-memory telemetry interface (header/variable parsing, race-free buffer reads, session-info YAML). |
| `IRacingOverlay.App` | The WPF application: widgets, dashboard, tray/control panel, settings persistence. |
| `IRacingOverlay.Sdk.Tests` / `IRacingOverlay.App.Tests` | Unit tests — the SDK tests build a synthetic shared-memory buffer in-memory, so the whole suite runs without iRacing installed. |

(The project folders/namespaces above are still named `IRacingOverlay.*` internally — only the
published app itself is branded OpenOverlay.)

## Contributing

Contributions are very welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for how to get set up and
what to know before opening a PR.

## License

MIT — see [LICENSE](LICENSE). Use it, fork it, sell overlays built on top of it, whatever you like.

Third-party: [YamlDotNet](https://github.com/aaubry/YamlDotNet) (MIT), used to parse iRacing's
session-info YAML blob.

## Disclaimer

OpenOverlay is an unofficial, fan-made tool and is not affiliated with or endorsed by iRacing
Motorsport Simulations. It only reads the shared-memory telemetry interface iRacing itself
publishes for third-party tools (the same one SimHub, CrewChief, and every other overlay/dashboard
app uses) — it does not modify game files or memory in any way.

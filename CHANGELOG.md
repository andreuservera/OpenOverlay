## Changelog

All notable changes to OpenOverlay, newest first.

### [0.7.0] - 2026-09-30

#### Added
- New Weather widget with environmental temperature and rain probability forecast.
- Last Pit Stop tracking widget and telemetry.
- Configurable metric/imperial units throughout the application.
- New cockpit theme system:
  - Casual
  - Classic Car
  - GT Sports
  - Hypercar
  - Pit Wall
  - Invisible
  - Default

#### Improved
- Complete flag management redesign through the new FlagCatalog, FlagOptions and FlagPresenter systems.
- Widget styling and layout consistency refactor across all panels.
- SDK connection resilience improvements.
- Session information parser hardening.
- Connection health monitoring and tracking.

#### Added
- Global hotkey management.
- System tray icon integration.
- Restart overlays action directly from the tray menu.
- Single-instance enforcement to prevent multiple OpenOverlay processes.

#### Reliability
- Structured application logging with rolling log files.
- Global exception handler.
- Crash recovery mechanisms.
- UI watchdog monitoring.
- Run journal diagnostics.
- Diagnostics report generation.

---

### [0.6.0] - 2026-09-09

#### Improved
- Widget resize and layout system completely redesigned.
- Dynamic widget sizing.
- More accurate reference lap time calculations.

#### Added
- DriverTableOptions configuration for Relative and Standings visibility controls.

#### Improved
- Control Panel completely rewritten.
- New ControlPanel module.
- Schema-driven settings architecture.
- Live widget preview backdrop.
- Widget catalog management.
- Theme management system.

#### Refactored
- Major MainWindow.xaml and MainWindow.xaml.cs redesign replacing the previous menu implementation.

---

### [0.5.0] - 2026-08-28

#### Improved
- Complete UI refresh across all widgets.
- Consistent visual styling and layout improvements.
- Better usability and readability throughout the overlay suite.

---

### [0.4.0] - 2026-07-23

#### Improved
- Telemetry is processed faster: per-car data is read once per update and shared by every widget.
- The high-rate displays, the Cockpit bars and the Pedal trace, can refresh at up to 60 Hz.
- The Delta bar is solid green while you gain time and solid red while you lose it.
- The Control Panel status line shows update timings to help track down stutter.

#### Fixed
- New widgets follow "Edit layout" straight away before they have a saved position.
- The zoom buttons on floating widgets respond to clicks again.
- Relative gaps are correct in practice and qualifying sessions.
- The field no longer disappears during the player's first lap.
- Cockpit blinking and the pedal trace maintain the same speed at every refresh rate.
- Tire pressures now display garage-set values instead of remaining blank.
- Implausible tire temperature and pressure readings are ignored.

---

### [0.3.1] - 2026-07-23

#### Added
- Widgets can automatically hide when the driver is not actively controlling the car, including menus, garage screens, spectating and replay modes.

#### Changed
- "Hide in pit" functionality redesigned as "Hide outside car" behaviour.

#### Fixed
- Auto-hide logic now uses iRacing's IsOnTrack telemetry state for more accurate detection.

---

### [0.3.0] - 2026-07-23

#### Fixed
- Qualifying standings correctly rank drivers by their best lap time, including cars parked in their pit stalls.
- The dashboard keeps its fullscreen size when shown again after being hidden.

---

### [0.2.0] - 2026-07-22

#### Added
- Windows installer (OpenOverlay-win-Setup.exe).
- Desktop and Start Menu shortcuts.
- Add/Remove Programs integration.
- Automatic update system through Velopack.
- Installed copies automatically detect new releases and install updates on the next application launch.

---

### [0.1.0] - 2026-07-22

The first public release.

#### Added
- Floating widgets that read iRacing live telemetry:
  - Relative
  - Standings
  - Cockpit
  - Delta
  - Fuel
  - Flags
  - Tire Info
  - Pedal Trace
  - Incidents
  - Track Info
  - Track Map
- Fullscreen dashboard for a second monitor with three themes:
  - Classic
  - Digital HUD
  - Raw DIY
- Cockpit with speed, gear, RPM, shift lights, ABS indicator and spotter bars.
- Class-grouped standings for multiclass sessions.
- iRating, estimated iRating change and Safety Rating display.
- Purple highlight for session best lap.
- Delta comparison against session best, personal best and optimal lap.
- Multiple simultaneous flag displays.
- Per-panel dashboard zoom controls.
- Configurable Standings and Relative columns.
- Persistent widget visibility, sizing and dashboard preferences.
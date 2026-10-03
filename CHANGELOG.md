## Changelog

All notable changes to OpenOverlay, newest first.

### [0.9.0] - 2026-10-04

#### Added
- Layouts: save your widgets as presets, with their own position, size and settings, and switch between them in one click.
  - New Layouts page in the Control Panel to create, edit, rename, duplicate and delete layouts, with a preview of each one.
  - Canvas at the real resolution of the chosen monitor, with a zoom bar (fit, 100% and steps in between)
  - Drag widgets from the catalog onto the screen, or double-click them to add them
  - Each widget's own settings, independent of its individual setup
  - Layout selector in the Control Panel toolbar: pick a layout and switch it on; while one is open, picking another switches to it directly.
  - Export layouts to a file and import them, choosing the monitor they go on.

#### Changed
- Control Panel UI changes

---

### [0.8.0] - 2026-10-03

#### Added
- New ABOUT section in the Control Panel with What's New, Changelog and About OpenOverlay pages.
- What's New page showing the changes in the version you're running.
- Changelog page listing every release, newest first, with each version expandable.
- Current version always visible in the Control Panel status bar; clicking it opens About OpenOverlay.
- One-time notice after an update with the new version's main changes and a shortcut to What's New.
- About OpenOverlay page:
  - Version, build, release date and install date on this PC
  - Update channel and the result of the last update check
  - One-click copy of the version details for bug reports
  - Links to GitHub, the documentation, issue reporting and the license
- Clutch pedal trace added to the Pedal Trace widget for complete pedal input monitoring.
- Tire compound indicators.
- Build and update channel included in diagnostics reports.

#### Improved
- Relative gaps now follow each car's real pace around the lap, staying close to iRacing's own Relative, also in multiclass races and after pit stops.
- Relative shows live race positions while racing, so overtakes appear straight away instead of at the start/finish line.
- Standings works like a classic timing screen: the official order and gaps update at the start/finish line, while pit and penalty indicators stay live.
- Cockpit dashboard proximity detection improved, providing a more reliable view of nearby cars.
- Relative and Standings visuals redesigned for improved readability and quicker identification of important race information.
- Clearer handling of lapped cars, blue-flag situations and driver highlighting in Relative and Standings.
- Track & Session widget now displays lap progress as current lap versus total laps for easier race tracking.
- Penalty indicators redesigned with distinct visuals for black, furled black and meatball flags.
- General UI polish across all widgets with improved readability and visual consistency.
- Additional widget sizing options for greater flexibility when building custom layouts.
- Improved UI performance: OpenOverlay uses much less CPU and GPU with widgets open, leaving more frame rate for iRacing.
- Smoother Cockpit and Pedal Trace updates when many widgets are open.

#### Changed
- Builds take their version from the newest CHANGELOG.md entry instead of git tags.
- CHANGELOG.md is the single source for What's New, the Changelog page and the GitHub release notes.
- The release workflow stops when the version tag doesn't match the newest CHANGELOG.md entry.
- pack-installer.ps1 reads the version from CHANGELOG.md and no longer takes -Version.

---

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
  - Global hotkey management.
  - System tray icon integration.
  - Restart overlays action directly from the tray menu.
  - Single-instance enforcement to prevent multiple OpenOverlay processes.

#### Improved
- Complete flag management redesign through the new FlagCatalog, FlagOptions and FlagPresenter systems.
- Widget styling and layout consistency refactor across all panels.
- SDK connection resilience improvements.
- Session information parser hardening.
- Connection health monitoring and tracking.

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
- Control Panel completely rewritten.
- New ControlPanel module.
- Schema-driven settings architecture.
- Live widget preview backdrop.
- Widget catalog management.
- Theme management system.

#### Added
- DriverTableOptions configuration for Relative and Standings visibility controls.

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
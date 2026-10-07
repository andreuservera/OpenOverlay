## Changelog

All notable changes to OpenOverlay, newest first.

### [0.11.1] - 2026-10-07

#### Fixed
- Standings: in multiclass races, the estimated iRating change now counts only the cars in your class, as iRacing does.
- Closing the Control Panel now always quits the app, instead of sometimes leaving it running in the background with no window or tray icon.


### [0.11.0] - 2026-10-06

#### Added
- Relative and Standings: new optional columns for each driver's country flag, their car's brand logo and the places gained or lost since the start of the race.
- Cockpit rebuilt as a modular dashboard: choose and reorder its readouts — gear, speed, RPM, ABS, fuel, inputs, water and oil temperatures, brake bias, traction control, incidents and delta — and turn the shift lights and proximity radar on or off.

#### Changed
- Cockpit opacity now fades only its background, like every other widget, so its readouts stay fully readable.

#### Improved
- Relative and Standings are easier to read, with a new condensed font and a wider driver name column.

#### Removed
- Cockpit themes. Your cockpit, including the ones saved in layouts, starts from the new default dashboard: pick and order its modules again in the Cockpit settings.

#### Fixed
- Layout editor: at low zoom, the "OFF SCREEN" badge no longer stretches a widget past the edge of the canvas and keeps itself showing.


### [0.10.0] - 2026-10-05

#### Added
- Layout shortcuts: give each layout its own hotkey, and use new ones to open or close the selected layout, switch to the next or previous one, or edit it.
- Auto-save in the layout editor: every change is saved as you make it, so an open layout updates on screen while you edit.
- Relative and Standings: reorder or hide columns by dragging, and place session info — session type, SOF, laps, time, temperatures, humidity, brake bias and your incidents — in any corner above or below the table.
- Standings split by class can show each class's driver count and SOF in its title bar.
- Pedal trace can now show gear, speed and steering, and Pedal trace, Track & session and Fuel calculator let you choose and reorder what they show.
- Control Panel and layout editor sections can be folded, and stay as you left them.

#### Fixed
- Widgets near the bottom of the screen no longer shift up and overlap others when you open or switch layouts.


### [0.9.0] - 2026-10-04

#### Added
- Layouts: save your widgets as presets — each with its own position, size and settings — and switch between them in one click.
  - A new Layouts page in the Control Panel to create, edit, rename, duplicate and delete layouts, with a preview of each one.
  - A layout editor with a canvas at the real resolution of the chosen monitor and a zoom bar (fit, 100% and steps in between).
  - Add widgets by dragging them from the catalog onto the canvas, or by double-clicking them.
  - Move the selected widget with the arrow keys, one pixel at a time, or one grid step with Shift.
  - Every widget keeps its own settings inside each layout, without touching how it's set up on its own.
  - A layout selector in the Control Panel toolbar: pick a layout to switch it on; while one is open, picking another switches straight to it.
  - Export layouts to a file and import them, choosing the monitor they go on.
- Track & session: choose which fields to show — track name, session, temperatures, wind, humidity, track usage, time left and lap.
- Relative and Standings: show or hide the strength of field (SOF) and the column headers; with the headers hidden, the widget gets shorter to match.
- Weather: choose how often the wind compass refreshes, from 10 to 60 Hz.

#### Improved
- Weather redesigned as four cards — track temperature, air temperature, rain and wind — side by side or stacked.
  - The wind compass now shows your car, with an arrow pointing to where the wind hits it.
  - Humidity sits next to the air temperature, and the track surface state under the chance of rain.
- Track & session uses the same icons as Weather for air temperature, track temperature, wind and humidity.
- Track map redesigned, with clearer markers and support for multiclass races.
- Fuel calculator can now be shown vertically, and its status is easier to read at a glance.
- Tire widget redesigned for easier reading.
- Relative and Standings:
  - Driver names keep their original capitalisation, in a bolder font that stays easy to read.
  - New tire compound icon, iRating gains and losses shown as up and down chevrons, and redesigned PIT, last pit stop and penalty flag tags.
  - When a driver has two penalty flags, they now show stacked rather than squeezed side by side.
  - Cars a lap down keep their blue as bright as every other colour in the table.
  - The header shows the session type (RACE, QUALIFYING, PRACTICE) and the SOF in short form, e.g. "SOF 2.9k".
- Pedal trace is more compact, with each pedal's bar lined up with its graph.

#### Changed
- Widget opacity now fades only the background, so the data on top stays fully readable. The Cockpit, which has no separate background, still fades as a whole.
- The wind compass refresh rate has moved from General › Performance to the Weather widget's settings.
- In Relative and Standings, "Show category name" is now "Show session type" and shows the session instead of your class.

#### Removed
- The Fuel widget, now covered by the Fuel calculator. Layouts that included it simply leave it out, and the Dashboard shows the Fuel calculator in its place.
- The "Show session number" option from Relative and Standings.
- Redundant titles inside the Pedal trace, Relative, Standings and Track map widgets.


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
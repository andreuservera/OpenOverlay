# OpenOverlay — Product, Architecture & UX Audit

> Scope: full source review of `IRacingOverlay.Sdk`, `IRacingOverlay.App` and both test projects, the README
> screenshots, CI/release workflows and packaging scripts, plus a render of the Control Panel produced from the
> production XAML and view model.
>
> Evidence gathered during the audit:
>
> | Check | Result |
> |---|---|
> | Full Release rebuild (`--no-incremental`, nullable enabled) | **0 warnings, 0 errors** |
> | Automated tests | **294 / 294 passing** (281 App + 13 SDK) |
> | Code size | App ≈ 15.1k lines C#/XAML · SDK ≈ 0.7k · Tests ≈ 5.0k |
> | History | 49 commits (Jul 22 → Sep 29, 2026), tags `v0.1.0` → `v0.4.0`, 2 active contributors |
> | Product surface | 13 floating widgets, 1 fullscreen dashboard, 7 cockpit themes, 3 dashboard themes |

---

# Executive Summary

## Overall score: **6.3 / 10** — Semi-Professional

OpenOverlay has a **professional-grade core** (clean SDK, pure testable builders, a real design system, a
preview-driven control panel, an automated installer + auto-update pipeline) wrapped in **indie-grade product
operations** (no crash safety net, no logs, unsigned binaries, near-zero marketing). The fastest path to a
genuinely professional product is not new features — it is making the app **impossible to fail silently**, then
removing the three frictions users hit first (SmartScreen warning, metric-only units, no in-sim hotkeys).

### Scorecard

| # | Area | Score | Priority |
|---:|---|---:|---|
| 1 | Value Proposition & Goals | 7.0 | Medium |
| 2 | Features & Functionality | 7.0 | High |
| 3 | UX | 6.5 | High |
| 4 | UI | 8.0 | Medium |
| 5 | Window/Layout Design | 7.0 | Medium |
| 6 | Performance | 6.5 | High |
| 7 | Architecture | 7.0 | Medium |
| 8 | Code Quality | 7.5 | Medium |
| 9 | Accessibility | 5.0 | Medium |
| 10 | Configuration & Customization | 7.0 | High |
| 11 | Error Handling | 4.0 | **Critical** |
| 12 | Security | 6.5 | High |
| 13 | Installation & Deployment | 7.0 | High |
| 14 | Telemetry & Observability | 4.5 | High |
| 15 | Documentation | 7.0 | Medium |
| 16 | Marketing & Visibility | 3.0 | High |
| 17 | Competitive Differentiation | 5.0 | High |
| 18 | Future Scalability | 6.0 | Medium |
| 19 | Perceived Quality | 7.0 | High |
| 20 | Dedicated UI Audit | 7.5 | Medium |
| | **Overall (unweighted mean)** | **6.3** | |

### Pillar roll-up

| Pillar | Areas | Score | Read |
|---|---|---:|---|
| Product | 1, 2, 3, 10, 17, 19 | 6.6 | Deep where it exists; parity gaps users notice early |
| Design | 4, 5, 9, 20 | 6.9 | Strong visual system; accessibility and layout flexibility lag |
| Engineering | 6, 7, 8, 11, 18 | 6.2 | Clean core dragged down by missing failure handling |
| Delivery & Ops | 12, 13, 14, 15 | 6.3 | Great pipeline, but unsigned, no logs, runtime near EOL |
| Growth | 16 | 3.0 | Essentially no go-to-market |

## Top 5 strengths

1. **Real domain depth.** Timed-race fuel math, refuel/out-lap detection, class-aware gaps, practice/qualifying
   ranking that remembers parked cars, continuous running order, pit-stop timing, SOF, flag priorities — with the
   iRacing quirks behind each decision documented in code (Windows-1252 YAML, `CarLeftRight` typed as int,
   sentinel values).
2. **Clean, testable core.** Dependency-free SDK with race-free triple-buffer reads, parser separated from OS I/O,
   a synthetic shared-memory builder for tests, pure static builders — 294 green tests and a warning-free build.
3. **A genuine design system.** Type scale + fixed line grid, semantic state palette, four font roles on
   Bahnschrift (DIN) with tabular numerals; broadcast-quality tables and seven distinct cockpit themes.
4. **Preview-driven, schema-based Control Panel.** Pick → configure → see it, using the *production* panels with
   sample data; adding an option is one line in `ControlPanelSchema`.
5. **Real delivery pipeline and privacy stance.** Velopack per-user installer, silent delta updates, tag-triggered
   releases, CI on PRs; read-only shared memory and nothing leaves the machine.

## Top 5 weaknesses

1. **No crash safety net.** No global exception handlers; one exception in any builder or panel during the UI tick
   closes the app mid-race, and the telemetry thread dies permanently on any non-`IOException` (e.g. malformed
   session YAML) while the UI shows "WAITING FOR IRACING".
2. **Zero diagnostics.** No log file, no crash report, no version shown, no telemetry record/replay — bugs can only
   be reproduced "live" (the code comments say so repeatedly).
3. **Distribution trust and lifecycle.** Unsigned installer and executable with silent auto-update; .NET 8 reaches
   end of support on **Nov 10, 2026**; the shipped build skips ReadyToRun.
4. **Market-expected features missing.** Imperial units (only Weather has them), global hotkeys, tray mode,
   profiles, a standalone radar, a real track map, VR/streaming output.
5. **Near-zero go-to-market.** No landing page, video, badges, community channel or comparison — the real
   differentiators (open source, no account, local-only, polish) are not communicated.

---

# Detailed Evaluation

## 1. Value Proposition & Goals

| Score | Priority |
|---:|---|
| 7.0 / 10 | Medium |

**Strengths**
- Clear promise: a free, open-source iRacing overlay with floating click-through widgets plus a second-monitor dashboard.
- Privacy is explicit ("No telemetry ever leaves your machine") and backed by the code (read-only shared memory, no accounts).
- Single-sim focus enables depth (multiclass, iRating estimate, flags, fuel) that multi-sim tools rarely match.
- Honest positioning on safety: official SDK only, no memory writes, clear disclaimer.

**Weaknesses**
- The README pitches a feature list, not a reason to switch from RaceLab, iOverlay, Kapps, SimHub or irDashies.
- No target persona (league racer? endurance team? low-end PC? privacy-conscious?) and no stated product goals.
- The strongest differentiators (no account, MIT, local-only, "never clipped" widgets, live preview) are implicit.

**Risks**
- Perceived as "yet another free overlay" regardless of quality.
- Scope creep toward incumbent feature parity without a niche to defend.

**Recommended improvements**
- One-line positioning + three proof points at the top of the README (e.g. *"The privacy-first, no-account iRacing
  overlay — open source, native, built to be read at 250 km/h"*).
- Pick a beachhead: **multiclass, endurance and league racers** — where the fuel calculator, class-aware standings
  and pit-stop tracking already shine.
- Publish a short public roadmap (GitHub Projects or Discussions).

## 2. Features & Functionality

| Score | Priority |
|---:|---|
| 7.0 / 10 | High |

**Strengths**
- 13 widgets + dashboard; 7 cockpit themes; 3 dashboard themes; per-widget auto-hide when not driving.
- Fuel calculator supports timed races (clock ÷ lap time), refuel detection, out-lap baselines, rolling windows,
  margins, and usable tank capacity with series restrictions.
- Standings: continuous ordering (not frozen until the S/F line), class-leader gaps, best-lap ranking in
  practice/qualifying that survives cars parking, SOF via the published formula, last-pit-stop column.
- Flags: full bitfield decode, grouped priorities, hold timers, per-flag toggles.

**Weaknesses**
- **Multiclass iRating Δ is computed across the whole field**, not per class
  ([StandingsBuilder.cs#L422](IRacingOverlay.App/ViewModels/StandingsBuilder.cs#L422)); a slower-class leader
  "loses duels" to every faster-class car ahead overall.
- Track map is a one-dimensional bar with overlapping badges — no track outline.
- No standalone radar/spotter widget (proximity only exists inside the Cockpit).
- Metric only (km/h, kPa, L, °C) except Weather.
- The Tires widget's headline number is the **cold** (garage) pressure, and temps/wear refresh only in the pit stall
  on most cars ([TireInfoBuilder.cs](IRacingOverlay.App/ViewModels/TireInfoBuilder.cs)) — the UI doesn't say so,
  so users will read frozen numbers as live.
- After leaving a session, widgets keep rendering the last snapshot (frozen data) unless auto-hide is on.
- No lap/sector history, pit window, stint timer, hotkeys or profiles.

**Risks**
- Wrong or stale numbers erode trust faster than missing features.
- A feature-parity race against better-funded incumbents.

**Recommended improvements**
- Group by `CarClassID` before `EstimateIRatingDeltas` (per-class iRating).
- Label tire data ("COLD", "last pit: L24") and dim it while driving.
- Show a "Waiting for session" placeholder when disconnected instead of stale rows.
- Ship a Radar widget reusing `CockpitBuilder`'s proximity logic.
- Follow iRacing's `DisplayUnits` for all units (see §10).

## 3. UX

| Score | Priority |
|---:|---|
| 6.5 / 10 | High |

**Strengths**
- Three-pane Control Panel (pick → configure → preview) with realistic sample data, search by name *or* purpose,
  and three honest widget states (VISIBLE / HIDDEN / WAITING FOR CAR).
- "Hide when I'm not driving", one global "Edit layout" mode, widgets restored on launch, invisible auto-updates.
- Fixed five-step size ladder (hover −/+, Ctrl+wheel) that can never break a layout.

**Weaknesses**
- **No in-sim control:** no global hotkeys, no tray icon; the Control Panel must stay open (closing it quits the
  app) and changing anything mid-session requires Alt+Tab away from the sim.
- **No first-run onboarding:** the single most important setup step (Borderless display mode) lives only in the
  README; "WAITING FOR IRACING" doesn't tell the user what to do.
- No recovery tools (reset a widget, reset all, bring all widgets to the primary monitor).
- Some choices aren't remembered: Delta reference
  ([ControlPanelSchema.cs#L320](IRacingOverlay.App/ControlPanel/ControlPanelSchema.cs#L320)) and dashboard monitor
  ([ControlPanelSchema.cs#L483](IRacingOverlay.App/ControlPanel/ControlPanelSchema.cs#L483)); the dashboard isn't
  reopened on launch.
- The preview clips at L/XL (horizontal scrolling) instead of fitting the pane.
- No version, About, Help or "What's new" after silent updates.

**Risks**
- First-session failure ("overlay doesn't show" in exclusive fullscreen) → uninstall.
- Silent updates change behaviour with no release notes.

**Recommended improvements**
- Tray icon + global hotkeys (toggle overlays, toggle edit mode, show panel), start minimized.
- First-run checklist: detect iRacing, remind Borderless, offer a recommended widget preset.
- Persist every choice; add a "Fit" preview zoom; add an About page with version, changelog, "Open settings/logs
  folder" and "Reset".

## 4. UI

| Score | Priority |
|---:|---|
| 8.0 / 10 | Medium |

**Strengths**
- [DesignTokens.xaml](IRacingOverlay.App/Themes/DesignTokens.xaml): type scale + fixed line grid, semantic palette,
  surfaces; `StatePalette` mirrors it in code.
- Four font roles on Bahnschrift with tabular numerals — digits don't dance while values change.
- Broadcast-grade tables: class colour bands, licence chips, iRating trend arrows, purple/green lap colouring, PIT
  badge family, unmistakable player row.
- The Control Panel has its own isolated theme that cannot leak into the previews.

**Weaknesses**
- Colour strings hard-coded in view-models duplicate the tokens
  ([DriverRow.cs#L80](IRacingOverlay.App/ViewModels/DriverRow.cs#L80)).
- Fuel calculator paints LAPS LEFT, TO FINISH and REFUEL all red: no hierarchy, and "−6.1 L" is ambiguous.
- The Delta widget is a number on a colour wash; the README markets a "Delta bar"
  ([README.md#L61](README.md#L61)).
- Relative ships with 10 visible columns by default
  ([DriverTableOptionsStore.cs#L44](IRacingOverlay.App/Overlay/DriverTableOptionsStore.cs#L44)) — too dense for a
  glance widget.
- Tire temperatures aren't heat-coloured; the wear bar is unlabeled.

**Risks**
- Visual density slows glance reading at speed — the one job an overlay has.

**Recommended improvements**
- Lean Relative defaults (POS · CAR · DRIVER · iR/SR · GAP).
- Fuel hierarchy: REFUEL as the hero value; colour only the failing status; "SHORT 6.1 L" wording.
- Optional ±1 s delta bar; tire temperature colour scale; derive row colours from tokens.

## 5. Window/Layout Design

| Score | Priority |
|---:|---|
| 7.0 / 10 | Medium |

**Strengths**
- Borderless, transparent, topmost, click-through (`WS_EX_TRANSPARENT`) widgets that never steal focus
  (`ShowActivated = false`); edit-mode opacity floor so a 0 % widget can still be found.
- Native drag via `WM_NCLBUTTONDOWN`; clamping to the virtual desktop avoids the "widget migrates to monitor 2" bug.
- `SizeToContent` + `LayoutTransform` scaling: a frame can never be smaller than its content.

**Weaknesses**
- The dashboard is a fixed grid; the screenshot shows large dead regions and half-empty tables.
- No snapping, alignment guides or numeric positioning for widgets.
- No `app.manifest` declaring PerMonitorV2 DPI awareness → bitmap-scaled (blurry) windows on mixed-DPI rigs.
- Five discrete sizes can be coarse on 1440p/4K/ultrawide/triple-screen setups.
- Topmost isn't re-asserted; monitor hot-plug isn't handled beyond clamping.

**Risks**
- Widgets lost off-screen after display changes; the dashboard looks empty on large monitors.

**Recommended improvements**
- PerMonitorV2 manifest; 2–3 dashboard presets ("Timing", "Strategy", "Broadcast") with tables filling height;
  edge/widget snapping while dragging; "Reset positions".

## 6. Performance

| Score | Priority |
|---:|---|
| 6.5 / 10 | High |

**Strengths**
- One shared connection that waits on iRacing's data-ready event; per-snapshot array cache; Standings throttled to
  1 Hz; high-rate displays on a frame-synchronised loop with a user-selectable rate.
- Pooled track-map badges; cockpits drawn in `DrawingContext` with frozen, cached brushes and pens.
- Built-in diagnostics (tick avg/max, worst frame gap, GC generation counts).

**Weaknesses**
- `CompositionTarget.Rendering` is subscribed for the app's whole lifetime
  ([MainWindow.xaml.cs#L105](IRacingOverlay.App/MainWindow.xaml.cs#L105)) — WPF renders every display frame even
  at the main menu with iRacing closed.
- 13 separate `AllowsTransparency` (layered) windows
  ([OverlayWindowBase.cs#L34](IRacingOverlay.App/Overlay/OverlayWindowBase.cs#L34)) — the most expensive WPF
  window mode, running next to a GPU/CPU-hungry sim.
- Relative rows are replaced item-by-item at 10 Hz
  ([RelativePanel.xaml.cs#L39](IRacingOverlay.App/Widgets/RelativePanel.xaml.cs#L39)); each replace tears down and
  re-templates the row's visual tree.
- `BuildRelative` runs every tick even when neither Relative nor the dashboard is open
  ([MainWindow.xaml.cs#L209](IRacingOverlay.App/MainWindow.xaml.cs#L209)).
- Per-tick allocations: header/data byte arrays, LINQ in buffer selection, a new `SolidColorBrush` plus colour-string
  parsing per track-map marker ([TrackMapPanel.xaml.cs#L92](IRacingOverlay.App/Widgets/TrackMapPanel.xaml.cs#L92)).
- Synchronous disk writes on the UI thread for every setting change (the opacity slider writes a file per step).
- No performance budget or benchmark.

**Risks**
- Frame-time spikes in iRacing on mid-range CPUs; "the overlay costs me FPS" reviews; laptop battery/thermals.

**Recommended improvements**
- Subscribe to `Rendering` only while connected *and* a high-rate consumer is visible.
- Stable row view-models updated in place (`INotifyPropertyChanged`) instead of item replacement.
- Skip builders with no consumer; cache frozen brushes per class colour; debounce settings writes.
- Define and track a budget (e.g. CPU per tick, allocations per second) using the existing diagnostics.

## 7. Architecture

| Score | Priority |
|---:|---|
| 7.0 / 10 | Medium |

**Strengths**
- Two layers: a dependency-free SDK (header parsing, race-free reads, YAML) and the WPF app; the parser is
  isolated from OS I/O for testing.
- Poll model: a background reader publishes immutable snapshots, the UI thread pulls `Latest` — no cross-thread UI.
- Builders are pure static functions; stateful trackers (fuel, pit stops, best laps) are isolated classes.
- `WidgetCatalog` + `WidgetSlot` + schema-driven `SettingItem`s rendered by implicit DataTemplates.
- The preview reuses the production panels — no second implementation to drift.

**Weaknesses**
- `MainWindow` is both the Control Panel host and the telemetry orchestrator (~490 lines) and grows with each widget.
- Adding a widget touches ~8 places (catalog, factory, window, panel, options + store, schema, preview, loop).
- No telemetry source abstraction → no replay, no loop tests, no path to other sims.
- Twelve near-identical `*Store` classes writing twelve files, with no versioning or migration.
- Connection lifecycle: var headers are cached per shared-memory handle rather than per sim session
  ([IRacingConnection.cs#L120](IRacingOverlay.Sdk/IRacingConnection.cs#L120)).

**Risks**
- Linear growth of the orchestrator; subtle bugs after sim restarts; configuration sprawl.

**Recommended improvements (incremental, no rewrite)**
- Extract a `TelemetryLoop` from `MainWindow`.
- Introduce an `IWidgetModule` (descriptor + factory + builder + update) registered in the catalog.
- One versioned `SettingsStore` behind the existing static APIs.
- `ITelemetrySource` with `SharedMemorySource` and `RecordingSource`.

## 8. Code Quality

| Score | Priority |
|---:|---|
| 7.5 / 10 | Medium |

**Strengths**
- Nullable enabled, warning-free Release build, modern C# (records, collection expressions, pattern matching).
- 294 tests pinning tricky behaviour (qualifying, pit stops, fuel, flags, colours); synthetic memory builder.
- Comments explain *why* and preserve empirically confirmed iRacing behaviour — rare institutional knowledge.

**Weaknesses**
- Comment verbosity: many multi-paragraph "history" comments ("reported live", "an earlier version…") raise
  reading cost; history belongs in commits or decision records.
- Duplication: the store classes; the class-grouping logic in `GroupForDisplay` and `BuildMulticlassView`.
- Weak typing: `List<object>` for mixed row types.
- Exception-driven type probing on every tick (`DeltaBuilder`, `FlagBuilder`) instead of an SDK-level
  type-agnostic read.
- No `.editorconfig`, analyzers or format gate; very large files (`StandingsBuilder.cs` ~900 lines, its tests
  ~1,900 lines).

**Recommended improvements**
- `.editorconfig` + `dotnet format --verify-no-changes` in CI + `AnalysisLevel=latest-recommended` +
  `TreatWarningsAsErrors`.
- Move history out of comments into `docs/decisions/`; add `TryGetInt32Like`-style SDK reads; a generic store.

## 9. Accessibility

| Score | Priority |
|---:|---|
| 5.0 / 10 | Medium |

**Strengths**
- State is usually double-encoded: trend arrows, "PIT" text, flag names + descriptions, licence letter + colour,
  signed deltas.
- Overlay contrast is deliberate (muted text ≈ 6.5:1; ~95 % opaque panels over bright scenery); 5 sizes + opacity.

**Weaknesses**
- Control Panel eyebrow text `#5A646D`
  ([ControlPanelTheme.xaml#L40](IRacingOverlay.App/ControlPanel/ControlPanelTheme.xaml#L40)) measures ≈ 3.0:1 on the
  rail and ≈ 2.7:1 on the status pill at 10.5 px — below WCAG AA (4.5:1). It is used for the **connection status**,
  the most important state in the window.
- Small text overall in the Control Panel (hints at 11.5 px).
- Purple vs green lap colours and red/green delta rely on hue; no colour-blind palette.
- No `AutomationProperties` on icon-only/switch controls; no designed focus visuals; no high-contrast support.
- English-only, hard-coded strings (no resources) for a global community.

**Recommended improvements**
- Lift `Cp.TextFaint` to ≥ 4.5:1 and render the connection label in `Cp.Text`.
- Colour-blind-safe palette option (e.g. blue/orange); `AutomationProperties.Name` on switches and chips; a focus
  visual style; move strings to `.resx` (enables es, de, fr, pt-BR, it).

## 10. Configuration & Customization

| Score | Priority |
|---:|---|
| 7.0 / 10 | High |

**Strengths**
- Per-widget columns, blocks and elements; cockpit and dashboard themes; flag selection and hold time; fuel window
  and margins; weather units and sizes; per-widget size, opacity and auto-hide; high-rate refresh.

**Weaknesses**
- No global units (mph, °F, psi, gal) — weather only.
- No profiles (road/oval, per car/series, practice/race), no import/export/share, no reset to defaults.
- Delta reference and dashboard monitor aren't persisted (see §3).
- Settings spread over twelve files in `%LOCALAPPDATA%\IRacingOverlay` (folder name ≠ brand; legacy files from older
  builds never cleaned up).
- Dashboard layout isn't configurable.

**Recommended improvements**
- Units from iRacing's `DisplayUnits` variable with an override.
- Profiles that auto-switch by car/series/session type, with JSON export/import.
- "Reset widget" and "Reset all"; persist every option.

## 11. Error Handling

| Score | Priority |
|---:|---|
| 4.0 / 10 | **Critical** |

**Strengths**
- Defensive telemetry reads (`HasVariable` everywhere), plausibility bounds on tire data, sentinel handling for
  laps/time, tick-race retries on buffer reads, silent update-check failures, JSON parse errors don't block startup.

**Weaknesses**
- **No global handlers** (`DispatcherUnhandledException`, `AppDomain.UnhandledException`,
  `TaskScheduler.UnobservedTaskException`) in [App.xaml.cs](IRacingOverlay.App/App.xaml.cs). `UiTimer_Tick` has only
  `try/finally` ([MainWindow.xaml.cs#L139](IRacingOverlay.App/MainWindow.xaml.cs#L139)), so any exception in a
  builder or panel terminates the app mid-race without a message.
- **The SDK loop only catches `IOException`**
  ([IRacingConnection.cs#L89](IRacingOverlay.Sdk/IRacingConnection.cs#L89)). A `YamlException` from an unusual
  driver/team name (other SDK ports sanitize those fields before parsing) or an out-of-range read during a partially
  written header faults the background task: telemetry stops forever while the UI says "WAITING FOR IRACING".
- **Var headers aren't re-read when the sim reconnects** on the same shared-memory handle
  ([IRacingConnection.cs#L113](IRacingOverlay.Sdk/IRacingConnection.cs#L113)); the official irsdk client re-resolves
  per connection, so stale offsets are possible after changing cars without restarting the overlay (verify live).
- Store writes are non-atomic `File.WriteAllText` with no `IOException` handling
  ([WidgetLayoutStore.cs#L32](IRacingOverlay.App/Overlay/WidgetLayoutStore.cs#L32)); a corrupt file silently resets
  to empty and the next save **permanently overwrites** the user's layout.
- No user-facing error surface.

**Risks**
- The worst possible failure mode for this product: the overlay vanishes or freezes during a race, with no evidence.

**Recommended improvements**
1. Global handlers → log + non-modal notice; keep running.
2. Per-widget isolation: wrap each build/update pair; a failing widget shows "paused — see log", the others keep working.
3. SDK: catch-all with backoff and restart; reset var headers and the session-info counter on every
   disconnected → connected transition or header-layout change; pre-sanitize YAML name fields.
4. Stores: write temp file + `File.Replace` with `.bak`; restore from backup on parse failure; catch I/O errors.

## 12. Security

| Score | Priority |
|---:|---|
| 6.5 / 10 | High |

**Strengths**
- Least privilege: read-only memory-mapped file, per-user install without admin, no accounts, no PII, no outbound
  traffic except update checks.
- Typed YAML deserialization; text-only rendering (no HTML/script surface).
- The release workflow scopes its token to `contents: write`.

**Weaknesses**
- Unsigned executable and installer — SmartScreen/AV friction and no way for users to verify origin.
- Silent auto-update trusts whatever appears on GitHub Releases: a compromised maintainer account or workflow ships
  to every install.
- Supply chain: `vpk` installed unpinned ([release.yml#L36](.github/workflows/release.yml#L36)) while the app pins
  Velopack 1.2.0; actions pinned by tag, not SHA; CI uses default token permissions; no Dependabot; no `SECURITY.md`.

**Recommended improvements**
- Authenticode signing (SignPath Foundation is free for OSS; Azure Trusted Signing is low-cost).
- 2FA, protected `v*` tags, required reviews and an approval environment for releases.
- Pin `vpk` and actions (SHA); `permissions: contents: read` by default; Dependabot; `SECURITY.md`.

## 13. Installation & Deployment

| Score | Priority |
|---:|---|
| 7.0 / 10 | High |

**Strengths**
- Velopack `Setup.exe`: per-user, shortcuts, launches on completion; delta updates; portable package.
- Tag → test → publish → pack → upload, fully automated; CI builds and tests every PR.

**Weaknesses**
- **.NET 8 LTS support ends Nov 10, 2026** — no runtime security fixes after that; framework-dependent users must
  keep an EOL runtime.
- Unsigned → "Unknown publisher" on first run (the biggest install drop-off for small Windows apps).
- The shipped build doesn't use ReadyToRun ([release.yml#L33](.github/workflows/release.yml#L33)) although the local
  script does — slower cold start for real users.
- No in-app update notice or changelog; no winget/Scoop listing; no "start with Windows" or "launch with iRacing".
- Parallel local build paths (PowerShell script + untracked Bash script and Spanish guide) can drift.

**Recommended improvements**
- Retarget to `net10.0-windows` (LTS); add `-p:PublishReadyToRun=true` to the release; sign; add a winget manifest;
  show "What's new" after an update; optional autostart.

## 14. Telemetry & Observability

| Score | Priority |
|---:|---|
| 4.5 / 10 | High |

*Two halves: ingesting iRacing telemetry is strong; observing the app itself is weak.*

**Strengths**
- Ingestion: tick-verified triple-buffer reads, data-ready event, Windows-1252 YAML fix, centralized variable names
  with documented types and units.
- A live diagnostics line (working set, GC counts, tick avg/max, worst frame gap).

**Weaknesses**
- No log file, crash dump, support bundle or version display — every bug report arrives with zero evidence.
- No telemetry recorder/replayer; bugs are "reported live" and "confirmed live", i.e. reproducible only in a session.
- No opt-in usage data: no idea which widgets or themes matter.
- The diagnostics line is developer jargon shown to every user.

**Recommended improvements**
- `Microsoft.Extensions.Logging` + rolling file under `%LOCALAPPDATA%\OpenOverlay\logs`.
- "Report a problem" button that zips logs, settings and version.
- `ITelemetrySource` + recorder (header, var headers, YAML, N ticks) + replay mode for development and tests.
- Optional, off-by-default, anonymous crash reporting that keeps the privacy promise.

## 15. Documentation

| Score | Priority |
|---:|---|
| 7.0 / 10 | Medium |

**Strengths**
- README with screenshots rendered by the app itself, features, the crucial Borderless instruction, sizing/editing,
  build/test/release instructions, project layout and disclaimer.
- CONTRIBUTING with testing expectations and a provenance rule for telemetry claims; rich code comments.

**Weaknesses**
- No CHANGELOG or release notes (combined with silent updates).
- No troubleshooting/FAQ (overlay not visible, SmartScreen, VR, performance, "frozen" tires).
- No architecture guide or "adding a widget" guide — that checklist exists only outside the repo.
- Placeholder clone URL ([README.md#L132](README.md#L132)); "Delta bar" claim; no Control Panel screenshot.
- Mixed-language, untracked docs (`GENERAR-OPENOVERLAY-EXE.md`).

**Recommended improvements**
- `CHANGELOG.md` feeding release notes; `docs/ARCHITECTURE.md`; `docs/ADDING-A-WIDGET.md`; FAQ; per-widget notes
  (iRΔ is an estimate, fuel math, tire data limitations).

## 16. Marketing & Visibility

| Score | Priority |
|---:|---|
| 3.0 / 10 | High |

**Strengths**
- Attractive, honest screenshots; clear license; a name, logo and consistent brand.

**Weaknesses**
- No landing page, demo video/GIF, badges (CI, release, downloads, license), community channel, social proof or
  comparison; "OpenOverlay" is generic and doesn't contain "iRacing" (poor discoverability); no release
  announcements.

**Risks**
- A good product nobody finds.

**Recommended improvements**
- A 60–90 s demo video + GIF at the top of the README; a GitHub Pages landing page; badges.
- A "Why OpenOverlay" comparison table; GitHub Discussions or Discord.
- Launch and release posts in r/iRacing, the iRacing forums and sim-racing Discords; reach out to creators.
- A tagline that contains "iRacing overlay".

## 17. Competitive Differentiation

| Score | Priority |
|---:|---|
| 5.0 / 10 | High |

**Landscape:** RaceLab, iOverlay, Kapps and SimHub (free/freemium, large communities) and irDashies (open source).

**Where OpenOverlay wins**
- MIT license, no account, local-only data.
- Design quality and consistency; preview-driven configuration; never-clipped scalable widgets.
- Deep multiclass, fuel and qualifying logic; seven cockpit themes.

**Where it loses**
- No VR, no streaming/OBS output, no real track map, no radar, no hotkeys/profiles, metric-only, small community.

**Risks**
- Competing on breadth against incumbents with years of head start.

**Recommended improvements**
- Own a niche: **"the privacy-first, best-in-class multiclass & endurance overlay"** — fuel windows, stint and
  driver-swap timers, pit-loss estimates, class-aware everything.
- Close the three parity gaps users notice first: units, hotkeys, radar.

## 18. Future Scalability

| Score | Priority |
|---:|---|
| 6.0 / 10 | Medium |

**Strengths**
- Catalog/schema pattern, reusable SDK, pure builders, strong test base.

**Weaknesses**
- High cost per widget; no plugin model; no source abstraction (replay, other sims); unversioned settings sprawl;
  orchestrator growth; hard-coded strings; bus factor of two.

**Risks**
- Each new feature gets slower to ship and riskier to change.

**Recommended improvements**
- `IWidgetModule` registry; `ITelemetrySource`; a single versioned settings file with migrations; resources for
  strings; lightweight decision records. **Keep WPF** — it is the right tool for a Windows-only sim.

## 19. Perceived Quality

| Score | Priority |
|---:|---|
| 7.0 / 10 | High |

**Strengths**
- Screenshots and Control Panel look like a commercial product; the installer and silent updates feel professional;
  typography and colour semantics are consistent everywhere; previews are labelled "SAMPLE DATA".

**Weaknesses**
- The first touch is a SmartScreen warning; crashes happen without a message; tire numbers look frozen; widgets keep
  stale data after a session; no version or About; a corrupted file silently wipes the layout; developer diagnostics
  in the status bar.

**Risks**
- Users judge the whole product by its worst moment, which today is an unexplained disappearance mid-race.

**Recommended improvements**
- Sign the binaries; crash-proof the loops; label data freshness; add an About page; a friendly status bar.

## 20. Dedicated UI Audit

| Score | Priority |
|---:|---|
| 7.5 / 10 | Medium |

**What is excellent**
- Type scale with a fixed line grid; tabular numerals; condensed DIN faces that fit dense tables.
- Class colour bands and gradient headers; the player row highlight; one badge family (PIT, last pit, licence).
- Flag cards with icon + name + one-line instruction; wind compass relative to car heading.
- The three-pane Control Panel with rail switches, three-state labels and live previews.

**Findings**

| ID | Surface | Finding | Severity | Recommendation |
|---|---|---|---|---|
| U1 | Control Panel | Connection status rendered in faint eyebrow style (≈ 2.7:1) — the most important state is the least legible text | High | Primary text colour + coloured dot + "what to do" hint when waiting |
| U2 | Control Panel | Preview clips at L/XL; horizontal scrolling required | Medium | "Fit to pane" by default, "100 %" toggle |
| U3 | Control Panel | Dashboard/Performance pages leave the preview pane empty ("Nothing to preview") | Low | Dashboard thumbnail in the selected theme; a CPU/tick sparkline |
| U4 | Control Panel | Cockpit theme chosen from a text dropdown of seven names | Medium | Visual thumbnail gallery (renders already exist) |
| U5 | Control Panel | Hints wrap into narrow columns beside wide controls (e.g. Flags → Simulate) | Low | Stack the control under the label when it is wide |
| U6 | Control Panel | Status bar shows MB, GC generations and tick milliseconds | Low | Friendly status; move diagnostics to the Performance page |
| U7 | Control Panel | No version, About, Help or Reset | Medium | About page with version, changelog, folders, reset |
| U8 | Relative | 10 columns by default, same as Standings | Medium | Lean default column set for a glance widget |
| U9 | Fuel calculator | Three red values; "−6.1 L" is ambiguous | Medium | REFUEL as hero; "SHORT 6.1 L"; colour only the failing status |
| U10 | Delta | Numeric only (README promises a bar) | Low | Optional ±1 s bar; fix README wording |
| U11 | Tires | Cold pressure as the hero value; temps/wear refresh only in the pits; not labelled; no heat colours | Medium | "COLD" label, "last pit" stamp, temperature colour scale |
| U12 | Track map | 1-D bar; badges overlap in dense packs | Medium | Short term: lane offsets for overlaps; long term: track outline |
| U13 | Dashboard | Fixed grid leaves large empty regions | Medium | Layout presets; tables fill available height |
| U14 | All widgets | Stale data remains after leaving a session | Medium | "Waiting for session" placeholder when disconnected |
| U15 | Edit mode | No on-screen hint of gestures or how to exit | Low | One-time hint banner; Esc exits edit mode |
| U16 | Multi-monitor | No PerMonitorV2 → blurry on mixed-DPI setups | Medium | `app.manifest` with PerMonitorV2 |

---

# Priority Roadmap

Effort is relative: **Low** = contained change in a few files · **Medium** = a feature-sized change ·
**High** = a new subsystem.

## Quick Wins

*High impact / low effort.*

| # | Task | Impact | Effort | Reason |
|---|---|---|---|---|
| QW1 | Global exception handlers + per-widget `try/catch` in both update loops | High | Low | One exception currently closes the app mid-race |
| QW2 | Resilient SDK loop: catch-all + backoff; reset var headers and session counter on reconnect; sanitize YAML names | High | Low | Telemetry thread can die silently; stale offsets after sim restart |
| QW3 | Rolling log file + version in the toolbar + "Open logs folder" | High | Low | Turns "reported live" bugs into evidence |
| QW4 | Atomic settings writes (temp + replace + `.bak`) with I/O error handling | High | Low | Prevents silent, permanent loss of layouts |
| QW5 | Units from iRacing `DisplayUnits` (speed, temperature, pressure, fuel) | High | Low | Imperial users are a large share of iRacing |
| QW6 | Retarget to .NET 10 LTS; ReadyToRun in `release.yml`; pin `vpk` | High | Low | .NET 8 EOL on Nov 10, 2026; faster start; reproducible packaging |
| QW7 | Per-class iRating Δ | Medium | Low | Wrong numbers in multiclass erode trust |
| QW8 | Persist Delta reference and dashboard monitor; reopen dashboard on launch | Medium | Low | "It forgot my settings" feels like a bug |
| QW9 | Gate `CompositionTarget.Rendering`; skip builders with no consumer | Medium | Low | Idle CPU/GPU cost beside the sim |
| QW10 | Clear widgets on disconnect; label tire data "COLD / last pit" | Medium | Low | Stops presenting stale data as live |
| QW11 | Control Panel contrast fix + connection guidance text | Medium | Low | Fails WCAG AA on the most critical status |
| QW12 | Preview "Fit" zoom | Medium | Low | Preview clips at L/XL |
| QW13 | Lean Relative defaults; fuel colour hierarchy and wording | Medium | Low | Faster glance reading at speed |
| QW14 | README: badges, fix placeholder, Control Panel screenshot, FAQ, CHANGELOG | Medium | Low | Conversion and support deflection |
| QW15 | CI hygiene: `permissions: contents: read`, Dependabot, `SECURITY.md` | Medium | Low | Supply-chain basics |

## Next Improvements

*High impact / medium effort.*

| # | Task | Impact | Effort | Reason |
|---|---|---|---|---|
| NI1 | Tray icon + global hotkeys (toggle overlays, edit mode, show panel) + start minimized / autostart | High | Medium | In-sim control without Alt+Tab; standard in the category |
| NI2 | Code signing in the release pipeline (SignPath Foundation or Azure Trusted Signing) | High | Medium | Removes SmartScreen/AV friction; authenticates updates |
| NI3 | In-place row view-models for Relative/Standings; brush caching; debounced writes | High | Medium | Largest steady-state UI cost |
| NI4 | Telemetry recorder + replay via `ITelemetrySource` | High | Medium | Reproducible bugs, demo mode, loop-level tests |
| NI5 | Standalone Radar widget (reuse `CockpitBuilder` proximity) | High | Medium | Highly visible, safety-relevant parity feature; logic already exists |
| NI6 | Profiles (auto-switch by car/series/session type) + import/export | High | Medium | Top power-user expectation |
| NI7 | Single versioned settings file with migrations (replaces 12 stores) | Medium | Medium | Removes duplication; prerequisite for profiles |
| NI8 | PerMonitorV2 manifest + dashboard presets + drag snapping | Medium | Medium | Multi-monitor rigs; dead space |
| NI9 | First-run onboarding + About/What's-new page | Medium | Medium | Activation and retention |
| NI10 | Accessibility pass: colour-blind palette, focus visuals, `AutomationProperties` | Medium | Medium | Inclusivity and polish |
| NI11 | Launch kit: demo video, landing page, Discussions/Discord, community posts | High | Medium | Distribution is the bottleneck, not features |

## Strategic Improvements

*Long-term opportunities.*

| # | Task | Impact | Effort | Reason |
|---|---|---|---|---|
| SI1 | Endurance/team suite: stint timer, driver swap, pit-loss estimate, fuel windows, shared strategy | High | High | A defensible niche that builds on existing strengths |
| SI2 | Real track map from recorded outlines, cached per track and shareable | High | High | Most visible gap versus incumbents |
| SI3 | VR support (OpenKneeboard/OpenXR layer or SteamVR overlay) | High | High | VR drivers are currently excluded entirely |
| SI4 | Streaming output (local web server + browser-source overlays) | Medium | High | Streamer segment and free marketing |
| SI5 | Localization via resources (es, de, fr, pt-BR, it) | Medium | Medium | Global community; maintainers are Spanish-speaking |
| SI6 | `IWidgetModule` plugin model + extracted `TelemetryLoop` | Medium | Medium | Lower cost per widget; community contributions |
| SI7 | Opt-in, anonymous crash reporting and feature usage | Medium | Medium | Data-driven roadmap without breaking the privacy promise |
| SI8 | Sustainability: GitHub Sponsors/Ko-fi; supporter perks (extra themes) without paywalling core | Medium | Low | Funds signing, hosting and maintainer time |
| SI9 | Multi-sim via `ITelemetrySource` — only after the niche is won | Medium | High | Larger market, but dilutes focus |

---

# Top 10 Highest ROI Improvements

| Rank | Improvement | Impact | Effort | Why it ranks here |
|---:|---|---|---|---|
| 1 | **Crash-proofing bundle** (QW1 + QW2 + QW4) | Very High | Low | Removes the worst failure mode — the overlay dying or freezing mid-race — and protects user settings |
| 2 | **Logging + version + "Report a problem"** (QW3) | High | Low | Every future bug becomes diagnosable; multiplies the value of all other fixes |
| 3 | **.NET 10 LTS + ReadyToRun + pinned `vpk`** (QW6) | High | Low | Hard deadline (Nov 10, 2026) with a small, mechanical change |
| 4 | **Units from `DisplayUnits`** (QW5) | High | Low | Unlocks the largest user segment with zero new UI |
| 5 | **Trust fixes** — per-class iRating, persisted choices, stale-data handling, tire labels (QW7, QW8, QW10) | Medium-High | Low | Wrong or stale numbers cost more credibility than missing features |
| 6 | **Tray + global hotkeys** (NI1) | High | Medium | Fixes the daily workflow; parity with every competitor |
| 7 | **Code signing** (NI2) | High | Medium | Directly lifts install conversion and secures auto-updates |
| 8 | **Performance gating + in-place rows** (QW9 + NI3) | High | Low-Medium | "Costs no FPS" is a feature sim racers actively choose on |
| 9 | **Launch kit** — README polish, video, community posts (QW14 + NI11) | High | Low-Medium | The product is better than its visibility; distribution is the bottleneck |
| 10 | **Radar widget** (NI5) | High | Medium | Most-requested parity widget; the proximity logic is already written |

*Honourable mention:* telemetry record/replay (NI4) — the biggest multiplier for engineering velocity, just
below the line only because its payoff is internal.

---

# What I Would Personally Change

## What I would improve
- Make failure **loud, contained and recoverable**: global handlers, per-widget isolation, a self-healing telemetry
  loop, atomic settings, logs.
- Add a **telemetry recorder/replayer** so "reported live" becomes "replayed in a test".
- Close the **daily-use gaps**: hotkeys, tray, units, profiles, reset tools.
- Tighten **data honesty**: per-class iRating, freshness labels on tires, blank widgets when disconnected.
- **Sign, ship and tell people**: signing, .NET 10, changelog, demo video, one community post per release.
- Trim history-style comments into decision records so the code reads faster.

## What I would keep
- The SDK/App split, the parser/OS separation and the synthetic shared-memory tests.
- Pure builders and the testing discipline around them.
- The design tokens, Bahnschrift typography and semantic palette.
- The fixed scale ladder and its "never clipped" guarantee.
- The preview that renders production panels with sample data.
- The schema-driven settings model, the Velopack pipeline, and the privacy stance.

## What I would avoid changing
- **No rewrite** in Electron, web or Avalonia — WPF is the right tool for a Windows-only sim and the codebase is healthy.
- **No multi-sim push** until the iRacing niche is won.
- **No accounts, cloud sync or mandatory telemetry** — they would erase the clearest differentiator.
- **No return to free-form resizing** — the ladder solved a real class of bugs.
- **No heavyweight DI/MVVM framework migration** — incremental extraction (`TelemetryLoop`, `IWidgetModule`,
  `ITelemetrySource`) delivers the benefit without the churn.

---

# Final Verdict

| Question | Answer |
|---|---|
| Classification | **Semi-Professional** — professional-grade core engineering and design; indie-grade reliability operations, distribution and marketing |
| Would users pay for it? | **Not for a license today.** Strong free options exist and the MIT license makes paywalled code impractical. Users *would* donate for polish and privacy, and could pay later for **services** (team/endurance strategy sync, broadcast packages) — not for widgets |
| Would I continue development? | **Yes.** The foundation is unusually solid for a ten-week, two-person project, the critical gaps are cheap to close, and a clear niche (privacy-first multiclass/endurance) is available |
| Highest leverage improvement | **Make it impossible to fail silently**: crash-proof loops, a self-healing telemetry connection, atomic settings and a log file. It protects every race, every user and every future fix |

---

# Action Plan

1. **Stabilize (now, before the .NET 8 end of support on Nov 10, 2026):** global and per-widget exception handling,
   resilient SDK loop with header reset on reconnect, atomic settings writes, rolling logs + version display,
   retarget to .NET 10 with ReadyToRun and pinned `vpk`. Release `v0.5.0` with a CHANGELOG.
2. **Remove friction:** units from `DisplayUnits`, persisted choices, per-class iRating, stale-data and tire
   labels, contrast fix, preview "Fit", tray + global hotkeys, code signing.
3. **Multiply quality:** gate the render loop, in-place row updates, telemetry record/replay, single versioned
   settings file.
4. **Grow:** README polish + demo video + community launch, Radar widget, profiles, first-run onboarding.
5. **Decide the next bet with data:** endurance/team suite (recommended) versus VR/streaming reach — choose based
   on logs, feedback and download numbers gathered in steps 1–4.

**Success metrics to track:** crash-free sessions, installs per release, returning users, issues closed with
attached logs, and CPU cost per tick.

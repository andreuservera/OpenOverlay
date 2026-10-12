# OpenOverlay — Product, Architecture & UX Audit

> **Revision 2 — 2026-10-06** (code at `6a2c77e`, version 0.11.0). Re-scores the first audit
> (2026-09-29, v0.4.0) against the current code. Each area shows the previous score, the new one and what
> moved it. Reliability details live in [reliability_report.md](reliability_report.md) and performance details
> in [performance_report.md](performance_report.md).
>
> Evidence gathered for this revision:
>
> | Check | 2026-09-29 | 2026-10-06 |
> |---|---|---|
> | Full Release rebuild (`--no-incremental`, nullable enabled) | 0 warnings, 0 errors | **0 warnings, 0 errors** |
> | Automated tests | 294 passing (281 App + 13 SDK) | **973 / 973 passing** (920 App + 53 SDK) |
> | Code size (tracked C#/XAML) | App ≈ 15.1k · SDK ≈ 0.7k · Tests ≈ 5.0k | App ≈ 33.3k · SDK ≈ 1.5k · Tests ≈ 12.3k |
> | History | 49 commits, `v0.1.0` → `v0.4.0` | 157 commits, CHANGELOG up to **0.11.0** (7 versions in 2 weeks) |
> | Product surface | 13 widgets, 7 cockpit themes, 3 dashboard themes | 12 widgets (Fuel merged into Fuel calculator; Weather, Pedal trace, Incidents added), modular Cockpit, 3 dashboard themes, **Layouts** with editor |

---

# Executive Summary

## Overall score: **7.0 / 10** (was 6.3) — Semi-Professional, close to Professional

In one week the project closed the two weaknesses that defined the first audit: **it no longer fails
silently** (global and per-widget containment, self-healing reader, atomic settings, structured logs, run journal,
crash reports) and **it covers the daily-use gaps** (tray, global and per-layout hotkeys, metric/imperial units,
layouts with import/export, version/About/What's new). Feature depth and configurability are now well above
the free-tool average.

What did **not** move is everything outside the code: unsigned binaries, .NET 8 reaching end of support in
**35 days** (Nov 10, 2026), a README that no longer describes the product, no community or launch, and the
accessibility/contrast findings. Two new risks come from the pace itself: the orchestrator (`MainWindow`) has
grown from ~490 to ~1,240 lines, and 0.11.0 removed cockpit themes and **reset users' saved cockpits** in a
minor release.

### Scorecard

| # | Area | Before | Now | Δ | Priority |
|---:|---|---:|---:|---:|---|
| 1 | Value Proposition & Goals | 7.0 | 7.0 | = | Medium |
| 2 | Features & Functionality | 7.0 | 8.0 | +1.0 | Medium |
| 3 | UX | 6.5 | 7.5 | +1.0 | Medium |
| 4 | UI | 8.0 | 8.0 | = | Medium |
| 5 | Window/Layout Design | 7.0 | 8.0 | +1.0 | Low |
| 6 | Performance | 6.5 | 7.5 | +1.0 | Medium |
| 7 | Architecture | 7.0 | 7.0 | = | **High** |
| 8 | Code Quality | 7.5 | 7.5 | = | Medium |
| 9 | Accessibility | 5.0 | 5.0 | = | Medium |
| 10 | Configuration & Customization | 7.0 | 8.5 | +1.5 | Low |
| 11 | Error Handling | 4.0 | 8.0 | +4.0 | Low |
| 12 | Security | 6.5 | 6.5 | = | **High** |
| 13 | Installation & Deployment | 7.0 | 7.0 | = | **Critical** |
| 14 | Telemetry & Observability | 4.5 | 7.5 | +3.0 | Medium |
| 15 | Documentation | 7.0 | 6.5 | −0.5 | **High** |
| 16 | Marketing & Visibility | 3.0 | 3.0 | = | **High** |
| 17 | Competitive Differentiation | 5.0 | 6.0 | +1.0 | High |
| 18 | Future Scalability | 6.0 | 6.0 | = | High |
| 19 | Perceived Quality | 7.0 | 7.5 | +0.5 | High |
| 20 | Dedicated UI Audit | 7.5 | 7.5 | = | Medium |
| | **Overall (unweighted mean)** | **6.3** | **7.0** | **+0.7** | |

### Pillar roll-up

| Pillar | Areas | Before | Now | Read |
|---|---|---:|---:|---|
| Product | 1, 2, 3, 10, 17, 19 | 6.6 | 7.4 | Parity gaps closed; layouts are a real differentiator |
| Design | 4, 5, 9, 20 | 6.9 | 7.1 | Layout editor lifts it; contrast and accessibility unchanged |
| Engineering | 6, 7, 8, 11, 18 | 6.2 | 7.2 | Reliability solved; orchestrator growth is the new debt |
| Delivery & Ops | 12, 13, 14, 15 | 6.3 | 7.0 | Great observability; unsigned, EOL runtime, stale README |
| Growth | 16 | 3.0 | 3.0 | Still no go-to-market |

## Top 5 strengths

1. **Fails loudly, contained and recoverable.** Global handlers, per-widget circuit breakers, a reader that
   reconnects with backoff and survives malformed YAML, atomic settings with `.bak`, a UI watchdog, run journal,
   error references and diagnostics export (see [reliability_report.md](reliability_report.md)).
2. **Layouts.** Presets with per-widget position, size and settings, an editor at the monitor's real resolution
   (zoom, grid, nudge, drag from catalog), JSON import/export with a schema, per-layout hotkeys and auto-save.
   This is the power-user feature most competitors charge for or lack.
3. **Domain depth keeps growing.** Live race positions in Relative, timing-screen Standings, per-class fastest
   lap and SOF, places gained, penalties, incidents, pit stops, flags and car brands, weather with car-relative
   wind, fuel calculator with configurable cells.
4. **Clean, heavily tested core.** 973 green tests (3.3× the first audit), warning-free build, pure builders,
   synthetic shared-memory tests including stall/restart/corrupt-YAML scenarios.
5. **In-sim workflow.** Tray icon with close-to-tray, global hotkeys, single-instance activation, units that
   follow iRacing's `DisplayUnits`, version + What's new after silent updates.

## Top 5 weaknesses

1. **.NET 8 end of support on Nov 10, 2026** — still `net8.0-windows`, release still without ReadyToRun, `vpk`
   still unpinned. The only item with a hard deadline.
2. **Distribution trust.** Unsigned installer and executable with silent auto-update; no Dependabot,
   `SECURITY.md` or SHA-pinned actions.
3. **The README describes an older product.** No mention of layouts, hotkeys, tray, units or the new widgets;
   placeholder clone URL; "Delta bar" claim; no badges, video or community. The best features are invisible.
4. **Orchestrator and settings sprawl.** `MainWindow.xaml.cs` ≈ 1,240 lines; ~25 `*Store` classes; no
   `ITelemetrySource`, so no record/replay. Every feature now costs more than the last.
5. **Release discipline at this pace.** 7 versions in 2 weeks; 0.11.0 dropped cockpit themes and reset saved
   cockpits (also inside layouts) without a migration; the last commit fixes an "app closed" bug — regressions
   are reaching users between releases.

---

# Progress since the first audit

| Item (2026-09-29 roadmap) | Status | Evidence |
|---|---|---|
| QW1 Global handlers + per-widget isolation | **Done** | `Diagnostics/GlobalExceptionHandler.cs`, circuit breakers, reliability report §3 |
| QW2 Resilient SDK loop, header reset, YAML sanitising | **Done** | `IRacingConnection.cs`, SDK tests 13 → 53 |
| QW3 Rolling logs + version + open logs | **Done** | `AppLog`, `RollingFileSink`, `RunJournal`, `DiagnosticsReport`, version in status bar |
| QW4 Atomic settings writes | **Done** | `Overlay/SettingsFile.cs` (temp + `File.Replace` + `.bak`) |
| QW5 Units from `DisplayUnits` | **Done** | `ViewModels/Units.cs` with user override |
| QW6 .NET 10 + ReadyToRun + pinned `vpk` | **Open** | all projects `net8.0-windows`; `release.yml` `dotnet tool install -g vpk` |
| QW7 Per-class iRating Δ | **Done** (2026-10-06, after this revision) | `EstimateIRatingDeltas` rates each class as its own race; covered by a multiclass test |
| QW8 Persist Delta reference / dashboard monitor | Partial | `DeltaOptions` persisted and in layouts; dashboard monitor not verified |
| QW9 Gate the render loop, skip idle builders | **Done** | critical tick driven by telemetry, not `CompositionTarget.Rendering` |
| QW10 Clear on disconnect; tire freshness labels | Partial | disconnect reset done; tires still not labelled COLD / last pit |
| QW11 Control Panel contrast | **Open** | `Cp.TextFaint` still `#5A646D` |
| QW12 Preview "Fit" | Partial | Fit in the layout editor; widget preview still clips at L/XL |
| QW13 Lean Relative defaults; fuel hierarchy | Partial | columns reorderable/hideable, fuel status redesigned |
| QW14 README badges, placeholder, screenshot, FAQ, CHANGELOG | Partial | CHANGELOG + in-app changelog done; README not updated |
| QW15 CI hygiene | **Open** | no Dependabot, no `SECURITY.md`, CI without `permissions:` |
| NI1 Tray + global hotkeys | **Done** | `TrayIcon`, `GlobalHotkeyManager`, layout hotkeys; autostart missing |
| NI2 Code signing | **Open** | — |
| NI3 In-place rows, brush caching | **Done** | unchanged rows skipped, `CachedBrushConverter`, 0.8.0 perf work |
| NI4 Telemetry record/replay | **Open** | no `ITelemetrySource` |
| NI5 Standalone Radar widget | Partial | proximity radar is a Cockpit module, not its own widget |
| NI6 Profiles + import/export | Mostly done | Layouts; auto-switch by car/series/session missing |
| NI7 Single versioned settings | Partial | shared `SettingsFile`, versioned layout schema; still ~25 store files |
| NI8 PerMonitorV2 + dashboard presets + snapping | Partial | layout editor grid/nudge; no DPI manifest, fixed dashboard |
| NI9 Onboarding + About/What's new | Partial | About/What's new/update notice done; no first-run onboarding |
| NI10 Accessibility pass | **Open** | — |
| NI11 Launch kit | **Open** | — |

**Score:** 11 of 26 items done or mostly done, 10 partial, 9 open. Everything that lived inside the app moved;
almost everything outside it (runtime, signing, CI, README, marketing, accessibility) did not.

---

# Detailed Evaluation

## 1. Value Proposition & Goals — 7.0 → **7.0** · Medium

**Changed:** layouts and in-sim control make the "power user, privacy-first" pitch credible.
**Still open:** the README still lists features instead of a reason to switch; no persona, no public roadmap.
**Do next:** one-line positioning + three proof points at the top of the README; target multiclass, endurance and
league racers; publish a roadmap.

## 2. Features & Functionality — 7.0 → **8.0** · Medium

**Changed:** Weather (four cards, car-relative wind), Pedal trace with clutch/gear/speed/steering, Incidents,
modular Cockpit with radar and shift lights, live race positions in Relative, timing-screen Standings, country
flags, car brands, places gained, penalty flags, per-class fastest lap and SOF, configurable Track & session and
Fuel calculator cells, units everywhere.
**Still open:**
- ~~iRating Δ computed across the whole field in multiclass~~ — fixed: each class is now rated as its own race.
- Tire data still unlabelled (cold pressure as hero, temps/wear refresh only in the pits).
- Track map redesigned but still 1-D; radar only inside the Cockpit; no stint timer or pit window.
**Do next:** per-class iRating; tire freshness labels; extract the Cockpit radar module into a standalone widget.

## 3. UX — 6.5 → **7.5** · Medium

**Changed:** tray icon and close-to-tray, global hotkeys plus per-layout hotkeys, single-instance activation,
layout selector in the toolbar, collapsible sections that remember their state, About/What's new and a one-time
update notice, widgets reset on disconnect.
**Still open:** no first-run onboarding (Borderless reminder, recommended layout); widget preview still clips at
L/XL; no autostart; 0.11.0 reset users' cockpits on update — a migration or at least a notice is the UX
expectation for a silent updater.
**Do next:** first-run checklist with a starter layout; preview "Fit"; settings migrations whenever an option is
removed.

## 4. UI — 8.0 → **8.0** · Medium

**Changed:** widget redesigns (Weather, Tires, Track map, Fuel calculator, Relative/Standings), condensed
Barlow font for tables, flags and brand logos, background-only opacity so data stays readable.
**Still open:** 47 hard-coded colour literals in view-models; Control Panel faint text unchanged; more optional
columns raise the density risk for a glance widget; Delta still has no bar.
**Do next:** derive colours from tokens; keep Relative defaults lean as columns grow.

## 5. Window/Layout Design — 7.0 → **8.0** · Low

**Changed:** layout editor at real monitor resolution with zoom/fit, grid, pixel and grid nudging, stacking
order, off-screen badges, monitor targeting on import; widgets no longer shift when switching layouts.
**Still open:** no PerMonitorV2 manifest (blurry on mixed-DPI rigs); dashboard is still a fixed grid.
**Do next:** `app.manifest` with PerMonitorV2; dashboard as a layout or with presets.

## 6. Performance — 6.5 → **7.5** · Medium

**Changed:** critical tick driven by telemetry instead of `CompositionTarget.Rendering`; unchanged table rows
skipped; less WPF work in Relative/Standings/Tires; a repeatable measurement method (performance report §8).
**Still open:** every widget is still an `AllowsTransparency` layered window
([OverlayWindowBase.cs#L40](IRacingOverlay.App/Overlay/OverlayWindowBase.cs#L40)) — the dominant cost with many
widgets open; new columns (flags, logos) add render work; settings writes still on the UI thread; no budget
tracked in CI.
**Do next:** re-measure 0.11.0 with the same method; debounce settings writes; evaluate a single composited
surface per monitor (performance report §6.3).

## 7. Architecture — 7.0 → **7.0** · **High**

**Changed (+):** layout model with per-widget config codecs and a JSON schema; `CockpitModules` catalog; shared
`SettingsFile`; diagnostics as its own module.
**Changed (−):** `MainWindow.xaml.cs` grew from ~490 to **~1,240 lines**; `StandingsBuilder.cs` to ~1,120; the
number of `*Store` classes roughly doubled. Adding a widget still touches many places, now including the codecs.
**Do next:** extract `TelemetryLoop` from `MainWindow` before the next feature; `IWidgetModule` (descriptor +
factory + builder + codec); `ITelemetrySource`.

## 8. Code Quality — 7.5 → **7.5** · Medium

**Changed:** test count 294 → 973, including reader fault scenarios and layout schema validation; still
warning-free.
**Still open:** no `.editorconfig`, analyzers or format gate; very large files; exception-driven type probing in
`DeltaBuilder`/`FlagBuilder`; history-style comments.
**Do next:** `.editorconfig` + `AnalysisLevel=latest-recommended` + `TreatWarningsAsErrors`; SDK `TryGet*` reads.

## 9. Accessibility — 5.0 → **5.0** · Medium

**Unchanged:** `Cp.TextFaint` `#5A646D` (≈ 2.7–3.0:1) still used for status text; hue-only purple/green and
red/green; no `AutomationProperties`, focus visuals or `.resx`.
**Do next:** lift `Cp.TextFaint` to ≥ 4.5:1; colour-blind palette option; automation names on switches.

## 10. Configuration & Customization — 7.0 → **8.5** · Low

**Changed:** layouts act as profiles with import/export; column reorder/hide by drag; session-info placement in
any corner; reorderable cells in Fuel calculator, Pedal trace, Track & session and Cockpit; units with override;
hotkeys per action and per layout; auto-save.
**Still open:** no auto-switch by car/series/session type; no "reset widget to defaults"; dashboard not
configurable.
**Do next:** layout auto-switch rules; "Reset to defaults" per widget.

## 11. Error Handling — 4.0 → **8.0** · Low

**Changed:** everything in the first audit's critical list is implemented and tested — see
[reliability_report.md](reliability_report.md).
**Still open:** layout-pass exceptions can't be attributed to one widget; tracker state (fuel, pit stops) lost on
relaunch; regressions still ship (the "app closed" fix in `6a2c77e`).
**Do next:** run the reliability §11 fault-injection checklist before every release; persist tracker state per
subsession.

## 12. Security — 6.5 → **6.5** · **High**

**Unchanged:** unsigned binaries with silent auto-update; `vpk` unpinned; actions pinned by tag; CI without
`permissions:`; no Dependabot or `SECURITY.md`. Single-instance and local-only logs are good additions.
**Do next:** SignPath Foundation (free for OSS) or Azure Trusted Signing; pin `vpk` and actions by SHA;
`permissions: contents: read` in CI; Dependabot; `SECURITY.md`; protected `v*` tags.

## 13. Installation & Deployment — 7.0 → **7.0** · **Critical**

**Changed (+):** CHANGELOG-driven versioning, release blocked on tag/CHANGELOG mismatch, manual release trigger,
in-app What's new.
**Unchanged (−):** `net8.0-windows` with **35 days** to end of support; release without ReadyToRun; unsigned;
no autostart, winget or Scoop.
**Do next:** retarget to `net10.0-windows` and ship it before Nov 10, 2026; add `-p:PublishReadyToRun=true` to
`release.yml`; pin `vpk`.

## 14. Telemetry & Observability — 4.5 → **7.5** · Medium

**Changed:** structured JSON logs with run IDs, error references shown in the status bar and tray, activity
trail, run journal detecting unclean exits, per-run problem tally, diagnostics report and copyable version info.
**Still open:** no telemetry recorder/replay — bugs are still reproduced live; no in-app log viewer; no
performance counters in the log.
**Do next:** `ITelemetrySource` + recorder (header, var headers, YAML, N ticks) + replay mode.

## 15. Documentation — 7.0 → **6.5** · **High**

**Changed (+):** CHANGELOG as single source for What's new, in-app changelog and release notes.
**Changed (−):** the README has fallen behind five releases: no layouts, hotkeys, tray, units, Weather, Pedal
trace or Incidents; placeholder clone URL ([README.md#L133](README.md#L133)); "Delta bar"
([README.md#L59](README.md#L59)); no Control Panel or layout editor screenshot. No FAQ, architecture or
"adding a widget" guide.
**Do next:** README refresh with new screenshots; FAQ (Borderless, SmartScreen, tire data, performance);
`docs/ARCHITECTURE.md`.

## 16. Marketing & Visibility — 3.0 → **3.0** · **High**

**Unchanged:** no landing page, video, badges, community channel or launch posts. The product now has a strong
story (layouts, privacy, reliability) and nobody is telling it.
**Do next:** 60–90 s demo video built around layouts + hotkeys; badges; GitHub Discussions; r/iRacing post once
the README is refreshed and the build is signed.

## 17. Competitive Differentiation — 5.0 → **6.0** · High

**Changed:** units, hotkeys, tray, layouts with import/export and a radar (inside the Cockpit) close most of the
parity gaps listed in the first audit.
**Still open:** no VR, streaming/OBS output, track outline or standalone radar; small community.
**Do next:** own multiclass/endurance (stint timer, pit window, pit-loss estimate); shareable layouts as a
community hook.

## 18. Future Scalability — 6.0 → **6.0** · High

**Changed (+):** versioned layout schema with codecs; module catalog pattern in the Cockpit.
**Changed (−):** orchestrator growth and store sprawl offset it; bus factor unchanged.
**Do next:** `TelemetryLoop` + `IWidgetModule` + `ITelemetrySource`; one versioned settings document with
migrations.

## 19. Perceived Quality — 7.0 → **7.5** · High

**Changed (+):** no silent disappearances, version and About visible, update notice, stale data cleared on
disconnect, visual polish across every widget.
**Still open (−):** SmartScreen on first touch; saved cockpits reset by 0.11.0; tire numbers that look live but
are frozen; a release cadence that ships fixes for the previous release the next day.
**Do next:** sign; migrate settings on breaking changes; slow to a stabilisation release with the fault
checklist.

## 20. Dedicated UI Audit — 7.5 → **7.5** · Medium

| ID | Finding (first audit) | Status |
|---|---|---|
| U1 | Connection status in faint text (≈ 2.7:1) | **Open** |
| U2 | Widget preview clips at L/XL | **Open** (fit exists only in the layout editor) |
| U3 | Dashboard/Performance pages show "Nothing to preview" | Not re-verified |
| U4 | Cockpit theme chosen from a text dropdown | Obsolete — themes replaced by modules |
| U5 | Hints wrap beside wide controls | Not re-verified |
| U6 | Developer diagnostics in the status bar | Partial — version + health shown; diagnostics moved to General › Diagnostics |
| U7 | No version, About, Help or Reset | Mostly fixed — About/What's new; no Reset |
| U8 | Relative ships 10 columns by default | Partial — columns reorderable/hideable |
| U9 | Fuel calculator: three reds, "−6.1 L" | Partial — status redesigned |
| U10 | Delta numeric only, README promises a bar | **Open** |
| U11 | Tires: cold pressure, no freshness label, no heat colours | Partial — redesigned, still unlabelled |
| U12 | Track map 1-D, overlapping badges | Partial — redesigned with multiclass; still 1-D |
| U13 | Dashboard fixed grid with empty regions | **Open** |
| U14 | Stale data after leaving a session | **Fixed** |
| U15 | Edit mode has no gesture hint | Superseded by the layout editor |
| U16 | No PerMonitorV2 | **Open** |

New findings:

| ID | Surface | Finding | Severity | Recommendation |
|---|---|---|---|---|
| U17 | Cockpit | Updating to 0.11.0 resets the user's cockpit (also inside layouts) | Medium | Map old themes to equivalent module sets on first load |
| U18 | Relative/Standings | Flags, logos, places gained, incidents and info bands make a crowded default easy to reach | Low | Ship 2–3 column presets ("Lean", "Broadcast", "Full") |

---

# Priority Roadmap (updated)

Effort: **Low** = contained change in a few files · **Medium** = feature-sized · **High** = new subsystem.

## Now — before Nov 10, 2026

| # | Task | Impact | Effort | Reason |
|---|---|---|---|---|
| P1 | Retarget to `net10.0-windows`; ReadyToRun in `release.yml`; pin `vpk` | Very High | Low | .NET 8 support ends in 35 days; faster cold start; reproducible packaging |
| P2 | Stabilisation release: run the fault-injection checklist, freeze features for one cycle, add a settings migration for the removed cockpit themes | High | Low | Users are getting regressions and resets at a 7-releases-in-2-weeks pace |
| P3 | ~~Per-class iRating Δ~~ **Done** | Medium | Low | Each class is now rated as its own race |
| P4 | README refresh: layouts, hotkeys, tray, units, new widgets, screenshots, badges; fix placeholder URL and "Delta bar" | High | Low | The best features are undocumented; prerequisite for any launch |
| P5 | Control Panel contrast (`Cp.TextFaint` ≥ 4.5:1, status in primary text) | Medium | Low | Unchanged WCAG AA failure on the most important status |
| P6 | Tire freshness labels (COLD, "last pit: L24") | Medium | Low | Frozen numbers presented as live |
| P7 | CI hygiene: `permissions: contents: read`, Dependabot, `SECURITY.md`, SHA-pinned actions | Medium | Low | Supply-chain basics for a silent auto-updater |

## Next

| # | Task | Impact | Effort | Reason |
|---|---|---|---|---|
| N1 | Code signing (SignPath Foundation / Azure Trusted Signing) | High | Medium | Removes SmartScreen; authenticates updates |
| N2 | Extract `TelemetryLoop` from `MainWindow`; `IWidgetModule` registry | High | Medium | Orchestrator is 2.5× bigger than at the first audit; cost per feature is rising |
| N3 | `ITelemetrySource` + recorder/replay | High | Medium | Turns "reproduced live" into tests; enables demo mode for videos |
| N4 | Launch kit: demo video, Discussions/Discord, r/iRacing post, shareable layouts | High | Medium | Distribution is now the bottleneck, not features |
| N5 | Standalone Radar widget from the Cockpit radar module | High | Low-Medium | Logic exists; highly visible parity feature |
| N6 | Layout auto-switch by car/series/session type; "Reset to defaults" per widget | Medium | Medium | Completes profiles |
| N7 | First-run onboarding with a starter layout; preview "Fit"; autostart option | Medium | Medium | Activation; first-session failure in exclusive fullscreen |
| N8 | PerMonitorV2 manifest; dashboard as layout/presets | Medium | Medium | Mixed-DPI rigs; dashboard dead space |
| N9 | Re-measure performance on 0.11.0; debounce settings writes; evaluate one composited surface per monitor | Medium | Medium-High | Layered windows remain the main cost with many widgets |
| N10 | Accessibility: colour-blind palette, `AutomationProperties`, focus visuals | Medium | Medium | Unchanged since the first audit |
| N11 | `.editorconfig` + analyzers + format gate; SDK `TryGet*` reads | Medium | Low | Keeps quality as the codebase doubles |

## Strategic

| # | Task | Impact | Effort | Reason |
|---|---|---|---|---|
| S1 | Endurance/team suite: stint timer, pit window, pit-loss estimate, driver swap | High | High | Defensible niche on top of the fuel and multiclass work |
| S2 | Real track map from recorded outlines | High | High | Most visible remaining gap versus incumbents |
| S3 | VR support (OpenKneeboard/OpenXR or SteamVR overlay) | High | High | VR drivers still excluded |
| S4 | Streaming output (local web server + browser sources) | Medium | High | Streamer segment and free marketing |
| S5 | Localization via resources (es, de, fr, pt-BR, it) | Medium | Medium | Global community |
| S6 | Opt-in anonymous crash reporting | Medium | Medium | Field data without breaking the privacy promise |
| S7 | Sponsorship (GitHub Sponsors/Ko-fi) | Medium | Low | Funds signing and maintainer time |
| S8 | Multi-sim via `ITelemetrySource` — only after the niche is won | Medium | High | Larger market, dilutes focus |

---

# Top 10 Highest ROI Improvements (updated)

| Rank | Improvement | Impact | Effort | Why it ranks here |
|---:|---|---|---|---|
| 1 | **.NET 10 + ReadyToRun + pinned `vpk`** (P1) | Very High | Low | Hard deadline, mechanical change |
| 2 | **Stabilisation release + settings migrations** (P2) | High | Low | Protects the trust the reliability work just earned |
| 3 | **README refresh** (P4) | High | Low | Unlocks marketing; every new feature since 0.6 is undocumented |
| 4 | **Code signing** (N1) | High | Medium | First-touch friction and update authenticity |
| 5 | **Trust fixes** — per-class iRating, tire labels, contrast (P3, P5, P6) | Medium-High | Low | Cheap; the last visible correctness and legibility debts |
| 6 | **`TelemetryLoop` + `IWidgetModule`** (N2) | High | Medium | Keeps feature velocity from collapsing |
| 7 | **Record/replay** (N3) | High | Medium | Debugging multiplier and demo-video source |
| 8 | **Launch kit** (N4) | High | Medium | The product is now clearly ahead of its visibility |
| 9 | **Standalone Radar** (N5) | High | Low-Medium | Logic already written |
| 10 | **Layout auto-switch + reset to defaults** (N6) | Medium | Medium | Finishes the strongest differentiator |

---

# What I Would Personally Change

## What I would improve
- **Slow down for one release.** The code is moving faster than the release process can verify; one feature-free
  cycle with the fault checklist, a .NET 10 build and settings migrations pays for itself.
- **Pay the orchestrator debt now**, while `MainWindow` is 1,240 lines and not 2,500.
- **Ship the story:** README, signing, video, one community post. The product finally deserves it.
- Close the small honesty debts: per-class iRating, tire freshness, contrast.

## What I would keep
- The reliability architecture (containment, health model, run journal, atomic settings).
- Layouts and their schema/codecs — the best product decision since the first audit.
- The SDK/App split, pure builders and the testing discipline (973 tests).
- Design tokens, the typography system and the preview that renders production panels.
- The privacy stance and the Velopack pipeline.

## What I would avoid changing
- **No rewrite** in Electron, web or Avalonia.
- **No multi-sim push** until the iRacing niche is won.
- **No accounts, cloud sync or mandatory telemetry.**
- **No more removals without migrations** — a silent updater must never reset what a user built.
- **No heavyweight DI/MVVM framework** — incremental extraction is enough.

---

# Final Verdict

| Question | Answer |
|---|---|
| Classification | **Semi-Professional, close to Professional** — engineering, reliability and configurability are professional; distribution, documentation and go-to-market are still indie |
| Would users pay for it? | **Not for a license**, but layouts + reliability + privacy now justify donations and a supporter tier |
| Would I continue development? | **Yes** — with a stabilisation cycle first |
| Highest leverage improvement | **Ship a signed .NET 10 stabilisation release with a refreshed README**, then tell people about it |

---

# Action Plan

1. **Before Nov 10, 2026:** .NET 10 + ReadyToRun + pinned `vpk`; cockpit settings migration; per-class iRating;
   contrast; tire labels; CI hygiene. Release as a stabilisation version after the fault-injection checklist.
2. **Trust:** code signing; README refresh with screenshots of layouts and the layout editor; FAQ.
3. **Velocity:** `TelemetryLoop` + `IWidgetModule`; `ITelemetrySource` with record/replay; analyzers.
4. **Grow:** demo video, Discussions, community launch, standalone Radar, layout auto-switch, onboarding.
5. **Next bet:** endurance/team suite (recommended) versus VR/streaming — decided from logs, feedback and
   download numbers.

**Success metrics:** crash-free sessions (from the run journal), unclean exits per 100 runs, installs per
release, issues closed with a diagnostics report attached, CPU cost per tick with N widgets open.

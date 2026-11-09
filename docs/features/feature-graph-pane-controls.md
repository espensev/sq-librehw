# Feature Spec: Graph Pane Controls

**Status:** source implemented and fixture-verified (2026-11-09, uncommitted at verification); not a candidate or live behavior; defaults await operator confirmation
**Updated:** 2026-11-09
**Extends:** [graph lanes](feature-graph-lanes.md) (lanes, weights, per-lane value zoom and persistence stay owned there)
**Origin:** operator request 2026-11-09 with an annotated screenshot of the stacked Power / Temperature / Fan graph and its right-click menu

## Problem and motivation

With `Stacked Axes` on, each visible lane is a horizontal **pane** of the graph.
Today the pane's value zoom exists but is implicit: wheel over a pane zooms the
shared time window *and* that pane's values together, Shift + wheel zooms the
values only, and the right-click menu has no pane-specific entries. The time
window is one shared axis for every pane. Removing a line from the graph needs a
trip to the sensor tree.

The operator asked that:

- the value and the time scale be adjustable **independently for each pane**,
  with the controls easy to reach from the right-click menu;
- the mouse wheel zoom ("resize") each pane separately;
- graphs can be deleted by clicking a graph or through menu options.

## Goals

- A right-click inside a pane opens a menu whose first section targets that pane:
  value scale, time scale, height, and removal.
- Each pane can keep following the shared time window (default) or own an
  independent time window, labelled and gridded inside that pane.
- Wheel gestures zoom exactly one pane's value scale or one pane's time scale.
- A plotted line can be removed from the graph by right-clicking it, and every
  line of a pane can be removed from the pane menu.
- With no pane override, the plot model, layout, and persisted keys are the same
  as before this feature.

## Non-goals

- Dragging pane borders, reordering panes, or wheel-driven pane height.
  Height stays `1x | 2x | 3x` (now also in the pane menu).
- Per-pane time windows in overlay mode (`Stacked Axes` off). Overrides persist
  but are not applied while overlaid.
- Changes to the tracker look, the web dashboard, CSV, `data.json`, or the HTTP
  contract.
- Deleting sensor history. Removal only un-plots; re-checking the sensor restores
  the line with its retained history.

## Decisions pending operator confirmation

The operator was away when these were asked; each is a reversible default. The
pane time-zoom and menu-anchor defaults were changed on 2026-11-09 to meet the
operator's original requirement: "users can set the zoom level for each pane,
SEPARATELY, this is essential."

| Question | Default taken | Alternative offered |
|---|---|---|
| Own time window per pane? | Yes, as an opt-in override per pane; default `Follow Shared` keeps panes aligned | Shared time only, per-pane value controls |
| Plain wheel over a pane | Zooms only that pane's value scale; `Shift` + wheel stays a value alias | Keep upstream time + value wheel |
| Pane time zoom (`Ctrl` + wheel, zoom rectangle, pane `Time Scale` zoom) | Detaches the pane into its own window seeded from the shared window, so with a fixed shared window no other pane moves. Under `Time Axis > Auto` the shared range refits to the following panes' data, so they can shift. An overriding pane zooms its own window. Pan in a following pane still moves the shared window | Zoom the shared window while the pane follows |
| Anchor of menu `Time Scale > Zoom In / Zoom Out` | The newest edge stays fixed and only the oldest edge moves, so a live window stays live (0–3600 s zooms in to 0–2880 s) | The window centre |
| Ways to delete | Right-click a line; pane `Remove from Graph` submenu (each sensor, or all in pane with confirmation) | Click line + Delete key |
| Wheel changes pane height? | No | `Alt` + wheel steps height |

## User-visible behavior

### Pane hit-testing

- In stacked mode a point inside the plot area belongs to the visible lane whose
  value-axis band contains it; a point over the value-axis labels belongs to the
  lane whose band contains its Y.
- In overlay mode only the value-axis label tiers select a lane; the shared plot
  area selects no lane.
- A line is hit when the nearest rendered point of a plotted series (interpolated)
  is within 8 px × DPI scale of the pointer, searching the hit pane's series in
  stacked mode and all series in overlay mode.

### Right-click menu

When the menu is opened by right-clicking the graph (not from the ⚙ button), a
pane section is prepended. The rest of today's menu follows unchanged after a
separator.

```
Remove "<sensor>" from Graph          (only when a line is hit)
── <pane name> ──                     (disabled header)
Value Scale ▸  Zoom In | Zoom Out | ✓Auto Range | Set Range...
Time Scale  ▸  ✓Follow Shared | Custom (…) | ─ | 30 sec … 24 h | ─ | Zoom In | Zoom Out
Height      ▸  1x | 2x | 3x
Remove from Graph ▸  <each sensor in pane> | ─ | All N in This Pane...
Rename... | Remove Lane               (user lanes only, as in Lanes ▸)
────
Stacked Axes, Show Axes Labels, Grid Density, Time Axis, Value Axes, Lanes, Reset Graph View
```

- The section is rebuilt each time the menu opens, for the pointer position at
  that moment; the previous section's items are disposed then (not when the
  menu closes, which can precede the chosen item's click).
- In the overlay plot area a hit line still adds its `Remove "<sensor>" from
  Graph` item (and the separator), but no pane section. Over the value-axis
  labels the lane under the pointer gets its section in either mode.
- `<sensor>` is the series title (hardware name and sensor name), as in the
  tracker.
- `Value Scale > Zoom In / Zoom Out` scale the pane's value range by 1.25 about
  its centre. They respect `Value Axes > Enable Zoom` (disabled when off) and
  leave Auto Range like a wheel zoom does.
- `Auto Range` is the existing lane auto range. `Set Range...` opens a two-field
  minimum/maximum prompt prefilled with the visible range (`G6`, or round-trip
  `R` when the `G6` pair would not parse back as an increasing range; a NaN or
  infinite bound leaves its field empty); it accepts
  finite numbers with maximum > minimum in the current culture, rejects group
  separators (so `1.5` in a comma-decimal culture is an error, not 15), and
  keeps the dialog open with an inline message on invalid input. The fixed
  range applies even when `Value Axes > Enable Zoom` is off. Menu value
  changes, like wheel zoom, are written to the existing `plotPanel.Min/Max<key>`
  keys by the next settings save; `Auto Range` removes them immediately. The
  default `TimeSpan` pane is the exception: its keys are the shared window's
  (see Persistence), so neither applies to it.
- `Time Scale > Follow Shared` (default) clears the pane override. A preset
  sets the pane's own window ending at "now" and is shown checked while the
  pane's window equals it. An override that matches no preset (after a zoom, a
  pan, or a detach) shows a checked, disabled `Custom (<span>)` item right after
  `Follow Shared`, or `Custom (<span>, ending <age> ago)` when the window ends
  1 s or more before "now"; durations are compact and culture-invariant (`45 s`,
  `4 min 10 s`, `1 h 5 min`).
- `Time Scale > Zoom In / Zoom Out` scale the pane's window by 1.25 with its
  newest edge fixed, so a live window stays live (0–100 s zooms in to 0–80 s).
  The span never drops below the shared axis's minimum range (1 s) and the
  window never reaches back more than 24 h. A following pane is first seeded
  from the shared window's current ages, so the zoom detaches it and does not
  zoom the shared window. With a fixed shared window no other pane moves; under
  `Time Axis > Auto` the shared range refits to the following panes' data, so
  they can shift.
- `Time Scale` is disabled in overlay mode. With `Time Axis > Enable Zoom` off,
  `Follow Shared` and the presets stay usable (as the shared `Time Axis` presets
  are) and only `Zoom In / Zoom Out` are disabled.
- `Remove from Graph` un-plots the chosen sensor(s) through the same path as the
  sensor-tree checkbox. `All N in This Pane...` asks for confirmation. A user
  lane keeps its definition, weight, and zoom; it hides until a sensor returns.
  The submenu is disabled for a lane with no plotted sensor.
- The shared `Time Axis` menu gains `All Panes Follow Shared` (after the preset
  separator, before `Label Mode`; enabled while any override exists), which
  clears every pane override.
- `Graph > Lanes > <lane>` and the graph-local `Lanes > <lane>` submenus keep
  their entries and gain the same `Value Scale`, `Time Scale`, and
  `Remove from Graph` submenus (one builder serves both menus), so every pane
  control is also reachable from the menu bar and the ⚙ button.

### Per-pane time window

- A pane with an override owns a time axis bound to its series. Its window keeps
  a fixed age distance from "now" as the graph advances, like the shared window.
- Its vertical gridlines and tick labels are drawn only inside its band; labels
  sit on a semi-opaque (alpha 192) plot-background strip across the band bottom,
  drawn above the series. A label that would cross the band's left or right
  edge, or touch the previous label, is skipped rather than drawn half clipped
  or overprinted. The strip is omitted when the band is shorter than one label
  line. The shared time axis no longer paints
  gridlines inside overriding bands; it keeps its labels below the plot, each
  drawn once. The pane axis reserves no margin, so the plot area is unchanged.
- The pane axis copies the shared axis's colors, font size, tick spacing, grid
  density, label mode, `Time Axis > Enable Zoom` state, and `Show Axes Labels`,
  and follows later changes to them. With `Show Axes Labels` off the pane draws
  neither its labels nor their strip; its gridlines stay. Its window follows
  "now" whether or not zoom is enabled.
- Wheel and drag inside an overriding pane move only that pane's window. Inside
  a following pane, `Ctrl` + wheel and the zoom rectangle detach the pane: it
  gets its own window, seeded from the shared window and zoomed. With a fixed
  shared window no other pane moves; under `Time Axis > Auto` the shared range
  refits to the following panes' data, so they can shift (see Persistence). A
  pan (right-drag, `Alt` + left-drag) in a following pane still moves the
  shared window (see Follow-ups).
- The hover tracker shows the time of the hovered pane's axis in the active label
  mode (local time or elapsed). Local-time labels choose seconds, minutes, or a
  date prefix from the range and tick step of the axis being labelled.
- Overlay mode binds every series to the shared time axis; overrides return when
  stacked mode returns. A hidden lane's override is likewise dormant until the
  lane is visible again.

### Mouse gestures

| Gesture | Over a stacked pane | Over value-axis labels | Over time-axis labels | Overlay plot area |
|---|---|---|---|---|
| Wheel | pane value zoom at pointer | that lane's value zoom | shared time zoom | shared time zoom |
| `Shift` + wheel | pane value zoom (alias) | that lane's value zoom | nothing | nothing |
| `Ctrl` + wheel | pane time zoom at pointer (detaches a following pane) | nothing | shared time zoom | shared time zoom |
| Right-drag, `Alt` + left-drag | pan pane value + its time (own window; shared window while following) | as OxyPlot | as OxyPlot | as OxyPlot |
| Middle-drag, `Ctrl` + right-drag, `Ctrl` + `Alt` + left-drag | zoom rectangle on pane value + its time (detaches a following pane) | as OxyPlot | as OxyPlot | as OxyPlot |

Value zoom respects `Value Axes > Enable Zoom`; time zoom respects
`Time Axis > Enable Zoom`. A disabled or missed wheel gesture is consumed and
changes nothing; wheel factor is 1.2 per notch, anchored at the pointer value.
This supersedes the graph-lanes line "Plain wheel and Ctrl + wheel stay
unchanged". Other wheel modifier combinations have no binding and do nothing.

A detach computes the new window from the shared axis, which the pane followed
until then: `Ctrl` + wheel zooms the shared window's range about the time under
the pointer, and the zoom rectangle takes the time range it spans. The result is
converted to ages and clamped like a menu zoom (at least 1 s, no older than
24 h), and only that pane's series move to it.

OxyPlot lays an axis out only when it renders, but input can reach a pane
before the next paint: a fast wheel spin, a touchpad pinch (which arrives as
`Ctrl` + wheel), or queued input. A pane axis therefore lays itself out over the
plot area when it is created and after every change of its window, with the
formula the next render uses. The next wheel notch, pan, or zoom rectangle then
maps through the pane's current window instead of an unrendered axis.

The pan and zoom-rectangle gestures resolve the pane by geometry, because
OxyPlot's own hit test always returns the first horizontal (shared) axis. They
take their enable flag from the shared time axis before switching to the pane
axis; the pane axes mirror that flag, so the result is the same. In a following
pane the zoom rectangle hands its time range to the detach and lets OxyPlot zoom
only the value axis; it applies OxyPlot's own rule that a rectangle 10 px or less
on either side zooms nothing. With `Time Axis > Enable Zoom` off it zooms the
value axis only and does not detach.

OxyPlot's remaining default gestures are unchanged: keyboard pan, zoom, and
reset (arrows, `+`/`-`, PageUp/PageDown, `A`, Home) and the double-click
`ResetAt` act on every axis, pane windows included (a reset pane window is
recorded as its new age window, as for the shared window); `XButton1/2` zoom
and touch use OxyPlot's hit test and so act on the shared time window even
inside an overriding pane.

### Persistence

| Key | Value | Notes |
|---|---|---|
| `plotPanel.laneTime.<lane key>` | `ageMin:ageMax` seconds, invariant culture, round-trip (`R`) | absent = follow shared; malformed, negative, non-increasing, or older than 24 h (`ageMax` above 86400) values are ignored on load and removed on the next save; removed with its lane |

Menu changes (presets, `Time Scale` zoom, `Follow Shared`, `All Panes Follow
Shared`) and detaches (`Ctrl` + wheel or zoom rectangle in a following pane)
write or remove the key immediately. Pan and zoom inside an overriding pane
update the window in memory, and the next settings save writes it, as for the
shared window. Dormant overrides (overlay mode, hidden lane) are kept.

With `Time Axis > Auto`, the shared window is saved from its data range
(`plotPanel.MinTimeSpan` / `MaxTimeSpan`) only while some visible series is on
the shared axis. A detach takes that pane's series off the shared axis, so the
Auto range refits to the series that remain. When every visible pane overrides,
the shared axis has no data and the stored window is kept. The default `TimeSpan` lane's value-range keys
have the same names (upstream naming). The shared window owns them, as it did
before this feature, so that lane's range is never written or removed there
(settings save, `Auto Range`, `Value Axes > Autoscale All`, and the startup
auto-fit).

Value-range keys are unchanged (`plotPanel.Min/Max<key>`, graph-lanes spec).

## Affected surfaces

- `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs`: pane hit-testing, pane
  menu, gesture bindings, per-pane time axes, override persistence, sensor-removal
  request.
- New fork-only `UI/PlotTimeAxes.cs` (shared `PlotTimeAxis` with band
  exclusion, `PlotPaneTimeAxis` with its in-band label layer and between-paint
  layout, pane-aware pan and zoom-rectangle manipulators) and `UI/PlotRangePrompt.cs` (prompt,
  `TryParseRange`, and `FormatPrefill`).
- `PresentationSurfaceCoordinator.cs`, `WinFormsPresentationAdapters.cs`,
  `MainForm.cs`: forward the sensor-removal request to `SetSensorNodesPlot`.
- Settings: the key above. No API, log, or data-contract change.

## Compatibility and risk

- Upstream sync: `PlotPanel.cs` and `MainForm.cs` diverge further; new logic lives
  in fork-only files where practical. Recorded in the upstream-divergence ledger.
- OxyPlot 2.2: relies on public virtual `Axis.Render`/`Axis.Measure`, render-context
  clipping, `PlotManipulator.XAxis` in `PanManipulator`/`ZoomRectangleManipulator`,
  the above-series annotation layer, `GetAxesFromPoint` returning the first
  matching axis, and `ZoomRectangleManipulator` zooming only its Y axis when its
  X axis is null; the pane zoom rectangle mirrors that class's private rectangle,
  clamps, and 10 px threshold. The pane axis also mirrors the horizontal branch
  of the internal `Axis.UpdateTransform` through the protected `SetTransform` and
  `OnAxisChanged`. An OxyPlot upgrade must re-run the pane rendering and input
  tests and re-check both copies against the new source.
- Render cost: with overrides, the shared axis renders once per painted strip
  (the spike measured about +30% export time for one override at 1000x400).
- `net472` and `net10.0-windows`: no new framework APIs.
- DPI and text scale: pane labels use the axis font and `plotTextScale`; hit
  tolerance scales with DPI.
- Behavior change: plain wheel no longer zooms time inside a stacked pane. In
  overlay mode, plain wheel over the plot area zooms only the shared time
  window (it no longer zooms a value axis too), and `Ctrl` + wheel there zooms
  only the shared time window at the normal 1.2 step, no longer OxyPlot's fine
  zoom of both axes.
- Behavior change (2026-11-09): in a stacked pane that follows the shared
  window, `Ctrl` + wheel, the zoom rectangle, and `Time Scale > Zoom In / Zoom
  Out` give that pane its own window instead of moving the shared window, and
  menu zoom keeps the newest edge. `Follow Shared` (or `All Panes Follow
  Shared`) rejoins the shared window.

## Acceptance criteria

- [x] With no override, the model's axes, series axis keys, plot area, and
  existing plot tests are unchanged without test edits.
- [x] Plain wheel and Shift + wheel over a stacked pane change only that pane's
  value range; time and sibling ranges stay fixed.
- [x] Ctrl + wheel over a following pane detaches only that pane: with a fixed
  shared window the shared window and every other pane stay, and its new window
  is the shared span / 1.2 with the time under the pointer kept. Over an
  overriding pane it changes only that pane's window.
- [x] Menu `Time Scale > Zoom In / Zoom Out` keeps the newest edge (a live pane
  stays live) and never goes below 1 s or beyond 24 h; on a following pane it
  detaches the pane from the shared window's current window and does not zoom
  the shared window, which stays unchanged when fixed.
- [x] A zoom rectangle in a following pane detaches it with the rectangle's time
  range, zooms its value axis, and leaves a fixed shared window unchanged; below
  OxyPlot's 10 px threshold or with time zoom off it does not detach.
- [x] Under `Time Axis > Auto` the three detaches above take the pane's series
  off the shared axis, so the shared range refits to the following panes' data
  and they can shift; the detached pane keeps its window. (Fixture-tested for
  `Ctrl` + wheel; the menu and rectangle detaches rebind the series through the
  same `SetLaneTimeWindow` path.)
- [x] A detached pane maps a wheel notch, a pan, and a zoom rectangle that
  arrive before its first paint through its own window (no dropped notch, no
  jump to the oldest 24 h, no collapse), and its stored window and setting
  follow; its scale and offset before the next render match those after it
  (to 1e-9).
- [x] With `Time Axis > Enable Zoom` off, `Time Scale` stays enabled with
  `Follow Shared` and the presets usable; only `Zoom In / Zoom Out` are disabled.
- [x] An override that matches no preset shows a checked, disabled `Custom (…)`
  item after `Follow Shared`.
- [x] A stored pane window older than 24 h (for example `90000:100000`) is
  ignored on load, and the next save removes its key.
- [x] A pane time preset binds only that pane's series to its own axis; its
  window follows "now" across ticks, survives restart, and `Follow Shared` /
  `All Panes Follow Shared` return it to the shared axis and remove the key.
- [x] Shared gridlines are absent inside an overriding band and present in
  following bands; pane gridlines and labels are confined to their band; plot
  area is unchanged.
- [x] Right-drag in an overriding pane pans only its own time window.
- [x] Right-click inside a pane prepends that pane's section; from the ⚙ button
  or in overlay plot area no pane section appears; a hit line adds its remove item.
  (Section building and rebuild/dispose are fixture-tested; the `Opening` hook's
  ⚙ flag and pointer mapping are code-reviewed only.)
- [ ] Remove-from-graph requests un-plot exactly the chosen sensors; re-plotting
  restores them; lane definitions persist. (The request carries exactly the
  chosen sensors and is forwarded unchanged to MainForm, fixture-tested; MainForm's
  un-plot through `SetSensorNodesPlot` and the re-plot path are code-reviewed only,
  live check pending.)
- [x] Value Zoom In/Out, Auto Range, and Set Range affect only the pane and persist
  through the existing keys; invalid Set Range input is rejected. The prompt
  leaves a NaN or infinite bound empty and prefills round-trip text when `G6`
  would collapse the range.
- [x] Overlay mode ignores overrides and restores them in stacked mode.
- [x] Full x64 test filter and both Release builds pass; ledger gate passes.
- [ ] Operator confirms the defaults above and the live gestures after a
  separately authorized promotion.

## Follow-ups

Not part of this change; each needs its own decision or fix.

- Pan detaches: right-drag or `Alt` + left-drag in a following pane still pans
  the shared window. Detaching would need the pan manipulator to create the
  pane axis when the drag starts.
- Auto shared window and detach: under `Time Axis > Auto` a detach takes the
  pane's series off the shared axis, so the shared range refits and the other
  following panes can shift. Keeping them still would need Auto to keep the
  detached series' data range or to hold the range at the detach.
- A keyboard-opened context menu (menu key, `Shift` + `F10`) builds its pane
  section for the mouse pointer's position, not for a focused pane.
- Contrast of the disabled `── <pane> ──` header item in the dark theme (not
  checked).
- Pane label strip on short panes: a band shorter than one label line shows no
  strip and so no pane time labels.
- Order of the `Lanes > <lane>` entries (`Height`, `Auto Range`, `Value Scale`,
  `Time Scale`, `Remove from Graph`) differs from the pane section (`Value
  Scale`, `Time Scale`, `Height`, `Remove from Graph`).

## Verification plan

Fixture tests in `LibreHardwareMonitor.Tests.Application` render through the
real `PngExporter`, drive gestures through `view.ActualController`, and record
draw calls to check gridline and label confinement.

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
eng\ci\Test-LhmCiGates.ps1 -Filter Test-UpstreamDivergenceLedger
```

Manual, after a separately authorized promotion: right-click each of the Power,
Temperature, and Fan panes; zoom each pane's values with the wheel; give the Fan
pane a 1 h window while Temperature stays on the shared 10 min; remove one line by
right-clicking it and re-check it in the tree; restart and confirm the layout.

## Verification log

### 2026-11-09: source implementation (uncommitted working tree on `master` at `2ebadab2`)

- Tests were written first. The 21 wheel-table and no-override cases ran against
  the pre-change code and 12 failed (stock `ZoomWheel` changed time and value
  together, Ctrl fell through to `ZoomWheelFine`, an outside wheel was unhandled);
  the other new tests failed to compile until the API existed. A rendered check
  then showed overprinted pane labels at Fine density; the label-overlap assertion
  failed (2/2) before the thinning fix and passes after it.
- `PlotPanelPaneControlsTests` (71 cases) and the coordinator forwarding test pass.
  The feasibility spike file was removed; its plot-area, series-mapping,
  confinement, label, pan, wheel, and tracker facts are asserted against a real
  `PlotPanel`.
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-incremental`:
  0 warnings, 0 errors. The same with `-f net472`: 0 warnings, 0 errors.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64`:
  one clean run (Application 338 passed), but QA then saw the Application suite
  fail on 3 of 5 runs. Those counts are superseded by the review-fix entry below.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1 -Filter Test-UpstreamDivergenceLedger`:
  15 passed, 0 failed (new files staged so the tracked-tree checks see them).
- Rendered check, not kept in source: a temporary harness exported a real
  `PlotPanel` (Power, a user Power lane, Temperature, Fan) before and after giving
  the user lane 60 s and Fan 1 h. The overriding panes showed their own gridlines
  and in-band labels, the shared grid remained only in following panes, and the
  plot area was unchanged.
- Not verified: live gestures and menus in the running app (manual plan above,
  after a separately authorized promotion); DPI above 100% (hit tolerance and
  fonts scale by design but were not exercised).
- Observed and unchanged: at Fine grid density the shared time-axis labels below
  the plot can overprint at 1000 to 1200 px widths; this predates the feature and
  is visible in the before render.

### 2026-11-09: review fixes (still uncommitted, `master` at `2ebadab2`)

- Flaky wheel tests: the wheel check compared the anchor time with
  `Assert.Equal(expected, actual, 6)`, which rounds both sides to 6 decimals.
  Time values inherit "now"'s 100 ns (7th-decimal) granularity. When that digit
  is 5, the zoom's ~1e-13 floating-point noise decides which way each side
  rounds: `Math.Round(-299.9740425 - 1e-13, 6)` is `-299.974043` but
  `Math.Round(-299.9740425, 6)` is `-299.974042`. QA's failing values were
  `-299.9740425` and `-299.9786755`. The wheel check and the other comparisons
  of two independently computed, non-round doubles (pan, zoom rectangle, tracker)
  now use an absolute tolerance of `1e-6`. Nine PlotPanel-filter runs before the
  change did not reproduce the flake locally.
- Auto shared window: a new fixture test was red before the fix. With every
  visible pane overriding, it saved `(0, 1)` over a stored `(0, 300)`. Skipping
  the write was not enough: the test then read `(NaN, NaN)`, because the
  auto-range default `TimeSpan` lane's value-range keys are
  `plotPanel.Min/MaxTimeSpan`. Before the skip, the final window write always
  restored them; with it, nothing does in that case. They were removed at two
  sites: the lane loop in `SetCurrentSettings`, and `SetLaneAutoRange`, which the
  startup auto-fit (layouts of default lanes only), `Value Axes > Autoscale All`,
  and the `TimeSpan` pane's `Auto Range` reach. The first round guarded only the
  lane loop. After a removal at the second site, a save in that Auto state did not
  restore the keys, so the stored window was lost and the next start fell back to
  0 to 600 s. Both sites now skip those keys through one predicate
  (`SharesTimeWindowKeys`). The same test, extended rather than added, runs
  Autoscale All and `AutoRangeLane("TimeSpan")` after the first save and checks
  that `(0, 300)` is still stored. Before the second guard it failed after
  Autoscale All with `(NaN, NaN)`. A probe without Autoscale All failed the same
  way after `AutoRangeLane("TimeSpan")`.
- `Show Axes Labels`: a new recording-context test was red before the fix
  (pane label text drawn with the option off). It now covers a pane axis created
  before the option exists, toggling at runtime, and a pane created while off.
- After the second guard, PlotPanel filter (`FullyQualifiedName~PlotPanel`), 6
  consecutive runs: each 106 passed, 0 failed. `PlotPanelPaneControlsTests`: 73
  cases (unchanged count).
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64`:
  Contracts 124 passed; Library 193 passed; Application 340 passed and 1 established
  skip (`SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets`).
  The Application project then passed 3 more consecutive runs (340 passed each).
- Both Release builds (`net10.0-windows`, `net472`, `--no-incremental`): 0
  warnings, 0 errors. `Test-LhmCiGates.ps1 -Filter Test-UpstreamDivergenceLedger`:
  15 passed, 0 failed (the four new files re-staged, index only).

### 2026-11-09: separate pane time zoom (still uncommitted, `master` at `2ebadab2`)

Defaults changed to detach (see Decisions) because a following pane's time zoom
moved every other following pane.

- Tests first. With only a behavior-neutral `PlotRangePrompt.FormatPrefill`
  extraction (still `G6` for both fields) so the tests compiled, the PlotPanel
  filter ran `Failed: 23, Passed: 107, Total: 130`. The failures were the
  intended ones: Ctrl + wheel and the zoom rectangle in a following pane moved
  the shared axis (the rectangle cases read `(-419.99, -239.99)` against an
  unchanged `(-599.99, 0.01)`); menu zoom gave `(40, 120)` for `(30, 110)` and
  moved a live pane's `ageMin` to 10; `Time Scale` was disabled with time zoom
  off; no `Custom` item (a separator at index 1); `90000:100000` and `0:86400.5`
  loaded; and the prompt prefilled `NaN`, `Infinity`, and collapsed `G6` pairs.
  A new 24 h boundary test (pane zoomed out to the full day, saved, reloaded)
  was never red: a 100,000-sample probe found no `now - (now - 86400)` above
  86400, so the pane `AxisChanged` handler needed no clamp.
- After the change, `FullyQualifiedName~PlotPanel`, 5 consecutive runs: each
  131 passed, 0 failed. `PlotPanelPaneControlsTests`: 98 cases. The other
  `PlotPanel*Tests` files are unchanged from `HEAD`.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64`:
  Contracts 124 passed; Library 193 passed; Application 365 passed and 1
  established skip (`SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets`).
- Both Release builds (`net10.0-windows`, `net472`, `-p:Platform=x64
  --no-incremental`): 0 warnings, 0 errors.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1 -Filter Test-UpstreamDivergenceLedger`:
  15 passed, 0 failed (four new files re-staged, index only).
- Not verified: the live gestures and menus in the running app, and how the
  disabled `Custom` radio item renders in either theme.

### 2026-11-09: input before a detached pane's first paint, and the Auto exception (still uncommitted, `master` at `2ebadab2`)

- Review found that a pane axis created by a detach kept a zero scale until the
  next paint, while the next `Ctrl` + wheel, pan, or zoom rectangle already
  targeted it. `PlotPaneTimeAxis` now overrides `OnAxisChanged` to lay itself
  out over the plot area after every change of its window. This is the
  horizontal branch of OxyPlot's internal `UpdateTransform`, applied through the
  protected `SetTransform`. `SyncPaneTimeAxes` now adds a new pane to the model
  before applying its window, so its first layout has a plot area.
- Tests first. A new theory detaches a following pane with `Ctrl` + wheel and
  then, never rendering, runs a wheel notch, a zoom rectangle, and a right-drag
  pan in that cycle order, once from each starting gesture. After each gesture
  it checks the pane window, the stored window, and the saved key. The detach
  helper now also checks that the pane's scale and offset before the next
  render equal those after it. The unfixed code was simulated by disabling the
  layout; the reorder alone changes nothing. Against it, the PlotPanel filter ran
  `Failed: 5, Passed: 130, Total: 135`:
  - the second notch was dropped (`ActualMinimum` `-549.98` stayed, against
    `-508.31`);
  - the pan jumped to the oldest 24 h (`-86399.98` against `-605.13`);
  - the rectangle collapsed the window against its newest edge (`-0.99`
    against `-399.99`);
  - both detach-helper tests read scale `0` before the render and `1.813`
    after it.
- A mutant that laid the axis out only once, at creation, failed all three
  theory cases. OxyPlot's wheel zoom leaves the offset stale, and `Zoom` leaves
  both scale and offset stale. In the cycle order, each gesture follows one that
  leaves the transform stale.
- Auto exception: the spec said, without qualification, that a detach leaves the
  shared window and every other pane unchanged. Under `Time Axis > Auto` the
  shared range refits to the series left on the shared axis. The Decisions row,
  the menu and per-pane bullets, the Persistence note, and the criteria now state
  the exception, and a follow-up records it. A new pinning test gives the CPU
  lane 600 s of data and the GPU 120 s, selects Auto, and detaches the CPU pane.
  It checks that the shared span goes from about 600 s to 110–130 s and that the
  detached pane keeps its window. It pins current behavior and was never red.
- After the fix, `FullyQualifiedName~PlotPanel`, 3 consecutive runs: each 135
  passed, 0 failed. `PlotPanelPaneControlsTests` has 102 cases. The other
  `PlotPanel*Tests` files and `PlotLanesTests` are unchanged from `HEAD`.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64`:
  Contracts 124 passed; Library 193 passed; Application 369 passed and 1
  established skip (`SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets`).
- Both Release builds (`net10.0-windows`, `net472`, `-p:Platform=x64
  --no-incremental`): 0 warnings, 0 errors.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1 -Filter Test-UpstreamDivergenceLedger`:
  15 passed, 0 failed (four new files re-staged, index only).
- Not verified: a real wheel burst or touchpad pinch before `WM_PAINT` in the
  running app. The fixture drives the controller directly and never paints.

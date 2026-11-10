# Feature Spec: Graph Pane Controls

**Status:** wheel-axis and explicit-scope revision implemented and source-verified; the graph-menu revision (design, state text, glyphs, prompts) is specified below with its implementation in progress. Promotion and live acceptance of both revisions remain separate. Earlier controls were promoted to SND-DESK on 2026-11-10 at `1bf2655f`; existing remove-from-graph and restart checks remain open.
**Updated:** 2026-11-10
**Extends:** [graph lanes](feature-graph-lanes.md) (lanes, weights, per-lane value zoom and persistence stay owned there)
**Origin:** operator request 2026-11-09 with an annotated screenshot of the stacked Power / Temperature / Fan graph and its right-click menu

## Current source contract: wheel axis and target scope

The operator clarified the interaction on 2026-11-10: retain time zoom through
`Shift` + wheel, make the pane under the pointer the standard target, and offer
a separate selection to zoom all panes. This supersedes both the earlier
review suggestion to remove time zoom and the shipped Shift/Ctrl mapping
documented below. This section describes the source revision; the historical
runtime verification below does not verify its deployment.

| Gesture | `This pane` (default) | `All panes` (explicit selection) |
|---|---|---|
| Wheel | Zoom only the pointed pane's values | Zoom every visible pane's values in its own units |
| `Shift` + wheel | Zoom only the pointed pane's time window | Zoom all visible time windows |

- A gesture changes one axis dimension only. Ordinary wheel never changes
  time; Shift + wheel never changes values. Ctrl + wheel is no longer a time
  alias. Unassigned modifier combinations are consumed without zooming.
- Expose `Zoom scope > This pane | All panes` as mutually exclusive choices.
  Start each application session with `This pane`. Keep the selected scope
  readable in the menu parent and graph surface, especially while `All panes`
  is selected; closing the menu must not hide the active scope.
- Scope applies to wheel zoom and explicitly scope-labelled Zoom In/Out
  commands within the graph input area. Numeric Set Range, Auto Range, pane
  height, sensor removal, and Follow Shared remain pane-specific and must not
  silently become bulk actions.
- `This pane` resolves a stacked pane from its plot band or axis labels. A
  time-label hit belongs to its pane when unambiguous. Shared labels and empty
  overlay space must not silently expand a local gesture to all panes. A
  missed or ambiguous local target changes nothing. Keyboard invocation uses
  the focused pane, not an unrelated mouse position.
- Local time zoom detaches a following pane from a snapshot of its current
  visible window. Every sibling's visible window must remain unchanged,
  including with shared Auto time enabled. Detachment alone must not change
  the eligible plotted-data extent used by shared Auto; normal movement from
  new samples remains valid. The current refit exception is not acceptable.
- `All panes` means the currently visible panes. Capture ranges and targets
  before applying the gesture. Use one zoom factor, mapping the pointer's
  relative position into each target's own range; never reuse a watts range
  for RPM or degrees. For values, use the fraction within the pointed pane's
  height (centre if no pane supplies an anchor); for time, use the common
  horizontal fraction. Apply shared axes once and each active override once.
- Existing detached time windows remain independent: all-pane zoom transforms
  each current window proportionally instead of assigning one common span or
  endpoint. It is not `All Panes Follow Shared`. Hidden-lane overrides are
  not rewritten; hidden following panes naturally inherit the shared window
  when shown again. Overlay time zoom in All panes scope changes its shared
  axis once; existing dormant overrides remain intact.
- Existing zoom enable flags and range clamps still apply. Rectangle, keyboard,
  touch, and reset bindings need explicit review before implementation is
  accepted; zoom must not silently combine dimensions or bypass target scope.
  The scope selector does not automatically turn pan into an all-pane action.
  No extra replacement shortcuts are approved by this clarification.

Implementation binding review: retain right-drag / Alt+left-drag pane-aware
pan and left-button tracker gestures. Unbind rectangle zoom, auxiliary-button
zoom, keyboard zoom/pan/reset, double-click reset, shake reset, and touch
pan/zoom; these inherited commands cannot express the selected dimension and
scope. No replacement shortcuts are introduced. Apps / Shift+F10 opens the
pane menu for the last clicked or wheel-targeted visible pane; with no such
focus it opens only graph-wide options. Shared time labels resolve locally
only when exactly one visible pane follows that axis.

### Current-revision acceptance and verification

- [x] Wheel changes only values, Shift + wheel only time, with This pane as
  the startup default; Ctrl + wheel cannot retain its former time shortcut.
- [x] Local gestures leave all siblings unchanged in fixed/shared Auto modes,
  before and after the next paint, including repeated input without a render.
- [x] All panes changes every eligible visible target once; mixed units,
  unequal pane heights, independent time spans, and shared-axis deduplication
  use the same factor and normalized anchor. Hidden overrides survive.
- [x] The scope radio state and persistent scope label agree; selecting This
  pane restores local behavior. Reopening menus preserves session selection;
  reconstructing the panel from saved settings returns scope to This pane
  without discarding saved ranges. Installed-process restart remains pending.
- [x] Axis-label, shared-label, overlay, missed-target, disabled-zoom, keyboard
  context, and unassigned-modifier cases cannot accidentally zoom all panes.
- [x] Add targeted controller/settings regressions, review all remaining input
  bindings, and build net472 and net10.0-windows.
- [x] Inspect the persistent scope indicator in light/dark fixture renders at
  144 DPI with 100%, 150% and 200% font sizes. Both labels fit and remain legible.
- [ ] Check the installed menus and gestures, real monitor-DPI changes and
  restart behavior after separately authorized promotion.

### Source verification, 2026-11-10

- Reviewed and completed the working-tree draft over `f793f738`. The
  [continuation review](../reviews/review-2026-11-10-pane-zoom-continuation.md)
  records the findings and limits.
- Shared Auto includes every visible plotted history even when its series
  draws against a detached pane axis. Detach alone leaves siblings unchanged;
  removing a plotted history still refits Auto. Scoped zoom snapshots targets
  and ranges before applying one factor, deduplicating the shared axis.
- `PlotLineSeries.GetNearestPoint` skips unprepared points/axes for every
  caller, replacing `GetSensorAt`'s reflection guard. Controller mouse-down
  after `SetSensors` reproduced `ArgumentNullException` for plain, Ctrl and
  Shift left-click; all three cases now pass and tracking resumes after render.
- `FullyQualifiedName~PlotPanel`: 155 passed. Full deterministic suite:
  Application 389 passed and one established opt-in skip; Library 193 passed;
  Contracts 124 passed. Both Release frameworks: zero warnings or errors.
- Upstream-divergence ledger gate: 15 passed, zero failed. `git diff --check`
  passed before staging.
- Fixture evidence lives under the authoritative workload root at
  `LibreHW/audits/2026-11-10-pane-zoom-continuation`: failing `tracker-red.trx`,
  passing `plot-final.trx`, full-suite TRX files, and twelve scope-label PNGs.
  The external visual harness uses fake sensor histories and a hidden form;
  it does not launch the product or exercise hardware. Its text percentages
  are font sizes at 144 device DPI, not three different monitor-DPI settings.

## Current source contract: graph menus

Revision of 2026-11-10, after the operator asked for right-click menu design
work. It replaces the shipped right-click menu section below. The evidence is
the live `1bf2655f` menu captures and their review (workload root,
`LibreHW/audits/2026-11-10-menu-ux`) plus the
[Codex range review](../reviews/review-2026-11-10-codex-graph-pane-work.md).

### Problems

- The check glyph is a fixed 7 px shape at a fixed offset. It is tiny at 150%
  and sits near the top of the row.
- Radio items paint the OS-themed glyph, which is light on the dark menus.
  Other exclusive choices (Grid Density, Label Mode) use checkmarks, and the
  Time Axis presets show no current choice at all.
- Separators are painted as background, so groups run together.
- The pane header is a disabled item and looks disabled.
- A pane right-click appends the whole graph-wide menu, including `Lanes`,
  below the pane controls. `Lanes` repeats the pane controls with a second
  `Auto Range`.
- `Height` is offered in overlay mode, where weights are ignored.
- `Reset Graph View` clears every sensor's in-memory history. It does not
  reset the view.
- `Set Range...` clips its labels and buttons at the live scale and shows no
  unit. The lane-name prompt uses the same fixed-pixel layout.

### Goals and non-goals

- Each menu shows what it targets and the current state of each setting
  without opening submenus.
- A pane right-click leads with that pane's controls. Graph-wide settings sit
  behind one `Graph Options` submenu.
- Toggles, exclusive choices, separators, headers, hover and disabled states
  are distinct, DPI-scaled and vertically centred in Light, Dark and Black.
- No label misdescribes its command.
- Not in scope: restructuring the menu-bar `Graph` menu or merging it with
  the gear menu; the sensor-tree menu, apart from the shared renderer;
  gestures; settings keys; window-level effects such as rounded corners and
  shadows.

### Menus

Pane right-click: a stacked plot band, or a lane's value-axis labels in either
mode. A keyboard invocation targets the focused pane.

```
Power · W                                  header: lane name · axis unit (not selectable)
Remove "<sensor>" from Graph               only when a line is hit
──
Values                 Auto | 30 – 140 W ›  ● Auto Range | ● Fixed Range...  <bounds> | ── | Zoom In  <scope> | Zoom Out  <scope>
Time        Shared | 1 h | 4 min 10 s   ›  ● Follow Shared  <shared span> | ● Custom (…) | ── | ● 30 sec … 24 h | ── | Zoom In/Out  <scope>
Height                              1×  ›  stacked only: ● 1× | ● 2× | ● 3×
Remove from Graph                       ›  <each sensor> | ── | All N in This Pane...
Rename Lane... | Remove Lane...            user lanes only
──
Zoom Scope                   This pane  ›  ● This pane | ● All panes
Graph Options                           ›  the graph-wide list below, without Zoom Scope
```

Graph-wide list: the ⚙ button, a right-click with no pane target (the overlay
plot area, the margins), or a keyboard invocation with no focused pane. A hit
line still prepends its remove item and a separator.

```
Zoom Scope                   This pane  ›
──
✓ Stacked Axes
✓ Show Axes Labels
Grid Density                    Normal  ›  ● Off | ● Major | ● Normal | ● Fine
Time Axis                       10 min  ›  ✓ Enable Zoom | ── | ● Auto | ● 30 sec … 24 h | ── | All Panes Follow Shared | Label Mode ›
Value Axes                              ›  ✓ Enable Zoom | Autoscale All
Lanes                                   ›  per visible lane: Values, Time, Height, Remove from Graph, Rename/Remove Lane
──
Clear Graph History...
```

- Right-aligned grey text is the current state. `Values` shows `Auto` or the
  fixed bounds with the unit. `Time` shows `Shared`, the preset or the custom
  span. `Time Axis` shows `Auto`, the preset or the custom span. The scoped
  `Zoom In`/`Zoom Out` entries show `This pane` or `All panes` in the same
  column.
- `Fixed Range...` opens the range prompt, as `Set Range...` did. While the
  range is fixed it is checked and shows its bounds. `Auto Range` restores
  fitting.
- The `Time Axis` presets mark `Auto` or the preset that equals the shared
  window. A zoomed shared window marks none and shows its span on the parent.
- `Lanes` shows its submenu arrow before the first open. Its lane entries use
  the pane builder and order, with no separate `Auto Range`.
- One set of graph-wide items serves both layouts. Opening the menu moves
  them between the top level and `Graph Options`, so persisted options keep
  their state and handlers.
- `Clear Graph History...` replaces `Reset Graph View` in this list and in the
  menu-bar `Graph` menu. It asks first and defaults to No. Yes clears every
  sensor's in-memory history (the old behavior). Logged CSV files are not
  touched.

### Appearance

One fork-only renderer serves the menu bar, the sensor-tree menu and the graph
menus.

- A toggle draws a check; an exclusive choice draws a filled dot. Both are
  centred in the check column and scale with the row. Every `UserRadioGroup`
  and every pane choice is an exclusive choice.
- Hover and keyboard selection draw an inset rounded highlight
  (`MenuHighlightColor`), visible in all three themes.
- Disabled text, glyphs and arrows use `MenuDisabledForegroundColor`. A header
  is a non-selectable label in semibold full-contrast text, so it never reads
  as disabled.
- Separators are a 1-px inset line (`MenuSeparatorColor`). Submenu arrows are
  chevrons. Dropdown borders use `MenuBorderColor`, which is now a blend that
  stays visible on Black.
- New theme colors default to blends of the menu background and foreground,
  so a theme needs no new overrides.

### Range and lane-name prompts

- Both prompts lay out from their content: labels, fields, a wrapping
  validation line and buttons. Nothing clips at 100–200% fonts. Range labels
  carry the unit (`Minimum (W):`).
- They open centred on the owner window, inside that screen's working area.
  Cancel changes nothing. Validation is unchanged.

### Decisions pending operator confirmation

| Question | Source default | Alternative |
|---|---|---|
| Graph-wide settings on a pane right-click | One `Graph Options` submenu | Inline after a separator (shipped) |
| `Reset Graph View` | Renamed `Clear Graph History...` with confirmation | Keep the name and add a separate view reset |
| Exclusive-choice glyph | Dot for every radio group | Checkmarks |
| Pane header | Lane name and unit, semibold | Name only, dim |

### Compatibility and risk

- New fork-only `UI/Themes/ThemedMenuRenderer.cs`: the renderer, the header
  label and the choice marker. It derives from `ThemedToolStripRenderer`,
  which stays byte-identical to upstream. The graph menus stop using
  `ToolStripRadioButtonMenuItem`, which is unchanged.
- Fork rows change for `Theme.cs` (menu colors), `UserRadioGroup.cs` (marks its
  items as choices), `MainForm.cs` (renderer, `Clear Graph History...`) and
  `PlotPanel.cs` (menus).
- `net472` and `net10.0-windows`: rounded highlights use `GraphicsPath` arcs,
  so no newer drawing APIs are needed.
- Settings, persisted keys and gestures are unchanged. Tests locate menu items
  by `Name`, not label.

### Menu revision acceptance

- [ ] A pane right-click shows the header, an optional line item, Values,
  Time, Height (stacked only), Remove from Graph, user-lane items, Zoom Scope
  and Graph Options. No graph-wide setting appears at the top level.
- [ ] The gear, a right-click with no pane, and a keyboard invocation with no
  focused pane show the graph-wide list at the top level, with no Graph
  Options parent.
- [ ] Moving the graph-wide items keeps their persisted state and handlers;
  disposing the panel disposes them wherever they are.
- [ ] Parent state text is correct for Values (Auto/fixed), Time
  (shared/preset/custom), Height, Zoom Scope, Grid Density and Time Axis
  (Auto/preset/custom).
- [ ] Exclusive choices draw one dot and toggles a check, centred and scaled.
  Separators, headers, hover and disabled items are distinct in Light, Dark
  and Black at 100%, 150% and 200% fonts (fixture renders).
- [ ] `Clear Graph History...` asks first: No keeps history, Yes clears it.
- [ ] `Lanes` shows its arrow before the first open, and lane entries have no
  duplicate `Auto Range`.
- [ ] Range prompt labels with units, fields and buttons fit at 100–200%
  fonts. Both prompts centre on the owner's screen.
- [ ] Full suite, both Release builds and the divergence-ledger gate pass.
- [ ] After a separately authorized promotion: right-click each pane, use the
  gear, and open `Set Range...` on the installed runtime.

## Shipped behavior and earlier verification

The remaining sections describe the prior implementation and its verification.
Where they conflict with the current source contract above, that contract
governs implementation; checked criteria below are historical evidence only.

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

## Decisions

The operator confirmed these defaults on 2026-11-10 by continuing to the
runtime promotion. Each one stays reversible. The pane time-zoom and
menu-anchor defaults were set on 2026-11-09 to meet the original requirement:
"users can set the zoom level for each pane, SEPARATELY, this is essential."

| Question | Confirmed default | Alternative not taken |
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

Superseded by [Current source contract: graph menus](#current-source-contract-graph-menus).
The shipped `1bf2655f` layout prepended this pane section to the unchanged
graph-wide menu; see Git history for its full text.

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
  through the existing keys; invalid Set Range input is rejected, including a
  finite increasing double pair whose float cast is not a finite increasing
  pair. The prompt leaves a NaN or infinite bound empty and prefills round-trip
  text when `G6` would collapse the range. Wheel and menu value zoom do not use
  that float check.
- [x] Overlay mode ignores overrides and restores them in stacked mode.
- [x] Full x64 test filter and both Release builds pass; ledger gate passes.
- [x] Operator confirmed the defaults above. After promotion of `1bf2655f`, a
  live sample checked pane menus, the gear menu, Power wheel zoom, and a Fan
  1 h detach restored with Follow Shared.
- [ ] A live remove-from-graph and a restart that keeps the layout are still
  unchecked.

## Follow-ups

Not part of this change; each needs its own decision or fix.

- Pan detaches: right-drag or `Alt` + left-drag in a following pane still pans
  the shared window. Detaching would need the pan manipulator to create the
  pane axis when the drag starts.
- Shared Auto stability on detach and keyboard menu targeting are resolved
  in the current source revision above; installed behavior awaits promotion.
- Pane label strip on short panes: a band shorter than one label line shows no
  strip and so no pane time labels.
- The header contrast and `Lanes` entry-order items are handled by the menu
  revision above.
- Overlay mode with the default `This pane` scope: no wheel gesture over the
  overlay plot area zooms (review finding). The operator decides whether
  `Shift` + wheel should act on overlay's single shared time axis.
- Menu-bar `Graph` menu and gear sharing one definition, and the sensor-tree
  menu's always-present `Graph Lane` and `Reset Pen Color`.

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

### 2026-11-10: Set Range float round-trip and pre-paint line hit (`master` after `baca022a`)

- Review of `baca022a` refuted two reports (grouped thousands already fail
  `NumberStyles.Float`; detach and a later own-window zoom are different
  gestures and both stay at 1 s). Two reports held.
- `SetLaneValueRange` returns false before `Zoom` when the float cast is NaN,
  Infinity, or not strictly increasing. The previous range stays, so the next
  save does not write equal floats or `Infinity` and then delete
  `plotPanel.Min/Max`. Wheel zoom and menu Zoom In/Out still go through
  `ZoomLaneValue`.
- `GetSensorAt` skips a series whose `ActualPoints` is still null. That is the
  gap after `SetSensors` replaces the series and before the next
  `model.Update`: `GetNearestPoint` otherwise throws `ArgumentNullException`.
  The property is protected, so the skip reads it by name.
- `FullyQualifiedName~PlotPanel` on the Application project (`-p:Platform=x64`):
  137 passed, 0 failed. The two new tests passed on a separate re-run.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64`:
  Contracts 124 passed; Library 193 passed; Application 371 passed and 1
  established skip (`SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets`).
- Both Release builds (`net10.0-windows`, `net472`, `-p:Platform=x64
  --no-incremental`): 0 warnings, 0 errors.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1 -Filter Test-UpstreamDivergenceLedger`:
  15 passed, 0 failed. `PlotPanel.cs` was already a ledger row, so the row text
  was left as it stood.
- Not verified in that pass: live gestures and menus in the running app.

### 2026-11-10: SND-DESK promotion and live sample (`1bf2655f`)

- Identity verifier returned `VERIFIED` for `snd-desk` instance
  `ca96d510-7d87-4cec-8e1a-bd8fc3866903`.
- `Publish-LibreHardwareMonitor.ps1` wrote
  `E:\Monitoring\LibreHW\Candidates\desk-master-20261110-1bf2655f`.
  Release `0.9.6_1bf2655.2026-11-10-1bf2655f185b-20261109T235203Z`,
  SHA-256 `9b924116c6e00477400a74390dd8cdc5410364ff7d8693cd7290db1d8eb8401c`.
  Its internal suites passed Library 193, Application 371 plus the known skip,
  and Contracts 124. Both Release builds passed with 0 warnings.
- WhatIf preflight, then `Install-LibreHardwareMonitorRelease.ps1
  -AllowStopExactProcess -Confirm:$false`. Activated. Rollback is
  `0.9.6_aa95772.2026-10-27-aa95772b3243-20261027T182911Z`. This runtime also
  contains the commits after `aa95772b`, including the upstream merge.
- Installed exe hash matches the manifest. `http://127.0.0.1:18085/data.json`
  returned 200. The main window title was `Libre Hardware Monitor - Sev IQ`.
- Right-click in Power, Temperature, and Fan each showed that pane's header
  plus Value Scale, Time Scale, Height, and Remove from Graph. Fan Time Scale
  listed Follow Shared, the presets through 24 h including 1 h, and Zoom In /
  Zoom Out. The gear button menu had the shared graph items and no pane header.
- Three plain wheel notches over Power moved its top label from 90 to 75.
  Temperature, Fan, and the shared time axis stayed on the same window.
  Power had no `plotPanel.Min/MaxPower` key, so Auto Range put it back on auto.
- Fan Time Scale `1 h` drew the pane's own axis from `11/9 23:59` to
  `11/10 00:52` while the shared axis stayed on the recent 10 minutes.
  Follow Shared removed that pane axis. The settings file hash was unchanged
  from the pre-gesture copy.
- Not done live: Remove from Graph plus re-plot, a restart that keeps a
  detached pane, the Set Range dialog, DPI, and the dark disabled-header
  contrast.

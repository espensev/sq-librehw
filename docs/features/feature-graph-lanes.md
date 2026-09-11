# Feature Spec: Graph Lanes

**Status:** promoted and live-verified on SND-DESK, including repaired startup zoom persistence; operator layout sign-off remains separate
**Updated:** 2026-09-11
**Focused value zoom and spacing extension:** promoted on SND-DESK; installed health and settings verified, operator gesture/visual acceptance pending
**Design lineage:** brainstormed in chat 2026-08-27 after the Text Size crash fix; supersedes no earlier spec

## Problem and motivation

The graph gives every `SensorType` exactly one Y axis (`PlotPanel._axes`, keyed by the
type name) and binds each series to its type's axis. Sensors of the same type with very
different ranges therefore share one scale: a 12 V rail flattens a 1.2 V Vcore, a 600 RPM
pump and a 2000 RPM fan compete, CPU and GPU package power hide each other's detail. The
operator also wants to give one group (typically the CPU) more of the graph while still
seeing the rest.

Automatic per-sensor axes were rejected: too many variants to lay out well. The accepted
model is operator-defined **lanes** — the operator decides the grouping, zooms each lane
independently, and gives a lane more height when it needs focus. With no lanes created
the graph is exactly today's graph.

## Goals

- Let the operator put any plotted sensor into a lane of its own, or into a lane shared
  with other sensors of the same type.
- Keep per-lane zoom and add a per-lane auto range.
- Let the operator weight a lane's height (1x, 2x, 3x) to focus on it.
- Persist lanes, membership, weights, and zoom across restarts, self-healing on stale or
  malformed state.
- Preserve default graph behavior when no user lane exists, except for the
  additive Shift-wheel shortcut and adaptive value tick spacing described below.

## Non-goals

- Mixing units in one lane. Unlike units stay on separate axes
  (`feature-native-ui-modernization.md`, "Visuals remain honest and efficient").
- Presets such as "make CPU lanes for everything", lane reordering, drag-to-resize lane
  borders, per-lane colors, dimming or filtering of non-focused series.
- Any change to the tracker, the web dashboard, CSV, or the `data.json` contract.
- Small-multiples layout.

## User-visible behavior and contracts

### Lanes

- A lane is one Y axis. The **default lanes** are today's per-type axes and keep their
  keys (`Voltage`, `Fan`, `Power`, ...), so existing persisted zoom keeps working.
- A **user lane** is created from a sensor and is bound to that sensor's `SensorType`. It
  accepts only sensors of that type. Its key is `<SensorType>#<n>` with a monotonically
  increasing `n` starting at 2; removed keys are never reused. Its display name defaults
  to `<Type> <n>` and is renamable.
- Axis title is the lane name; the unit comes from the type as today.
- A lane with no plotted sensor is hidden, like an empty type axis today, but persists.
- Removing a lane returns its sensors to the default lane of their type. Default lanes
  cannot be removed or renamed.
- Stack order: default lanes keep today's order; a user lane sits directly below the
  default lane of its type, in creation order.

### Menu paths

- Sensor tree, sensor node context menu: `Graph Lane >` with the default lane, each user
  lane of that sensor's type, and `New Lane...`. Choosing a lane also enables
  `Show in Graph` if it was off. With a multi-selection the submenu applies to every
  selected sensor when they share one type; otherwise it is disabled.
- `Graph` menu and the graph-local options menu: `Lanes >` with one entry per visible or
  user lane: `Rename...` and `Remove Lane` (user lanes only), `Height > 1x | 2x | 3x`,
  and `Auto Range`.
- `Reset Graph View` keeps today's meaning: it clears sensor values/history and does not
  alter lane zoom, membership, or weights. The existing `Value Axes > Autoscale All`
  action resets zoom on every lane.

### Zoom and height

- Shift + mouse wheel over a stacked lane's plot area or value-axis labels zooms
  only that lane's Y range, anchored at the pointer value. Wheel up zooms in;
  wheel down zooms out. The time window and every other lane retain their ranges
  and auto-range modes. Normal live time scrolling continues.
- The shortcut respects `Value Axes > Enable Zoom`. Outside a visible lane it
  does nothing. In overlay mode, target the value-axis labels; the shared plot
  area is ambiguous and does nothing. Plain wheel and Ctrl + wheel stay unchanged.
- Value tick spacing follows rendered lane height and axis text size, including
  Fine grid mode, instead of forcing twenty labels into short lanes. Time-axis
  Fine spacing and the existing grid visibility choices remain unchanged.
  No new settings, units, APIs, hardware access, or admin requirement are added;
  both frameworks use the same controller binding and existing zoom persistence.
- Mouse-wheel over a lane's axis zooms only that lane (existing OxyPlot behavior under
  `yAxesEnableZoom`). `Auto Range` clears that lane's persisted zoom and leaves it in
  durable auto-range mode. A later manual zoom returns only that lane to fixed-range
  persistence.
- With `Stacked Axes` on, each visible lane gets `weight / sum(weights of visible lanes)`
  of the plot height. With `Stacked Axes` off, lanes are overlaid tiers as today and
  weights are ignored.
- Weights apply to default lanes too, so a temperature-heavy CPU view can be 3x without
  creating a user lane.
- Startup auto-fit retains its legacy behavior only when there are no user lanes and
  every default lane has weight 1. With any user lane or customized default weight,
  the first real data must preserve restored manual zoom for all lanes, including the
  default member of a split. The startup decision is consumed once; removing a lane
  later must not trigger a delayed reset. Explicit `Auto Range` and `Autoscale All`
  remain unchanged.

### Persistence

| Key | Value | Notes |
|---|---|---|
| `plotPanel.lanes` | `key=name:weight;...` for user lanes, in creation order | names are trimmed; `;`, `=`, `:` are replaced by space on input |
| `plotPanel.laneNext.<SensorType>` | next monotonically increasing user-lane number | minimum `2`; repaired from the largest persisted key when absent or malformed |
| `plotPanel.laneWeight.<key>` | `1`..`3` | default lanes only; user-lane weight lives in `plotPanel.lanes` |
| `<sensor identifier>/graphLane` | lane key | same pattern as `<sensor identifier>/plot`; absent means the default lane |
| `plotPanel.Min<key>` / `plotPanel.Max<key>` | fixed zoom persistence | both absent means durable auto range; user lanes reuse the existing key pattern |

### Edge cases and failure states

- Unknown, malformed, or type-mismatched `graphLane` value: the sensor uses its default
  lane; the stale value is overwritten on the next explicit lane choice, never on load.
- Malformed entry in `plotPanel.lanes`: that entry is skipped, the rest load, and nothing
  else in the settings file is rewritten. Weight outside `1..3` clamps.
- A user lane whose type name no longer parses as a `SensorType`: skipped on load.
- Creating a lane for a sensor that is not plotted: allowed; it also turns plotting on.
- Sensor removed from hardware: its lane membership stays persisted, like `plot`.
- Removing a lane never makes its key reusable, so retained membership for an absent
  sensor cannot attach to a later unrelated lane. It resolves to the default lane until
  the operator explicitly assigns that sensor again.

## Affected surfaces

- `LibreHardwareMonitor.Windows.Forms/UI/PlotLanes.cs` (new, pure): lane list, key
  allocation, membership resolution, weight clamp, stacked-layout shares, and
  serialization. No WinForms or OxyPlot types; unit-testable like `UiScale`.
- `PlotPanel`: axes and annotations created from `PlotLanes` instead of the type enum;
  `SetSensors` binds `YAxisKey` to the resolved lane; `UpdateAxesPosition` uses weight
  shares; new public lane create, resolve, rename, remove, weight, and auto-range
  operations; the graph-local `Lanes >` submenu.
- `MainForm`: sensor context submenu, `Graph > Lanes >` submenu, forwarding through
  `PresentationSurfaceCoordinator` / `WinFormsPresentationAdapters` like
  `PlotResetGraphView`.
- Settings: keys above. No API, log, or data-contract change.

## Compatibility and risk

- Upstream sync: `PlotPanel` diverges further from upstream; keeping the logic in
  `PlotLanes` and thin calls in `PlotPanel` limits the merge surface. Accepted local-fork
  divergence.
- `net472` and `net10.0-windows`: no new framework APIs; both targets build the same code.
- DPI and text scale: lanes are axes, so `plotTextScale` and the tracker are unaffected.
- Existing settings files: fully compatible; no key changes, only additions.
- Performance: lane count is small (tens at most); no per-tick work beyond today's.

## Acceptance criteria

- [x] Shift + wheel changes only the hovered value axis in both directions; time
  and sibling ranges/modes stay fixed, with disabled zoom and outside hits inert.
- [x] Overlay axis-label targeting works; overlay plot-area input is inert.
- [x] Rendered short lanes have readable tick spacing at 100% and 200% text scale,
  adapting after resize and height changes; existing zoom persistence remains green.
- [x] With no user lane and no weight set, axis keys, stacking, zoom persistence,
  and series binding are unchanged (existing plot tests stay green without edits).
- [x] A sensor can be moved to a new lane, to an existing lane of its type, and back to the
  default lane from the tree context menu; the graph reflects it immediately.
- [x] A lane accepts only its type; the submenu never offers a lane of another type.
- [x] Per-lane zoom and `Auto Range` affect only that lane; `Value Axes > Autoscale All`
  resets all lane zoom, while `Reset Graph View` retains its sensor-value reset behavior.
- [x] Height weights 1x-3x change stacked shares exactly as `weight / sum`; overlay mode
  ignores them.
- [x] Lanes, membership, weights, and customized-layout zoom survive a restart; malformed or stale values
  self-heal without touching other settings.
- [x] Removing a lane returns its sensors to the default lane.
- [x] Remove-while-sensor-absent, create another lane, and sensor-return cannot reactivate
  the removed membership because lane keys are never reused.
- [x] Auto Range remains auto through autosave and restart; a subsequent manual zoom stores
  fixed Min/Max only for that lane.
- [x] `PlotLanesTests` cover key allocation, membership resolution, clamping, layout
  shares, serialization round-trip, and removal.
- [x] Both Release builds and the full x64 suite pass.
- [ ] The operator verifies a Vcore vs 12 V lane split (or the approved SND-DESK
  Vcore vs 3.3 V substitute), a pump vs fan split, CPU vs GPU
  power, a 3x CPU lane, and restart persistence on the live build.

## Verification plan

For the focused-wheel extension, render fixture lanes and send wheel events through
the actual plot controller. Check pointer anchoring, sibling/time isolation,
auto-range persistence, overlay targeting, disabled zoom, and outside hits. Render
short and weighted lanes at multiple text scales and inspect adjacent tick spacing.
After a separately authorized promotion, repeat Shift + wheel on the live graph.

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Manual, on the promoted build: create the four splits listed in the last acceptance
criterion, zoom one lane with the wheel and confirm the others hold, set a lane to 3x in
stacked mode and confirm the share, toggle `Stacked Axes` off and on, restart, and
confirm the layout returns. Then record the result here and mark the status shipped.

## Implementation order

1. `PlotLanes` pure class, test-first (`PlotLanesTests`).
2. `PlotPanel` reads lanes for axis creation, binding, and layout; existing plot tests
   stay green.
3. Lane operations and persistence in `PlotPanel`; settings round-trip test.
4. Menus in `MainForm` and the graph-local menu, forwarded through the presentation
   coordinator.
5. Spec verification log, `docs/README.md` row, and `AGENTS.md` pointer updated.

## Verification log

### 2026-09-11 — focused value zoom promotion

- Explicitly authorized standard deployment on verified `snd-desk`, installation
  `ca96d510-7d87-4cec-8e1a-bd8fc3866903`. Clean source
  `fbb3a3ea079b4aab863cac4a59b2bbb0aa812e62` passed the publisher's isolated gates:
  410 tests passed, one established skip, both Release builds zero warnings/errors.
- Candidate `focused-value-zoom-20260911` was promoted through installer WhatIf
  and normal guarded install, after native File > Exit saved current settings.
  Release `0.9.6_fbb3a3e.2026-09-11-fbb3a3ea079b-20260911T020503Z` has SHA-256
  `f1788f34e968545898b9d5b60b0b96dfa0fa2cf96b80967d30b6a99f3ca14097`.
- Independent checks at 02:06 UTC verified installed/rollback hashes, exactly
  one installed-path process (PID 59288), a responding window, HTTP 200 for `/`,
  `/data.json`, and `/metrics`, continuing CSV growth, and launcher Validate PASS
  without drift. Public validation and two normal activations returned success
  and retained that same PID. The managed task is the only enabled LibreHW
  startup owner; the legacy repository-build task was subsequently retired by
  the guarded local-release finalizer.
- All 18 selected graph settings, including lane membership, weights, and zoom,
  match the clean-exit backup at
  `E:\SevLocal\Data\LibreHardwareMonitor\release-recovery\focused-value-zoom-20260911\before-promotion.config`.
  The one rollback slot retains `d65d2ae336820a5cc97541319f971f30bd682dfb`.
  This verifies deployment and preservation; operator Shift-wheel/visual
  acceptance remains separate from the rendered/controller regression tests.

### 2026-09-11 — focused value zoom and readable lane ticks (source only)

- Added Shift-wheel through the plot controller, selecting the rendered value
  axis under the pointer. Only its range and manual/auto mode change; overlay
  plot-area input, outside hits, and disabled zoom are inert.
- Value axes now use automatic pixel-based tick intervals in Fine mode, fixing
  the fixed twenty-division crowding while retaining Fine time-axis spacing.
- Before implementation, three focused-wheel cases and both rendered spacing
  cases failed. After implementation, all nine new cases pass, including overlay
  axis-label targeting, pointer anchoring, sibling/time isolation, persistence
  mode, resize, weights, and 100%/200% fonts. Existing panel tests remain green.
- Full x64 solution filter: 410 passed, one established live-config skip, zero
  failures. Both Release builds (`net10.0-windows`, `net472`) pass with zero
  warnings/errors. Fixtures rendered through the real WinForms PNG exporter;
  no hardware runtime was launched or replaced. Live shortcut/visual acceptance
  remains a separate post-promotion check.

### 2026-09-11 — startup zoom repair and live re-verification

- The operator authorized the fix and repeated build, promotion, and live restart
  verification. The repair preserves the legacy uncustomized startup auto-fit while
  protecting manual ranges in customized lane layouts.
- Regression coverage must use the default `AutoFitYOnStart` setting and nonempty
  sensor history after reconstruction, covering user lanes, weighted default lanes,
  uncustomized compatibility, and explicit range-reset actions. Source and live
  results are recorded below only after verification.
- The new user-lane and weighted-default tests first failed against the old startup
  path (expected manual bounds 20 and 0.5; observed auto-fit minima 39.8 and 1.099).
  After the guard, all 11 `PlotPanelLaneTests` passed. Legacy auto-fit, subsequent
  manual zoom, explicit resets, and removal of the last user lane are covered.
- Independent diff review accepted the fix and independently passed all 11 panel
  tests in Release. Clean source `d65d2ae336820a5cc97541319f971f30bd682dfb`
  passed the candidate gates: 401 tests passed, one established live-config skip,
  and both x64 Release frameworks built with zero warnings/errors.
- Verified `snd-desk` promotion at 2026-09-10 23:43 UTC installed release
  `0.9.6_d65d2ae.2026-09-11-d65d2ae33682-20260910T234223Z`, SHA-256
  `c7fb15fe9a26494db5cab2c0b9bdd1adcd3279c64df1ba84255a60fafa68bf10`.
  The rollback slot now contains the preceding `96a3e629` release.
- Native wheel input set manual zoom on Vcore and default Voltage before promotion.
  Clean File > Exit saves, populated live graphs, public-launch restarts, and
  subsequent saves preserved these exact bounds through promotion and another
  normal restart:

  | Lane | Minimum | Maximum |
  |---|---|---|
  | Voltage (3.3 V) | 1.8759649 | 4.9208164 |
  | Vcore (`Voltage#2`) | 1.0818045 | 1.2093045 |

- `zoom-fix-before-promotion.config`, `zoom-fix-after-promotion.config`, and
  `zoom-fix-after-restart.config` under the recovery directory below retain all
  four bounds, lane definitions, three sensor memberships, and stacked mode.
  CPU Power remains 3x and in Auto Range (both fixed-bound keys absent).
  This closes the earlier failed technical criterion without claiming operator
  ratification. An unchecked native Auto Range menu item exposes no UIA toggle
  pattern, so numeric persisted bounds, not a missing toggle value, prove zoom mode.
- The fixed live build passed independent executable/rollback hash checks, exact
  single-process checks, all three HTTP endpoints, continuing CSV growth, and
  launcher Validate with no drift. No SND-HOST promotion was performed.

### 2026-09-11 — live verification preparation

- The operator approved Vcore versus 3.3 V for SND-DESK because the live
  sensor inventory has no 12 V reading. The operator identified Fan #7
  (`/lpc/nct6701d/0/fan/6`) as the pump for the pump-versus-fan check.
- Pre-test settings were backed up without changing the live config to
  `E:\SevLocal\Data\LibreHardwareMonitor\release-recovery\graph-lanes-20260911\before-graph-lanes.config`,
  SHA-256 `6703FB1CEFDD6D6FED517B0A5C48CFDA2952351089F511D4AC11ECB467CFF104`.

### 2026-09-11 — SND-DESK promotion and live checks

- Verified controller/target `snd-desk`, installation
  `ca96d510-7d87-4cec-8e1a-bd8fc3866903`. Promoted clean source
  `96a3e629087200b3fcfacdd7de8b99536228e84f` through the guarded local installer.
  Installed SHA-256:
  `cfeb25307a961b21bb2488822e79fa25170b9dc29db9432cfecf443f3e513771`.
- Candidate gates: 397 passed, one established live-config test skipped;
  both x64 Release frameworks built with zero warnings/errors. HTTP `/`,
  `/data.json`, and `/metrics` returned 200; CSV logging continued.
- Native menu interaction created `Vcore` (`Voltage#2`), `Pump` (`Fan#2`),
  and `CPU Power` (`Power#2`). The 3.3 V sensor was assigned to the existing
  Vcore lane, then returned to default Voltage; menu check states confirmed
  both assignments and the live graph updated immediately.
- Screenshots confirmed separate Vcore/3.3 V, pump/fan, and CPU/GPU power
  scales. CPU Power at 3x occupied three times a 1x lane's height. Overlay
  mode ignored weights; restoring stacked mode restored the 3x share.
  Wheel zoom changed Vcore's scale independently. CPU Power Auto Range
  remained checked after a clean File > Exit and public-launch restart.
- Lane definitions, membership, and weights survived restart:
  `Voltage#2=Vcore:1;Fan#2=Pump:1;Power#2=CPU Power:3`.
- **Initial failure, resolved by the repair above:** manual Vcore zoom did not survive startup.
  `after-first-clean-exit.config` retained `MinVoltage#2=1.0946635` and
  `MaxVoltage#2=1.1938664`; `after-restart-clean-exit.config` lost both keys
  without another zoom action. Both evidence copies are alongside the
  pre-test backup above. `PlotPanel.InvalidatePlot` calls
  `AutoscaleAllYAxes()` on first real data when `AutoFitYOnStart` is true,
  overriding the restored manual range. The repaired startup guard and new live
  evidence above supersede this failure; the original evidence is retained.
- These are agent-executed live checks, not a claim of operator ratification
  or SND-HOST promotion. At this initial checkpoint, the application remained
  healthy on SND-DESK and the rollback slot retained the August 14 release;
  the subsequent repair promotion above replaced that slot with `96a3e629`.

### 2026-09-01 — source implementation

- Application graph-lane regression tests: 13 passed (`PlotLanesTests` and
  `PlotPanelLaneTests`).
- Full x64 solution filter: 397 passed, 1 skipped, 0 failed.
- Release `net10.0-windows` x64 build: passed with 0 warnings and 0 errors.
- Release `net472` x64 build: passed with 0 warnings and 0 errors.
- The UI Automation scrollbar gate was hardened to compare UIA bounds with the HWND's
  native physical coordinates; this removes DPI-virtualized multi-monitor origin drift.
- Live GUI verification and promotion were not run.

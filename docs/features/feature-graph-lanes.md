# Feature Spec: Graph Lanes

**Status:** source implemented and automated gates pass; live operator verification and promotion pending
**Updated:** 2026-09-01
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
- Keep the default graph identical in behavior when no lane exists.

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

- Mouse-wheel over a lane's axis zooms only that lane (existing OxyPlot behavior under
  `yAxesEnableZoom`). `Auto Range` clears that lane's persisted zoom and leaves it in
  durable auto-range mode. A later manual zoom returns only that lane to fixed-range
  persistence.
- With `Stacked Axes` on, each visible lane gets `weight / sum(weights of visible lanes)`
  of the plot height. With `Stacked Axes` off, lanes are overlaid tiers as today and
  weights are ignored.
- Weights apply to default lanes too, so a temperature-heavy CPU view can be 3x without
  creating a user lane.

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

- [x] With no user lane and no weight set, axes, keys, stacking, zoom persistence, and
  series binding are unchanged (existing plot tests stay green without edits).
- [ ] A sensor can be moved to a new lane, to an existing lane of its type, and back to the
  default lane from the tree context menu; the graph reflects it immediately.
- [x] A lane accepts only its type; the submenu never offers a lane of another type.
- [x] Per-lane zoom and `Auto Range` affect only that lane; `Value Axes > Autoscale All`
  resets all lane zoom, while `Reset Graph View` retains its sensor-value reset behavior.
- [x] Height weights 1x-3x change stacked shares exactly as `weight / sum`; overlay mode
  ignores them.
- [x] Lanes, membership, weights, and zoom survive a restart; malformed or stale values
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

### 2026-09-11 — live verification preparation

- The operator approved Vcore versus 3.3 V for SND-DESK because the live
  sensor inventory has no 12 V reading. The operator identified Fan #7
  (`/lpc/nct6701d/0/fan/6`) as the pump for the pump-versus-fan check.
- Pre-test settings were backed up without changing the live config to
  `E:\SevLocal\Data\LibreHardwareMonitor\release-recovery\graph-lanes-20260911\before-graph-lanes.config`,
  SHA-256 `6703FB1CEFDD6D6FED517B0A5C48CFDA2952351089F511D4AC11ECB467CFF104`.

### 2026-09-01 — source implementation

- Application graph-lane regression tests: 13 passed (`PlotLanesTests` and
  `PlotPanelLaneTests`).
- Full x64 solution filter: 397 passed, 1 skipped, 0 failed.
- Release `net10.0-windows` x64 build: passed with 0 warnings and 0 errors.
- Release `net472` x64 build: passed with 0 warnings and 0 errors.
- The UI Automation scrollbar gate was hardened to compare UIA bounds with the HWND's
  native physical coordinates; this removes DPI-virtualized multi-monitor origin drift.
- Live GUI verification and promotion were not run.

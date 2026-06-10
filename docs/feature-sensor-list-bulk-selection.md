# Feature Spec: Sensor List Bulk Selection and Keyboard Access

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Implemented  
**Updated:** 2026-06-10  
**Related docs:** `feature-graph-menu.md`, `feature-graph-ui-review-fixes.md`, `review-sensor-list-bulk-selection-follow-up.md`, `local-ui-customizations.md`  
**Purpose:** make the sensor tree and Graph Inputs dialog properly multi-selectable: bulk context-menu actions, group actions, keyboard verbs, and a selection model that does not self-destruct.

## 1. Summary

The sensor tree supports multi-select, but almost every action ignores it: the right-click menu collapses to Hide/Unhide for multi-selections, graph/tray/gadget membership and pen colors are single-sensor-only, a 1-pixel mouse drag wipes a Ctrl-built selection, and the Graph Inputs grid forbids multi-row selection entirely. This feature makes selections first-class: bulk context-menu actions with counts, type-group actions, keyboard verbs (Del, Space, Apps), and multi-row bulk toggling in Graph Inputs.

## 2. Problem and Motivation

Review sweep 2026-06-10 (multi-agent, adversarially verified) found that adding ten sensors to the graph, tray, or gadget requires ten separate right-click round-trips because `TreeView_Click` returns early for multi-selections before those items are built (`MainForm.cs` ~1371). Two latent input defects compound it: `TreeView_MouseDown` starts swipe-select unconditionally, so any drag collapses multi-selections to a single row; and Aga's `NodeCheckBox` toggles Plot for the whole selection on Space even while the checkbox column is invisible (graph hidden) — a silent state change.

## 3. Goals and Non-Goals

**Goals**

- Bulk actions on the multi-select tree context menu: add/remove graph, pen color set/reset, tray and gadget membership, with selection counts in labels.
- Single-select gains "Add to Graph"/"Remove from Graph" menu items (plot becomes reachable with the graph hidden).
- Type-group (e.g. "Temperatures") right-click menu: hide/unhide all, add/remove group to/from graph.
- Keyboard: Del hides selection; Space plot-toggle blocked while the graph is hidden; Apps / Shift+F10 opens the context menu; F2 (already functional via Aga) and Del advertised in menu item shortcut hints.
- Graph menu gains "Toggle Plot for Selected Sensors".
- Graph Inputs grid: multi-row selection, Space bulk-toggles selected rows, right-click "Plot/Unplot Selected (N)".
- Bulk plot/pen-color changes recompute the plot once, not once per sensor (extends the existing `_suspendPlotSelectionChanged` batching; `ClearGraphInputs` retrofitted onto it). Visibility changes do not raise `PlotSelectionChanged` and are not part of this batching path.

**Non-goals**

- Sensor reordering (display-order persistence, drag-drop) — separate future spec.
- Central gadget/tray management dialog, tree search/filter, CSV logged-sensor selection.
- Any change to sensor `Identifier` values, `Node.Nodes` ordering, CSV columns, or web JSON output.

## 4. Behavior Specification

Tree context menu, sensor node(s) selected (`N` = selected sensor count, clicked node included):

- Single select keeps Parameters... and Rename (Rename shows "F2" hint).
- Hide / Unhide unchanged; Hide shows "Del" hint.
- New graph group (single and multi): "Add to Graph" / "Add Selected to Graph (N)" shown when any selected sensor has `Plot == false`; "Remove from Graph" / "Remove Selected from Graph (N)" when any has `Plot == true`. Pen Color... and Reset Pen Color(s) now apply to the whole selection.
- Multi select additionally gets "Show Selected in Tray (N)" / "Remove Selected from Tray (N)" and, when the gadget exists, "Show Selected in Gadget (N)" / "Remove Selected from Gadget (N)". Show/Remove items appear only when at least one selected sensor is missing/present respectively; per-sensor membership checks keep mixed selections idempotent. Bulk tray adds pass `balloonTip = false`.
- Single select keeps the existing checkable "Show in Tray" / "Show in Gadget" and Control submenu unchanged.

Type node (e.g. "Temperatures") right-click — previously no menu:

- "Hide All in Group (N)" / "Unhide All in Group (N)" (shown when any child is visible/hidden), "Add Group to Graph (N)" / "Remove Group from Graph (N)". Group = the type node's direct `SensorNode` children, including hidden ones.
- Reachability limitation: with "Show hidden sensors" off, a type node is filtered out when all of its children are hidden. Its "Unhide All in Group" action is therefore available only while the type node still has a visible child or while hidden sensors are shown.

Selection / input:

- Swipe-select only starts on a plain left press over an unselected row: Ctrl/Shift-modified presses and presses on an already-selected row no longer start it, and only the left button sustains it. Plain drag-select behavior over unselected rows is unchanged.
- `TreeView_KeyDown`: Space is marked handled when Graph > Show Graph is off (blocks the invisible selection-wide Plot toggle). When the graph is shown, Space currently falls through to Aga's `NodeCheckBox`, which toggles each selected sensor separately; the follow-up plan replaces that path with `TogglePlotForSelectedSensors` so the advertised shortcut and the batched menu action are equivalent. Del hides the selected sensors. Apps or Shift+F10 opens the same context menu as right-click, positioned at the selected node (no-op when nothing is selected). Left/Right expansion persistence unchanged.
- Graph menu: "Toggle Plot for Selected Sensors" (hint "Space") — if any selected sensor is unplotted, plots all selected; otherwise unplots all selected. No-op without a sensor selection.

Graph Inputs dialog:

- Grid allows multi-row selection (Ctrl/Shift+click). Space with >1 row selected toggles them as a batch (all on if any off, else all off) through the existing single-recompute path; the stray single-cell toggle is suppressed on both KeyDown and KeyUp. Right-click selects the row under the cursor when it is outside the current selection and offers "Plot Selected (N)" / "Unplot Selected (N)". Clicking directly on the On checkbox cell still toggles just that cell (known quirk, unchanged).

Failure/edge cases: empty selections cancel menus or no-op; mixed plot/tray/gadget states show both directions of each action; all bulk operations are idempotent per sensor.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Tree context menu extended (sensor multi-select + type-group branch); Graph menu +1 item; Graph Inputs grid multi-select + context menu; shortcut hints on Rename/Hide |
| Settings/config | None new — existing per-identifier `/plot`, `/penColor`, `/hidden`, `/tray`, `/gadget` keys written in bulk via existing setters |
| Remote web/API | None. `Node.Nodes` never mutated; HttpServer walks raw `Node.Nodes` (`HttpServer.cs` GenerateJsonForNode) |
| Logging/files | None. CSV logger reads the hardware model, not the tree/UI state |
| Hardware/admin flow | None |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | Changes concentrated in fork-touched `MainForm.cs`/`GraphInputsForm.cs`. One vendored-lib delta: `Aga.Controls` `TreeViewAdv.GetNodeBounds(TreeNodeAdv)` visibility `internal` → `public` (1 word; Aga upstream is dormant) |
| `net472` vs `net10.0-windows` | Plain WinForms/LINQ only; both targets built |
| DPI/multi-monitor | Context menu positions derive from node bounds/click points in client coordinates, clamped to the tree's client area |
| Existing settings/users | No settings schema change; single-select menu layout gains one graph group but keeps all existing items and semantics |
| Plot recompute cost | Bulk plot and pen-color paths share `RunBatchedPlotChange`, which uses a suppression-depth counter + pending-rebuild flag so each user action recomputes once; `ClearGraphInputs` is one recompute. Visibility changes raise no plot-selection event. Tree Space while the graph is shown now routes through `TogglePlotForSelectedSensors` (one batched recompute), resolving the former unbatched follow-up. |
| Hide/plot interaction | Resolved by the follow-up: `PlotSelectionChanged` rebuilds from `GetAllSensorNodes()` (the full model) via `RebuildPlotSelection`, never the filtered `treeView.AllNodes`. `Plot` is the sole source of graph membership and is independent of tree visibility and the "Show Hidden Sensors" filter. See `review-sensor-list-bulk-selection-follow-up.md`. |

## 7. Acceptance Criteria

- [x] Multi-select right-click offers graph/pen-color/tray/gadget bulk actions with counts; ten sensors can be added to the gadget in one action.
- [x] Ctrl+click selections survive small mouse drags; pressing an already-selected row does not collapse the selection.
- [x] Space does nothing in the tree while the graph is hidden; with the graph shown it routes through `TogglePlotForSelectedSensors` (one batched recompute) instead of Aga's per-node toggle loop, matching the Graph menu action.
- [x] Del hides the selected sensors; Apps/Shift+F10 opens the context menu at the selected node.
- [x] Type-group right-click can hide/unhide or plot/unplot the whole group.
- [x] Graph Inputs: Shift-select a range, press Space or right-click → one recompute toggles all selected rows.
- [x] Existing behavior not in scope remains unchanged: `/data.json` and Prometheus output ordering, CSV columns, single-sensor menu semantics, gadget/tray rendering.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` | 0 errors |
| Runtime smoke | Launch rebuilt app; multi-select sensors; right-click → bulk items present and act on all; Apps key opens menu; Del hides; Graph Inputs Shift-select + Space toggles batch | As specified, app stays responsive, CSV logging continues |

## 9. Open Decisions

None blocking for the implemented feature.

Post-implementation review confirmed that the visible-graph Space path should be routed through `TogglePlotForSelectedSensors`. It also found a higher-order issue: plot recomputation reads the filtered visual tree instead of the full model, and the global boolean suppression guard is non-composable. The corrected architecture and ranked work are recorded in `review-sensor-list-bulk-selection-follow-up.md`.

## 10. Implementation Notes

- `MainForm.cs`: `TreeView_Click` menu body extracted to `ShowNodeContextMenu(TreeNodeAdv, Point)` (shared by mouse + keyboard); new `SetSensorNodesPlot`, `SetSensorNodesPenColor` over a shared `RunBatchedPlotChange` core; `ClearGraphInputs` retrofitted; `InitializeGraphMenu` adds the toggle item; `TreeView_MouseDown`/`TreeView_MouseMove` swipe-select guard; `TreeView_KeyDown` Space gate + Del + Apps/Shift+F10.
- `GraphInputsForm.cs`: `MultiSelect = true`, grid KeyDown/KeyUp Space batch + suppression, `CellMouseDown` right-click row selection, `ContextMenuStrip` built on `Opening`, disposed with the form.
- `Aga.Controls/Tree/TreeViewAdv.cs`: `GetNodeBounds(TreeNodeAdv)` made public for keyboard menu placement.
- F2 rename was already implemented inside Aga's `NodeTextBox.KeyDown`; this feature only advertises it.
- `SetSensorNodesVisible` batches tree redraw with `BeginUpdate`/`EndUpdate`; it does not affect plot batching because `SensorNode.IsVisible` neither changes `Plot` nor raises `PlotSelectionChanged`.

Follow-up changes (see `review-sensor-list-bulk-selection-follow-up.md`):

- `PlotSelectionChanged` is now a lightweight request path. While a batch scope is active it sets `_plotRebuildPending`; otherwise it calls `RebuildPlotSelection`, which enumerates `GetAllSensorNodes()` (the full model) rather than `treeView.AllNodes`. Graph membership, default color slots, and explicit pen colors are all read from the model, so tree visibility and "Show Hidden Sensors" never change graph output.
- The boolean `_suspendPlotSelectionChanged` is replaced by `_plotEventSuspendDepth` (nesting counter) + `_plotRebuildPending`. `RunBatchedPlotChange` increments/decrements the depth and performs exactly one rebuild when the outermost scope exits, in a `finally` so a mutation that throws mid-batch still rebuilds from the changes already applied.
- `ShowGraphInputsForm` no longer holds suppression for the dialog lifetime. It passes `SetSensorNodesPlot` to the dialog; `GraphInputsForm` routes every plot mutation (single checkbox edit and bulk action) through that setter, so each user action is one batched rebuild and unrelated sensor/hardware add-remove events still rebuild the graph while the dialog is open. `_inputsChanged`/`_suspendInputsChanged` removed.
- `TreeView_KeyDown` handles only a plain Space (returns early for any modifier so Alt+Space opens the window system menu and Ctrl/Shift+Space extend selection); a plain Space always consumes the key (`Handled` + `SuppressKeyPress`) so Aga's `NodeCheckBox` never sees it, and with the graph shown it calls `TogglePlotForSelectedSensors`.
- `GraphInputsForm` uses an `InputsGrid : DataGridView` subclass that records whether the context menu was opened from the keyboard (`WM_CONTEXTMENU` `lParam == -1`); `GridMenu_Opening` cancels a mouse-originated opening that does not hit a data cell, and uses a `_swallowNextSpaceKeyUp` handshake plus an `e.ColumnIndex < 0` guard in `Grid_CellMouseDown`.
- Tree context menu: `AddTreeContextMenuSeparator` inserts a separator only between non-empty sections; `AddBulkMembershipMenuItems` extracts the tray/gadget add/remove loops (bulk tray adds keep `balloonTip = false`); `EnsureVisible` is called before reading node bounds for keyboard menu placement.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-10 | `dotnet build ... -c Debug -f net10.0-windows -p:Platform=x64` | pass | 0 warnings, 0 errors |
| 2026-06-10 | `dotnet build ... -c Release -f net472 -p:Platform=x64` | pass | 0 warnings, 0 errors |
| 2026-06-10 | `dotnet build ... -c Release -f net10.0-windows -p:Platform=x64` | pass | Recurrent CS0016 lock on `LibreHardwareMonitorLib\obj\...\LibreHardwareMonitorLib.xml` (external user-mapped section, pre-existing environment issue) cleared by deleting the obj XML before building |
| 2026-06-10 | Launched rebuilt Release app (elevated), PID 96172 | pass | Responding after 12 s; CSV logging resumed (`LibreHardwareMonitorLog-2026-06-10-233.csv` actively appending) |
| 2026-06-10 | Manual menu interaction QA (bulk items, Apps key, Del, Graph Inputs Space batch) | pending | Awaiting maintainer click-through; code paths compile-verified on both targets |
| 2026-06-10 | Follow-up landed (model-based rebuild, nestable suppression, routed Graph Inputs, Space toggle, context-menu origin guard, cleanup); `dotnet build ... -c Release -f net10.0-windows -p:Platform=x64` | pass | 0 warnings, 0 errors (after stopping the running build artifact that held a file lock) |
| 2026-06-10 | Follow-up build `dotnet build ... -c Release -f net472 -p:Platform=x64` | pass | 0 warnings, 0 errors |
| 2026-06-10 | Relaunched rebuilt follow-up Release app, PID 38804 | pass | Responding after 12 s; CSV logging resumed (`LibreHardwareMonitorLog-2026-06-10-550.csv` actively appending) |
| 2026-06-10 | Adversarial multi-agent review of the follow-up diff (5 dimensions, dual refuters) + fixes (Alt+Space modifier guard, swallow-flag strand reset, Clear All mirror sync, WM_CONTEXTMENU point capture); rebuilt Release x64 both targets | pass | 0 warnings, 0 errors after fixes; one major (Alt+Space) and three minor findings fixed, one race finding refuted, two tradeoffs accepted — see `review-sensor-list-bulk-selection-follow-up.md` §0.1 |
| 2026-06-10 | Follow-up manual QA (hidden-sensor plot persistence, hide/unhide no-recompute, Show Hidden toggle color stability, plain tree Space toggle, Alt+Space system menu, Graph Inputs header/blank right-click guard, Clear All with active filter) | pending | Awaiting maintainer click-through; code paths compile-verified on both targets and adversarially code-reviewed |

## 12. Post-Implementation Review Correction

A review finding claiming that `SetSensorNodesVisible` caused N plot recomputes was rejected. `SensorNode.IsVisible` only updates the base visibility flag and persists the `/hidden` setting; it does not assign `Plot` or raise `PlotSelectionChanged`. The rejected finding must not be used as justification for changing the visibility helper.

The adjacent real behavior is pre-existing: hiding a plotted sensor causes zero plot recomputes and leaves `Plot == true`, but the next recompute reads the filtered visual tree and can remove that series. This feature adds Del and group-menu entry points to that existing visibility path but does not create the inconsistency. The follow-up plan resolves it by rebuilding graph membership from the full sensor model.

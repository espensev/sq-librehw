# Sensor List Bulk Selection: Post-Implementation Review and Follow-Up Plan

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Implemented (Phases 1–3); dual-target build verified; manual QA pending  
**Updated:** 2026-06-10  
**Related spec:** `feature-sensor-list-bulk-selection.md`  
**Purpose:** record the corrected review result and provide an implementation-ready plan for confirmed follow-up work.

## 0. Implementation Status

All P0–P3 items in §3 are implemented and both targets build clean (Release x64, `net10.0-windows` + `net472`, 0 warnings/0 errors). Notable as-built details:

- The graph rebuild reads `GetAllSensorNodes()` via `RebuildPlotSelection`; `PlotSelectionChanged` is the lightweight request path. Suppression is `_plotEventSuspendDepth` + `_plotRebuildPending`, with the single rebuild performed in `RunBatchedPlotChange`'s `finally`.
- `GraphInputsForm` is constructed with `MainForm.SetSensorNodesPlot` and routes all plot mutations through it; dialog-lifetime suppression is gone.
- **Deviation from §3 P1 (context-menu targeting):** instead of tracking right-`MouseDown` `HitTest` results, an `InputsGrid : DataGridView` subclass records keyboard vs. mouse menu invocation from `WM_CONTEXTMENU` (`lParam == -1`), and `GridMenu_Opening` hit-tests the cursor for mouse-originated openings. This needs no cross-event flag that can go stale and preserves keyboard (Apps/Shift+F10) invocation; the row-under-cursor selection still happens in `Grid_CellMouseDown`.
- **Color-assignment consequence:** automatic plot colors are assigned by ordinal position over the full model, so a user who has hidden sensors with "Show Hidden Sensors" off may see different default colors than before (hidden sensors now consume a palette slot). This is the intended result of making membership/colors independent of the display filter (§4.4); explicit per-sensor pen colors are unaffected.

### 0.1 Adversarial Review Outcome

A five-dimension multi-agent review (state/event model, Graph Inputs form, input handling, contract regressions, plan fidelity), each finding cross-checked by two refuters, ran against the working-tree diff. Findings acted on:

- **Fixed (major):** the tree Space handler was modifier-insensitive, so **Alt+Space** (window system menu) was suppressed and, with the graph shown, plot-toggled. It now returns early for any modifier, leaving Alt/Ctrl/Shift+Space to the framework; only a plain Space is the toggle verb.
- **Fixed (minor):** `_swallowNextSpaceKeyUp` could be stranded if a bulk-toggle KeyDown's paired KeyUp never reached the grid (focus change), silently eating one later single-row Space toggle. A single-row Space KeyDown now clears the flag.
- **Fixed (minor):** bulk `SetRows` (notably **Clear All**, which passes every row) updated the model but `RefreshRows` only re-synced grid-bound rows, leaving filtered-out rows' `On` mirrors stale until the next 1 s tick. `SetRows` now refreshes every changed row.
- **Fixed (minor):** the Graph Inputs context-menu hit test read the live cursor at `Opening` time; it now hit-tests the screen point captured from the `WM_CONTEXTMENU` `lParam`.
- **Not reachable (refuted):** the "cross-thread race on the suspension fields during a modal Graph Inputs session" cannot occur — `HardwareAdded`/`HardwareRemoved` (and all plot events) originate on the UI thread; the background updater only runs `Accept`/`InvalidatePlot`. Threading is unchanged from upstream.
- **Accepted tradeoffs (no change):** plain Space no longer contributes its character to Aga tree type-ahead search (Space is now a dedicated plot-toggle verb); the Graph Inputs **Apply** button is effectively a no-op because edits apply live through the routed setter (it still commits an in-progress cell edit). The `WM_CONTEXTMENU` `lParam == -1` keyboard sentinel and 16-bit coordinate extraction follow the established WinForms idiom.

Both targets rebuilt clean after the fixes (Release x64, `net10.0-windows` + `net472`, 0/0).

## 1. Review Result

The implementation satisfies the feature spec's checked acceptance criteria by code trace and dual-target build evidence. Manual click-through QA remains pending.

One reported major finding was invalid: `SetSensorNodesVisible` does not trigger one plot recompute per sensor. `SensorNode.IsVisible` updates `base.IsVisible` and persists the `/hidden` setting only; it neither changes `Plot` nor raises `PlotSelectionChanged`.

The real adjacent quirk predates this feature: hiding a plotted sensor performs no plot recompute, leaves `Plot == true`, and can leave the series displayed until a later recompute. Del and the group menu add entry points to the existing visibility path; they did not introduce the quirk.

## 2. Higher-Order Findings

These findings outrank the original input and cleanup list because they define the state model used by every plot action.

### H1. Plot recomputation reads the filtered view instead of the sensor model

`PlotSelectionChanged` currently enumerates `treeView.AllNodes`, while plot settings and Graph Inputs operate on the full `Node` model. `TreeModel.GetChildren` filters invisible nodes unless the main window's "Show hidden sensors" option is enabled, so the set of sensors considered by a graph rebuild depends on a display filter.

Confirmed consequences:

- a hidden sensor can have `Plot == true` but be omitted from the graph after any recompute;
- plotting a hidden sensor from Graph Inputs can persist `/plot=true` without adding the series when the main tree hides hidden sensors;
- hiding a plotted sensor leaves the old series until a later recompute, then removes it even though `Plot` remains true;
- unhiding that sensor does not restore the series until another recompute;
- changing the main tree's hidden-sensor filter can change later graph membership and automatic color assignment indirectly.

**Recommended contract:** plot membership and tree visibility are independent. `Plot == true` means the sensor is a graph input whether or not its tree row is visible. Every graph rebuild should enumerate `GetAllSensorNodes()` (the underlying model), not `treeView.AllNodes` (the filtered presentation).

Under this contract, hide/unhide does not need to raise `PlotSelectionChanged`: visibility does not affect graph membership. This also makes Graph Inputs' "Show hidden sensors" option behaviorally coherent.

### H2. Plot-event suppression is global, non-nestable, and held too long

`_suspendPlotSelectionChanged` is a boolean. `RunBatchedPlotChange` sets it true and then unconditionally false, so a nested caller can clear suppression owned by an outer caller. `ShowGraphInputsForm` holds the same flag for the dialog's entire modal lifetime, which suppresses unrelated sensor-add/remove and hardware-add/remove plot events until a user action happens to force a rebuild.

**Recommended design:**

- replace the boolean with a suppression depth and a pending-rebuild flag;
- while depth is nonzero, plot events mark one rebuild pending rather than disappearing;
- when the outermost scope exits, perform exactly one model-based rebuild if any event occurred;
- stop holding suppression for the whole Graph Inputs session;
- route Graph Inputs single-row and bulk plot mutations through `MainForm.SetSensorNodesPlot`, making `MainForm` the single batching owner.

This design is composable, preserves unrelated events, and gives tree, menu, and dialog actions the same mutation path.

## 3. Confirmed Follow-Ups

| Priority | Item | Classification | Planned change |
|---|---|---|---|
| P0 | Graph rebuild uses filtered `treeView.AllNodes` | State-model correctness | Rebuild from a snapshot of `GetAllSensorNodes()`. Preserve `Plot` independently of `IsVisible`; visibility and "Show hidden sensors" must not affect graph membership or color ordering. |
| P0 | Global boolean plot suppression can discard or prematurely release updates | Event-model correctness | Replace it with nested suppression depth + pending rebuild. Remove dialog-lifetime suppression and centralize Graph Inputs plot mutations through `SetSensorNodesPlot`. |
| P1 | Tree Space causes N recomputes for N selected sensors while the graph is shown | Confirmed performance defect and shortcut mismatch | Handle Space in `MainForm.TreeView_KeyDown` for both graph states. Keep hidden-graph behavior as a no-op; when shown, call `TogglePlotForSelectedSensors`, mark handled/suppressed, and prevent Aga's per-node `NodeCheckBox.KeyDown` loop. |
| P1 | Graph Inputs menu opens from header or blank area and acts on stale/off-screen selection | Confirmed context-menu targeting defect | Track right-button `DataGridView.HitTest` results from grid `MouseDown`. Cancel a mouse-originated opening unless it hit a valid data cell; preserve keyboard/Apps-key invocation for the current selection. |
| P2 | Grid Space suppression re-evaluates selection independently on key-up | Defensive hardening | Add a `_swallowNextSpaceKeyUp` handshake set only when KeyDown performs a bulk toggle, then consume and clear it in KeyUp. |
| P2 | `Grid_CellMouseDown` assumes a non-negative column index | Defensive hardening | Reject `e.ColumnIndex < 0` before indexing `Cells`; keep the existing non-checkbox fallback for valid cells. |
| P2 | A fully hidden type group loses its group-menu unhide entry point | Reachability limitation | Keep the existing recovery path through View > Show Hidden Sensors for this pass. Evaluate a hardware-level "Unhide Hidden Sensors (N)" action as a separately specified UI enhancement. |
| P3 | Four tray/gadget membership loops duplicate predicate/action structure | Maintainability | Extract a small bulk membership helper that accepts nodes, membership predicate, and action. Preserve `balloonTip = false` for bulk tray adds. |
| P3 | `ShowNodeContextMenu` has a redundant nested brace block | Mechanical cleanup | Remove the orphaned block and reindent without behavior changes. |
| P3 | Conditional menu sections rely on current item availability to avoid duplicate/empty separators | Future robustness | Add a separator helper that inserts only between non-empty sections and never duplicates an existing separator. |
| P3 | Keyboard context menu can anchor to clamped header coordinates for a scrolled-away selection | Accessibility polish | Call `EnsureVisible(selectedNode)` before reading bounds for Apps/Shift+F10 placement. |

## 4. Plot and Visibility Decision

The plan adopts this contract:

1. `SensorNode.Plot` is the source of truth for graph membership.
2. `SensorNode.IsVisible` controls only tree presentation.
3. Hidden plotted sensors remain plotted.
4. Plot recomputation always uses the complete model.
5. "Show hidden sensors" changes only tree presentation.

This is the narrowest coherent interpretation of the existing independent `/plot` and `/hidden` settings and of Graph Inputs' ability to expose hidden sensors. It avoids an implicit side effect where hiding a row silently changes graph output.

If the intended product behavior is instead "Hide also removes from graph," that is a different user-visible contract: hide would need to assign `Plot = false` or maintain a separate effective-membership rule. That alternative should not be mixed into this bugfix.

## 5. Reachability Limitation

The type-group menu operates on all direct sensor children, including hidden sensors. With "Show hidden sensors" off, `TypeNode` becomes invisible when every child is hidden, so "Unhide All in Group" is unavailable from the tree exactly when the entire group is hidden. The implemented spec now records this limitation.

Changing that reachability requires a separate UI decision, such as keeping empty type rows visible, adding an unhide command at the hardware-node level, or providing a global hidden-sensor management surface.

## 6. Implementation Sequence

### Phase 1: Correct the state and event model

1. Split the current event handler into a lightweight request path and a model-based rebuild method.
2. Snapshot `GetAllSensorNodes()` once per rebuild and use that snapshot for selected sensors, default colors, and explicit pen colors.
3. Replace `_suspendPlotSelectionChanged` with nesting depth plus a pending flag. Ensure partial changes still trigger one rebuild if a mutation throws.
4. Change `GraphInputsForm` to receive a bulk plot setter owned by `MainForm`.
5. Route both checkbox edits and `SetRows` through that setter; remove `_inputsChanged`, `_suspendInputsChanged`, and the dialog-lifetime suppression.
6. Keep `SetSensorNodesVisible` visibility-only and do not force a graph rebuild.

### Phase 2: Fix input correctness

1. Route visible-graph tree Space through `TogglePlotForSelectedSensors`; consume the event before Aga's checkbox control sees it.
2. Replace cell-only right-click tracking with grid-level hit testing so header and blank-area clicks are distinguishable from keyboard context-menu invocation.
3. Add the Space key-down/key-up handshake and the negative-column guard.

### Phase 3: Apply bounded cleanup

1. Extract the tray/gadget bulk membership helper.
2. Remove the redundant `ShowNodeContextMenu` brace block.
3. Add separator insertion protection.
4. Ensure the keyboard-selected tree node is visible before computing menu bounds.

### Phase 4: Verify and record

1. Build Release x64 for `net10.0-windows` and `net472`.
2. Run the manual matrix below.
3. Record results in the feature spec verification log.
4. Keep the hardware-level unhide enhancement out of this pass unless separately accepted.

## 7. Verification Plan

| Check | Expected result |
|---|---|
| With main View > Show Hidden Sensors off, use Graph Inputs > Show hidden sensors to plot a hidden sensor | The hidden sensor appears on the graph and remains `Plot == true`. |
| Hide and unhide an already plotted sensor | Tree visibility changes; graph membership does not change. |
| Toggle main View > Show Hidden Sensors repeatedly | Graph membership and automatic plot colors do not change. |
| Plot/unplot mixed visible and hidden rows in Graph Inputs | One rebuild per action; all requested sensors reach the requested state. |
| Add/remove a sensor or hardware source while Graph Inputs is open | The plot rebuild request is not discarded. The dialog may remain a snapshot for this pass, but graph state stays correct. |
| Exercise nested batched plot changes in an instrumented/debug path | Inner completion does not release outer suppression; the outermost completion performs one pending rebuild. |
| Select multiple tree sensors with the graph shown; press Space | All selected sensors change to the same target plot state and the plot recomputes once. |
| Select multiple tree sensors with the graph hidden; press Space | No plot state changes. |
| Compare Graph menu "Toggle Plot for Selected Sensors" with tree Space | Both use the same all-on-if-any-off, otherwise-all-off behavior. |
| Right-click a Graph Inputs data row outside the current selection | That row becomes the sole selection and the menu targets it. |
| Right-click the Graph Inputs header or blank area below the last row | No context menu opens and no stale selection action is offered. |
| Invoke the Graph Inputs context menu from the keyboard | The menu opens for the current selected rows. |
| Change selection between Space KeyDown and KeyUp during an instrumented/manual test | The single-cell checkbox does not receive a stray key-up toggle. |
| Build both target frameworks | 0 errors for `net10.0-windows` and `net472` Release x64. |

## 8. Residual Risks and Non-Goals

- Graph Inputs takes a sensor-list snapshot when opened. Dynamically added sensors need not appear as new rows until the dialog is reopened in this pass; the requirement is that their graph rebuild events are no longer suppressed.
- There is no dedicated automated test project. The state-rebuild and suppression logic should be kept small and side-effect-light; verification remains dual-target build plus focused runtime checks unless adding a test project is separately justified.
- Changing hide to clear `Plot`, keeping empty type rows permanently visible, or adding a hardware-level unhide command are separate user-visible decisions.

## 9. Refuted Concerns

No code change is planned for the reviewed concerns that did not survive trace verification: closure staleness, `Node.Nodes` mutation, `net472` language incompatibility, Graph Inputs handler leaks, timer re-entrancy, or the bulk tray `balloonTip = false` choice.

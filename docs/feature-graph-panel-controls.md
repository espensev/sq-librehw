# Feature Spec: Graph Panel Controls

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Draft  
**Updated:** 2026-06-06  
**Related docs:** `feature-graph-menu.md`, `local-ui-customizations.md`, `feature-workflow.md`  
**Purpose:** make graph-specific controls available from the graph panel itself, without changing graph data or table behavior.

## 1. Summary

Add a small graph-local control surface to the plot panel so users can change graph view options where they are looking, not only through the main menu or plot right-click menu.

## 2. Problem and Motivation

The fork already has a stronger top-level `Graph` menu and a plot context menu. Some graph options still feel hidden because they live behind right-click behavior or in the main menu while the user's attention is on the plot. A compact graph-local control keeps those options discoverable while preserving the existing dense sensor table.

## 3. Goals and Non-Goals

**Goals**

- Add one compact, keyboard-accessible graph-local entry point in the graph panel.
- Reuse existing plot option state and handlers where possible.
- Surface graph-local options such as grid density, time-axis presets, stacked axes, axes labels, value-axis zoom/autoscale, and reset graph view.
- Work in all graph placements: separate window, bottom panel, and right panel.
- Preserve defaults and user settings for existing graph options. New options follow their own feature specs.

**Non-goals**

- Do not redesign the main menu.
- Do not replace the plot right-click context menu.
- Do not add new graph data transformations.
- Do not change sensor polling, logging, min/max values, web/API output, or selected graph inputs.
- Do not add large toolbar chrome that steals meaningful plot area.

## 4. Behavior Specification

When the graph is visible, the plot panel provides one small graph-local control: a graph-options drop-down button anchored at the top-right of the `PlotPanel`. Prefer an overlay button so the plot keeps its current footprint. If WinForms z-order or focus behavior makes an overlay unreliable, use a slim header strip no taller than 28 px.

The button opens the same graph view command set as the plot right-click context menu. Reuse the existing `PlotPanel` menu actions and option objects where practical instead of creating a parallel state system.

The menu should expose the same behavior as existing graph controls:

- grid density: Off, Major, Normal, Fine;
- time-axis presets already available in the plot context menu;
- stacked axes toggle;
- show axes labels toggle;
- time-axis zoom enable toggle;
- value-axis zoom enable toggle;
- autoscale value axes;
- reset graph view.

The graph-local menu must use the same setting keys and state as existing controls. Changing an option from the graph-local menu updates any matching main-menu or context-menu state, and changing the option elsewhere is reflected when this menu opens.

The control must remain usable when the plot is embedded at the bottom or right side and the panel is narrow. Use a fixed-size icon/drop-down button with a tooltip and accessible name such as `Graph options`; do not rely on text that can clip.

Opening the menu from the button must not interfere with the existing right-click plot context menu or the right-drag context-menu suppression behavior. The control must not consume plot mouse gestures outside its own bounds.

The plot right-click context menu remains available. The main `Graph` menu remains the primary global graph menu.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Adds one small graph-options drop-down button inside the plot panel. The button opens the shared plot graph-options menu. |
| Settings/config | Reuses existing settings for graph options; no migration expected. |
| Remote web/API | No change. |
| Logging/files | No change. |
| Hardware/admin flow | No change. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | Keep the implementation localized to `PlotPanel` / `MainForm` and avoid broad menu refactors. |
| `net472` vs `net10.0-windows` | Use WinForms controls available in both targets. |
| DPI/multi-monitor | Use fixed minimum sizes, anchoring, and no clipped text; verify at common DPI scales. |
| Hardware/admin rights | Not affected. |
| Existing settings/users | Preserve existing keys and defaults; do not migrate unless unavoidable. |

## 7. Acceptance Criteria

- [ ] A graph-local graph-options button is visible and keyboard-reachable whenever the graph panel is visible.
- [ ] The control works in separate-window, bottom-panel, and right-panel graph placements.
- [ ] The control has a tooltip and accessible name, and its text/icon does not clip in narrow graph panels.
- [ ] Opening options from the button and from the plot right-click menu exposes the same command semantics.
- [ ] Grid density can be changed from the graph-local menu and matches the existing context-menu behavior.
- [ ] Time-axis presets can be selected from the graph-local menu and match existing behavior.
- [ ] Stacked axes, axes labels, zoom toggles, and autoscale value axes are available without changing their semantics.
- [ ] Autoscale value axes visibly refreshes the plot immediately after selection.
- [ ] Existing main-menu and right-click graph controls still work.
- [ ] Existing plot mouse gestures still work outside the button bounds.
- [ ] No sensor data, polling, logging, web/API behavior, or graph input selection changes.
- [ ] Text and controls do not clip at 100%, 125%, and 150% DPI.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` | 0 errors |
| Runtime smoke | Launch app, show graph, test separate/bottom/right placements | Graph-local control remains usable in each placement |
| State sync | Change grid density and stacked axes from graph-local menu, then open context menu/main menu | Menus reflect the same state |
| Input behavior | Right-click the plot, right-drag the plot, then open the button menu | Existing plot context-menu behavior remains intact |
| Autoscale refresh | Zoom a value axis, then choose autoscale from the graph-local menu | Axis rescales and plot redraws immediately |
| Keyboard/accessibility | Tab to the control and open it from the keyboard | Menu opens and tooltip/accessibility name identifies the control |
| Regression check | Watch graph and sensor table while toggling options | Sensor values and graph inputs are unchanged |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| Overlay fallback | Implementation | Top-right overlay button; use a slim <= 28 px header only if overlay behavior is unreliable |
| Include `Graph Inputs...` from the panel | Spec acceptance | No; keep input management in the main `Graph` menu unless explicitly requested |
| Button glyph | Implementation | Use a standard options/drop-down glyph where available; avoid text unless it fits without clipping |

## 10. Implementation Notes

Implementation should prefer opening or sharing the existing `PlotPanel` context-menu actions instead of duplicating state logic. If commands must be shared between `MainForm` and `PlotPanel`, introduce the smallest helper needed.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-06 | Spec drafted | Pending | Implementation not started |

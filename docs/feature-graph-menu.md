# Feature Spec: Dedicated Graph Menu

## Summary

Add a top-level `Graph` menu to Libre Hardware Monitor and consolidate graph-specific controls into it.

Graphing is a primary workflow in this fork. Users should be able to find graph visibility, view reset, time window, location, and stroke thickness from one place without changing the dense sensor table or telemetry behavior.

## Current Problem

Graph controls are split across several places:

- `View > Show Plot`
- `View > Reset Plot`
- `Options > Plot Location`
- `Options > Stroke Thickness`
- `Options > Sensor Values Time Window`
- Sensor-tree checkboxes, which control graph inputs but are not obvious as graph controls

The features exist, but the organization makes graphing feel secondary and harder to explain.

## Goals

- Add a top-level `Graph` menu between `View` and `Options`.
- Use `Graph` terminology in new user-facing labels.
- Move existing graph-specific commands into the new menu.
- Preserve existing settings and selected graph sensors.
- Preserve the default dense sensor tree, gridlines, exact values, units, and min/max columns.
- Keep this fork's Compact Mode opt-in and outside the `Graph` menu.
- Keep graph behavior unchanged unless the user changes a graph option.

## Non-Goals

This feature does not redesign the application.

It must not:

- Reduce sensor table density.
- Remove compact mode.
- Remove table or graph gridlines.
- Hide current, min, or max values.
- Change polling semantics.
- Change logging semantics.
- Change sensor names, paths, or units.
- Smooth, average, downsample, or hide graph data as part of the menu change.
- Break existing graph sensor selections.
- Break remote web server or API behavior.

## Core Principle

Graph controls may become easier to access, but telemetry must not become less dense, less accurate, less precise, or less auditable.

The graph is an additional inspection view. It must not replace or weaken the dense sensor table.

## Menu Bar

Current:

```text
File   View   Options   Help
```

Target:

```text
File   View   Graph   Options   Help
```

`View` remains for general application display. `Graph` owns graph-specific display and input management. `Options` remains for broader application behavior.

## Phase 1 Scope

Phase 1 is intentionally narrow: add the `Graph` menu and move existing graph commands into it without changing behavior.

Target Phase 1 menu:

```text
Graph
  Show Graph
  Reset Graph View

  Time Window >
  Graph Location >
  Stroke Thickness >
```

### Command Mapping

| Old UI | New UI | Behavior |
| --- | --- | --- |
| `View > Show Plot` | `Graph > Show Graph` | Toggles graph visibility |
| `View > Reset Plot` | `Graph > Reset Graph View` | Resets graph viewport only |
| `Options > Sensor Values Time Window` | `Graph > Time Window` | Controls graph history window |
| `Options > Plot Location` | `Graph > Graph Location` | Controls graph placement |
| `Options > Stroke Thickness` | `Graph > Stroke Thickness` | Controls graph line thickness |

Existing setting keys may remain unchanged internally. This avoids migration risk and preserves user settings.

## Items That Stay Outside Graph

These are not graph-specific:

- `View > Reset Min/Max`
- `View > Expand All Nodes`
- `View > Collapse All Nodes`
- `View > Compact Mode`
- `View > Show Hidden Sensors`
- `View > Show Gadget`
- `View > Columns`
- `Options > Temperature Unit`
- `Options > Theme`
- `Options > Split Panel Scaling Mode`
- `Options > Log Sensors`
- `Options > Logging Interval`
- `Options > Update Interval`
- `Options > File Rotation Method`
- `Options > Remote Web Server`
- startup and minimize options
- hardware polling options such as force drive wakeup

Important distinction:

- `Update Interval` controls how often sensors are polled.
- `Time Window` controls how much graph history is displayed.
- `Logging Interval` controls how often values are written to logs.

These should not be merged.

## Phase 2: Graph Inputs Dialog

Add `Graph > Graph Inputs...`.

The dialog should provide a dense table for selecting graph sensors:

```text
Graph Inputs

Search: [________________________]

[ ] Show hidden sensors

On   Sensor                         Current Value   Unit   Type
x    CPU / Core #1 / Temperature    61.5            C      Temperature
x    CPU / Core #2 / Temperature    59.8            C      Temperature
     GPU / Hot Spot                 74.2            C      Temperature
     GPU / Fan                      1470            RPM    Fan
x    GPU / Power                    182.5           W      Power

[Clear All] [Select Visible] [Apply] [Close]
```

Requirements:

- Use the same graph selection state as the sensor-tree checkboxes.
- Do not introduce a second graph-selection system.
- Show dense rows and gridlines.
- Show full sensor paths where practical.
- Show current values while choosing graph inputs.
- Support search/filtering.
- Support quick selection and deselection.
- Respect hidden sensors or provide a clear equivalent toggle.

## Acceptance Criteria For Phase 2

- `Graph > Graph Inputs...` opens a dense graph-input selection dialog.
- The dialog uses the same selected-sensor state as the sensor-tree checkboxes.
- Checking or unchecking a sensor in the dialog updates the graph input state immediately.
- Existing selected graph sensors are preserved when the dialog opens.
- The dialog supports search/filtering.
- The dialog can include or exclude hidden sensors through a visible checkbox.
- The dialog shows current values, units, and sensor types while choosing inputs.
- `Clear All` clears graph inputs only; it does not hide sensors or reset min/max values.
- `Select Visible` selects the currently visible filtered rows as graph inputs.
- The main `Graph` menu includes a direct `Clear Graph Inputs` command.

## Future Phases

Phase 3: add graph-local controls in the graph panel. Draft spec:
[`feature-graph-panel-controls.md`](feature-graph-panel-controls.md).

Phase 4: improve long-window graph rendering while preserving spikes, such as min/max/latest envelope rendering. Do not add average-only visual compression that can hide excursions. Draft spec:
[`feature-long-window-graph-rendering.md`](feature-long-window-graph-rendering.md).

New graph feature: add local clock-time labels for the graph T axis as the default label mode, while keeping elapsed labels available. Spec:
[`feature-real-time-axis.md`](feature-real-time-axis.md).

## Acceptance Criteria For Phase 1

- Main menu contains `Graph` between `View` and `Options`.
- `Graph > Show Graph` toggles the existing graph visibility setting.
- `Graph > Reset Graph View` performs the existing graph reset behavior without resetting sensor min/max values.
- `Graph > Time Window` uses the existing graph history time-window setting.
- `Graph > Graph Location` uses the existing graph location setting.
- `Graph > Stroke Thickness` uses the existing graph stroke setting.
- Existing selected graph sensors are preserved.
- Existing settings are preserved.
- Compact Mode remains opt-in, stays in `View`, and is not controlled by the `Graph` menu.
- Default sensor table density is unchanged.
- Exact sensor values remain visible.
- Polling, logging, remote web server, and API behavior are unchanged.
- No graph data smoothing, averaging, downsampling, or rounding changes are introduced.

## Implementation Notes (post-delivery)

These notes reconcile the spec text above with what actually shipped (commits `7b0e079`, `1f4225c`, `bb432e1`, `dc424c5`).

- **Phases 1 and 2 shipped together.** The `Graph` menu therefore interleaves the Phase 2 commands; its real order is: `Show Graph`, `Graph Inputs...`, `Clear Graph Inputs`, `Reset Graph View`, separator, `Time Window`, `Graph Location`, `Stroke Thickness`. This is a superset of the Phase 1 target diagram, not a regression.
- **Compact Mode is a new local feature, not a pre-existing upstream behavior.** It is opt-in and defaults to off, so the dense, fully-auditable table remains the default. The `Graph` menu does not move or control Compact Mode. When enabled it intentionally hides the Min/Max columns and gridlines for density; the `Show Min` / `Show Max` menu items are disabled while compact is active so their checkmarks cannot misrepresent the actual column state.
- **Other local additions delivered in the same implementation series are documented in [`local-ui-customizations.md`](local-ui-customizations.md)** — multi-select hide/unhide, plot grid density, plot time-axis presets, the `Sensor.cs` averaging-accumulator fix, modernization changes, and the "Sev IQ" relabel. Those are outside this spec's scope but are recorded there for traceability.

## Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-06 | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 -p:OutDir="$env:TEMP\sq-librehw-verify\net10\"` | Pass | 0 warnings, 0 errors. Normal release output path was locked by a running `Libre Hardware Monitor` process. |
| 2026-06-06 | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 -p:OutDir="$env:TEMP\sq-librehw-verify\net472\"` | Pass | 0 warnings, 0 errors. |

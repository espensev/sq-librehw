# Feature Spec: Real-Time Time Axis

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Implemented  
**Updated:** 2026-06-06  
**Related docs:** `feature-graph-menu.md`, `feature-graph-panel-controls.md`, `local-ui-customizations.md`  
**Purpose:** make the graph T axis show real local clock time by default, with elapsed history time still available as an alternate mode.

## 1. Summary

Add a local-clock label mode for the graph T axis and make it the default. In this mode, graph labels show local wall-clock times for samples, such as `14:32`, while preserving the existing elapsed-window graph behavior and time-window presets. The graph should show what the displayed axis actually means, not hide behind relative labels by default.

## 2. Problem and Motivation

The current time axis is relative to now: data points are plotted by age in seconds. That is useful for seeing "how long ago" a point occurred, but it makes it harder to correlate sensor events with external events such as a game launch, benchmark run, log timestamp, or Windows event. Local-time labels are closer to the actual sample timestamps and make correlation easier without changing telemetry data.

## 3. Goals and Non-Goals

**Goals**

- Add a user-selectable time-axis label mode: local clock time vs elapsed time.
- Make local clock time the default label mode.
- Show labels that correspond as directly as possible to the local clock time of displayed samples.
- Keep elapsed/relative labeling available for users who prefer age-from-now labels.
- Keep existing time-window presets and zoom behavior.
- Keep graph data storage and sensor values unchanged.

**Non-goals**

- Do not change sensor sampling, polling, logging, or history retention.
- Do not change graph data values or sensor history semantics while changing axis labels.
- Do not add calendar/date-heavy labels for short windows unless needed for clarity.
- Do not change table values, min/max, or the remote web/API output.

## 4. Behavior Specification

The graph gains a time-axis label mode with two choices:

- **Local Time:** default behavior. Labels represent local wall-clock time for the corresponding sample position.
- **Elapsed:** legacy/alternate behavior. Labels represent elapsed history time.

Local Time is the default for missing settings and new installations because it displays the time axis as truthfully as possible. Elapsed remains available as an explicit mode for users who want relative age labels.

In Local Time mode, a point whose X position is 60 seconds from the live edge should label near the current local time minus 60 seconds, or the sample's own timestamp if the implementation can use it directly. The current `PlotPanel` data path computes X as age in seconds from a UTC refresh anchor; label formatting should map that age back to local time from the same refresh anchor. As the graph updates, labels scroll with the live data. Zoom and pan continue to operate on the same visible history window; only label formatting changes.

For short windows, labels should include seconds when the visible range is 2 minutes or less. For longer same-day windows, labels can use hours and minutes. If the visible range crosses a local date boundary, labels must include enough date context to avoid ambiguity. Use current-culture date/time formatting where practical.

Daylight-saving transitions may produce repeated or skipped local clock labels. That is acceptable as long as labels reflect local time and the graph does not crash.

The selected label mode should be available from the plot context menu under `Time Axis > Label Mode > Elapsed / Local Time`. If `feature-graph-panel-controls.md` is implemented, the same label-mode menu should also be available from the graph-local control.

If the persisted setting is missing, invalid, or from an older build, the app must use `LocalTime`.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Adds `Time Axis > Label Mode > Elapsed / Local Time` to the plot context menu and later graph-local controls. |
| Settings/config | Add persisted setting `plotTimeAxisLabelMode` with values `LocalTime` and `Elapsed`; default and invalid-value fallback is `LocalTime`. |
| Remote web/API | No change. |
| Logging/files | No change. |
| Hardware/admin flow | No change. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | Keep changes localized to `PlotPanel` axis formatting and menu creation. |
| `net472` vs `net10.0-windows` | Use OxyPlot axis formatting APIs that work in both targets. |
| DPI/multi-monitor | Verify labels do not overlap badly at common DPI scales; use adaptive formats. |
| Hardware/admin rights | Not affected. |
| Existing settings/users | Missing setting uses `LocalTime`; elapsed mode remains selectable if a user prefers the previous relative labels. |

## 7. Acceptance Criteria

- [x] The graph offers a Local Time vs Elapsed label mode. (`Time Axis > Label Mode` in the plot context menu)
- [x] Local Time is the default for missing settings and new installations. (`plotTimeAxisLabelMode` default `LocalTime`)
- [x] Local Time mode labels the T axis with local wall-clock time corresponding as closely as possible to displayed samples. (`FormatLocalTimeAxisLabel` maps each tick back to local time from the live `_now` anchor)
- [x] Elapsed mode remains available and matches current time-axis behavior. (`StringFormat = "h:mm"`, the original axis format)
- [x] The selected mode persists across app restart. (persisted via `UserRadioGroup` to `plotTimeAxisLabelMode`)
- [x] Time-window presets and zoom still work in both modes. (label mode only swaps the formatter; preset/zoom paths unchanged)
- [x] No sensor data, polling, logging, table values, or web/API behavior changes. (display-only formatter; no data-path edits)
- [ ] Missing or invalid persisted label-mode settings fall back to Local Time. **Partial:** unparseable and negative values resolve to `LocalTime`, but an out-of-range high value clamps to `Elapsed` (`UserRadioGroup` clamps to `menuItems.Length - 1`). Only `0`/`1` are ever persisted, so this is unreachable in normal use; tighten only if a third mode is added.
- [ ] Labels remain readable for 30-second, 10-minute, 1-hour, and 24-hour windows. Logic implemented (`HH:mm:ss` for ≤ 2 min, else `HH:mm`); awaiting runtime visual spot-check (§11).
- [ ] A visible window that crosses midnight includes date context in Local Time mode. Logic implemented (`VisibleTimeAxisCrossesLocalDate` → `M/d HH:mm`); awaiting runtime visual spot-check (§11).

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` | 0 errors |
| Runtime smoke | Launch app, show graph, confirm default labels | T axis uses local clock time by default |
| Mode switch | Switch between Local Time and Elapsed | Axis label mode changes without affecting plotted data |
| Persistence | Select Elapsed, close app, relaunch, then switch back to Local Time | Selected mode persists and both modes remain available |
| Fallback | Remove or corrupt `plotTimeAxisLabelMode`, relaunch | Local Time mode is selected |
| Window coverage | Check 30s, 10m, 1h, and 24h windows | Labels remain useful and readable |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| Exact format strings | Implementation | Current-culture short time with seconds for <= 2m ranges; include date when the local date changes across the visible range |
| Menu location in main `Graph` menu | Spec acceptance | Keep in plot context menu first; add through graph-local controls when that feature lands |
| Use OxyPlot `TimeSpanAxis` formatter or switch to `DateTimeAxis` | Implementation | Prefer formatting the existing relative axis to minimize data-path changes |

## 10. Implementation Notes

Current `PlotPanel` uses `TimeSpanAxis` with X values computed from `(_now - value.Time).TotalSeconds`. The lowest-risk implementation is likely a custom label formatter that maps relative seconds back to local time using the current live timestamp, rather than rewriting the series to `DateTimeAxis`.

Implemented in `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs` using the existing `TimeSpanAxis` and a `LabelFormatter`. The new persisted setting is `plotTimeAxisLabelMode`, with `LocalTime` as index `0` and `Elapsed` as index `1`. Missing or unparseable stored values fall back to `LocalTime`, and `UserRadioGroup` clamps to the valid index range (a negative value resolves to index `0` = `LocalTime`). Note an out-of-range *high* value would clamp to `Elapsed` (index `1`), not `LocalTime`; this is unreachable in practice because only `0`/`1` are ever written.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-06 | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 -p:OutDir="$env:TEMP\sq-librehw-verify\net10-localtime\"` | Pass | 0 warnings, 0 errors |
| 2026-06-06 | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 -p:OutDir="$env:TEMP\sq-librehw-verify\net472-localtime\"` | Pass | 0 warnings, 0 errors |
| 2026-06-06 | `dotnet build ... -f net10.0-windows` and `... -f net472` (Release x64, **normal output path**, running app closed for the rebuild) | Pass | Both targets: build succeeded, 0 warnings / 0 errors. Confirms the build at the real output path, superseding the temp-`OutDir` workaround. |
| 2026-06-06 | Manual runtime UI check | Pending | Need launch with this build and confirm default Local Time labels, menu switching, and persistence |

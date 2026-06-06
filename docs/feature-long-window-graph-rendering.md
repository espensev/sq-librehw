# Feature Spec: Long-Window Graph Rendering

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Draft  
**Updated:** 2026-06-06  
**Related docs:** `feature-graph-menu.md`, `local-ui-customizations.md`, `feature-workflow.md`  
**Purpose:** improve graph responsiveness for long time windows without hiding displayed values or weakening sensor history semantics.

## 1. Summary

Improve plot rendering when long time windows or many selected sensors produce too many points to draw smoothly. The default goal is to display values as truthfully as possible. The rendering path may reduce the number of plotted primitives only when it still shows what is being displayed on the axis and preserves visible excursions such as spikes, drops, and latest values.

## 2. Problem and Motivation

Long graph windows can accumulate many sensor samples. Drawing every point for every selected sensor can make the UI sluggish, especially at 12-hour and 24-hour windows. A naive average-only downsample would make the graph faster but can hide important short spikes. This fork values auditable telemetry, so performance improvements must preserve extremes and must not silently make the axis imply more detail or precision than is actually being rendered.

## 3. Goals and Non-Goals

**Goals**

- Keep long-window graphs responsive with many points and multiple selected sensors.
- Prefer raw, true point rendering whenever it is practical.
- Preserve spikes, drops, latest points, and visible min/max excursions.
- Make any optimized/envelope rendering honest about what is displayed.
- Leave raw `ISensor.Values` unchanged.
- Keep logging, web/API, Prometheus, min/max table values, and sensor polling unchanged.
- Use a rendering-only optimization only when plotted point count exceeds a threshold and the optimized representation still preserves visible truth.

**Non-goals**

- Do not smooth, average-only, interpolate away, or otherwise hide excursions.
- Do not hide a reduced/envelope rendering mode from the user if it materially changes how the plot should be read.
- Do not change the sensor sampling interval.
- Do not delete historical samples earlier than the configured time window.
- Do not change the meaning of current, min, max, or logged values.
- Do not add a second graph data store unless a later implementation note justifies it.

## 4. Behavior Specification

The graph keeps using the same sensor history source. On each plot refresh, snapshot the selected sensor's current `sensor.Values` sequence for rendering. When a selected sensor has few enough points for the current plot width, render the raw points as today. Raw point rendering is the preferred and most truthful display mode.

When the selected sensor has more points than are useful for the visible plot width, the UI rendering path may bucket samples by horizontal pixel or by a comparable visible-domain interval. In that case the plotted representation is an envelope of the data in each visible interval, not an average. Use the actual plot area width when available; if the plot width is unavailable or invalid, fall back to raw rendering. The initial optimization threshold should be conservative, for example only when raw points per series exceed `max(2 * plotAreaPixelWidth, 2000)`.

Each bucket must preserve at least:

- minimum value in the bucket;
- maximum value in the bucket;
- latest value in the bucket, if different from the min/max points;
- first/last continuity points where needed to avoid misleading line breaks.

Preserved points must keep their original timestamp/X coordinate and be emitted in original time order. Do not reorder points as "min then max" when the actual sample order was different.

The rendered series must make spikes and drops visible even when they occur between major gridlines or between displayed ticks. If a bucketed/envelope path is active, the graph UI should expose that fact in a lightweight way, such as a tooltip, status text, or graph-local menu state, so the user can tell whether they are viewing raw points or an extreme-preserving rendered envelope.

This is a visual rendering optimization only. It must not mutate `ISensor.Values`, alter `_sum` / `_count` averaging state, or affect any non-plot consumers.

If the optimization cannot safely preserve extremes for a sensor type or axis state, fall back to raw rendering for that series.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Optional indicator or menu state may show raw vs envelope rendering when optimization is active. |
| Settings/config | No setting required for the first implementation; if added, default must prefer truthful raw/envelope rendering, never average-only hiding. |
| Remote web/API | No change. |
| Logging/files | No change. |
| Hardware/admin flow | No change. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | Keep optimization inside `PlotPanel` rendering/data preparation; avoid changes to `LibreHardwareMonitorLib` unless required. |
| `net472` vs `net10.0-windows` | Use framework-compatible collections and OxyPlot APIs. |
| DPI/multi-monitor | Bucket by actual plot area width, not raw form width. Verify at common DPI scales. |
| Hardware/admin rights | Not affected. |
| Existing settings/users | No migration required; graph history settings keep their current meaning. Raw rendering remains the behavior when point counts are practical. |

## 7. Acceptance Criteria

- [ ] At short time windows and small point counts, rendering matches raw-point behavior.
- [ ] Raw point rendering remains the default whenever it is practical.
- [ ] At long time windows, visible spikes and drops remain visible after optimization.
- [ ] If envelope rendering is active, the UI exposes that the graph is showing an extreme-preserving envelope rather than raw individual points.
- [ ] The latest point for each selected sensor is still represented.
- [ ] Preserved bucket points retain original time order.
- [ ] `ISensor.Values`, logging, web/API, Prometheus, current/min/max table values, and polling behavior are unchanged.
- [ ] The graph remains responsive with a 24-hour window and a representative multi-sensor selection.
- [ ] No average-only downsampling is introduced.
- [ ] A deterministic synthetic series with a one-sample spike and a one-sample drop still renders both excursions after optimization.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` | 0 errors |
| Runtime smoke | Launch app, select several sensors, switch to 12h/24h time windows | App remains responsive and graph indicates raw/envelope rendering when relevant |
| Spike preservation | Use a controlled or reviewed data path with short excursions | Min/max excursions remain visible |
| Bucket helper check | If implementation introduces a pure bucketing helper, verify it with a synthetic spike/drop/latest series | Min, max, latest, and time order are preserved |
| Regression check | Compare raw sensor values, table min/max, logs/API before and after | Non-plot data semantics are unchanged |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| Threshold tuning | Implementation | Start only above `max(2 * plotAreaPixelWidth, 2000)` points per series; adjust only with verification evidence |
| Series shape | Spec acceptance | Use first/last/min/max/latest bucket points rather than average-only lines |
| User toggle | Spec acceptance | No toggle for first version; a visible indicator is enough unless comparison needs a toggle |

## 10. Implementation Notes

The current plot path builds OxyPlot `LineSeries` from `sensor.Values` in `PlotPanel.SetSensors`. That is the likely place to prepare an extreme-preserving visible series without changing library data.

No dedicated test project exists in this repository today. If this feature introduces a pure helper, prefer adding focused automated coverage for the helper. If that is disproportionate, record the synthetic manual validation data and result in this spec's verification log.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-06 | Spec drafted | Pending | Implementation not started |

# Independent Text Scaling: Sensor Pane vs Graph

Status: DRAFT (design approved pending operator review, 2026-07-18)

## Problem

The View → Text Size slider drives one scale (`uiTextScale`) that resizes the sensor
tree, menus, columns and glyphs — and also the plot: axis tick labels, axis titles,
tick density (`IntervalLength`) and the hover tracker. Raising it for sensor-text
readability visibly changes the rendered graph (fewer ticks, axis labels consuming
plot area). The operator wants large sensor text with an unscaled graph, while
keeping a way to enlarge the x/y axis text independently when wanted.

## Design

Two independent, persisted scales, both clamped by `UiScale.ClampPercent` (75–250%):

| Scale | Setting key | Controls | Drives |
|---|---|---|---|
| UI text | `uiTextScale` (existing) | View → Text Size slider | Sensor tree font, menu font, column widths, glyphs, row heights, **plot hover tracker** |
| Graph text | `plotTextScale` (new, default 100) | Graph → Graph Text Size slider (new) | Plot axis tick labels, axis titles, tick-density compensation (`IntervalLength`) |

Decisions:

- **Tracker follows UI text.** The cursor tooltip is reading text, like the tree;
  the rendered graph and its axes stay compact regardless.
- **`plotTextScale` defaults to 100%** on first run after upgrade. The graph
  returns to its unscaled look even if `uiTextScale` is high — this is the point
  of the feature. `uiTextScale` migration: none needed.
- **Both sliders share the debounce/defer machinery** from the slider-glitch fix:
  per-tick only a fixed-size readout label updates; heavy work commits via
  `UiTextScaleCommitGate` after a 150 ms pause; menu-strip label/font mutations
  defer to menu close. Each slider owns its own gate + timer instance.

## Components

- `PlotPanel`: split `SetAxisTextScale(int)` into
  - `SetAxisTextScale(int)` — axis `FontSize`/`TitleFontSize` + `IntervalLength` only;
  - `SetTrackerTextScale(int)` — tracker font (`_plot.Font`) only.
  Each keeps its own captured base font/percent; both end with `InvalidatePlotCosmetic()`.
- `MainForm`: extract a private slider-dropdown builder used by both menus
  (trackbar config, readout label, host, keep-open handler, gate, timer), with
  callbacks for preview (readout) and commit (deferred-menu-aware apply).
  New field `_plotTextScalePercent`, loaded/saved like `_uiTextScalePercent`.
  `ApplyUiTextScale` calls `SetTrackerTextScale(_uiTextScalePercent)`;
  a new `ApplyPlotTextScale` calls `SetAxisTextScale(_plotTextScalePercent)`
  and updates the Graph menu item text ("Graph Text Size (N%)") on full commit.
- `UiScale` / `UiTextScaleCommitGate`: unchanged, reused.

## Testing

- Existing `UiScaleTests` and `UiTextScaleCommitGateTests` cover the shared math
  and commit policy.
- New tests (TDD): PlotPanel axis-vs-tracker split — `SetAxisTextScale` must not
  touch `_plot.Font`; `SetTrackerTextScale` must not touch axis font sizes or
  `IntervalLength`; both percents clamp via `UiScale`.
- Live visual gate (operator): high UI text + 100% graph text → big tree text,
  unscaled graph; graph slider alone enlarges axis text only; both dropdowns
  drag without jitter.

## Out of scope

- Web dashboard / telemetry console scaling (separate surface).
- Per-axis or legend-specific sizing.
- Any change to `lib/` or the `data.json` contract (UI-only feature).

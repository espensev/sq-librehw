# Feature Spec: Graph/sensor-tree UI review fixes (2026-06-07 sweep)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Implemented <!-- Draft | Accepted | Implemented | Verified | Done -->
**Updated:** 2026-06-07
**Related docs:** [`feature-workflow.md`](feature-workflow.md), [`local-ui-customizations.md`](local-ui-customizations.md), [`feature-graph-menu.md`](feature-graph-menu.md)
**Purpose:** fix five defects found by an adversarially-verified multi-agent review of the fork's local additions.

## 1. Summary

A review sweep of the fork's local additions (beyond the already-fixed `HttpServer`/`Logger`/`NvidiaGpu`)
produced five confirmed findings — one persisted-UI-state regression and four quality/cosmetic issues.
All five are fixed here. No data-contract surface (`data.json`, CSV, identifiers) is touched.

## 2. Problem and Motivation

The confirmed findings (all adversarially verified, 0 refuted):

1. **Column resizes silently lost (medium, regression).** In non-compact mode, `ApplySensorTreeLayout`
   reset the Value/Min/Max column widths to a startup snapshot on every Show Value/Min/Max toggle, and
   `SaveConfiguration` then persisted the reverted width — a user's column drag was lost permanently.
2. **BindingList subscription leak (low).** `GraphInputsForm.RebuildFilter` built a fresh `BindingList`
   over the same `INotifyPropertyChanged` rows on every keystroke/toggle; the old list was never
   cleared, so `PropertyChanged` hooks accumulated for the dialog's session.
3. **Plot recompute double-fire / fan-out (low, perf).** A single Graph-Inputs checkbox toggle ran the
   expensive `PlotSelectionChanged` recompute twice (`SensorNode.Plot` event + the dialog callback);
   bulk Clear All / Select Visible ran ~N recomputes despite the "notify exactly once" intent.
4. **Duplicate minute labels (low, cosmetic).** With the new defaults (Fine grid + Local-Time labels),
   sub-minute gridline spacing at common zooms rendered repeated adjacent `HH:mm` labels.
5. **Dead tie-break (nit).** `GetNiceAxisStep` used `double.Epsilon` as a tolerance, so its "prefer
   smaller step on a near-tie" branch never fired.

## 3. Goals and Non-Goals

**Goals:** fix all five with minimal, behavior-preserving changes; keep non-dialog and non-compact paths
identical to before.

**Non-goals:** no change to data.json/CSV/Prometheus/identifiers; no refactor of the SensorNode plot
event system; no change to the graph menu surface or settings keys.

## 4. Behavior Specification

- **#1 `MainForm.ApplySensorTreeLayout`.** The non-compact `else` branch restores the saved column
  widths only when actually leaving compact mode (`if (_compactLayoutActive)`; the flag still holds the
  prior state, updated afterwards). `RowHeight`/`GridLineStyle` restoration stays (idempotent). Live
  user resizes in normal mode are now preserved across visibility toggles and across sessions.
- **#2 `GraphInputsForm`.** A single reusable `BindingList<GraphInputRow>` (`_visibleRows`) is the
  `BindingSource.DataSource`, set once. `RebuildFilter` repopulates it in place (suppress events →
  `Clear()` → `Add()` → re-enable → `ResetBindings()`). `Clear()` unhooks each item's `PropertyChanged`
  and `Add()` rehooks, so subscriptions stay balanced — no orphaned subscribed lists.
- **#3 `MainForm.PlotSelectionChanged` + `ShowGraphInputsForm`.** A new `_suspendPlotSelectionChanged`
  flag (default false) is set while the dialog is open; the dialog's callback temporarily lifts it to
  perform exactly one recompute (plus `treeView.Invalidate()`) per user action. This suppresses the
  per-node `SensorNode.Plot` events for both single-toggle and bulk operations. `PlotSelectionChanged`
  only *reads* `PenColor` (never assigns), so it is not re-entrant and the forced recompute runs with
  the guard down — identical to upstream. Non-dialog callers (tree right-click, hardware add/remove,
  `ClearGraphInputs`) are unaffected (flag false).
- **#4 `PlotPanel.FormatLocalTimeAxisLabel`.** Uses `HH:mm:ss` when `_timeAxis.ActualMajorStep` is a
  finite value in `(0, 60)` seconds (in addition to the existing `range <= 120s` case), so sub-minute
  gridlines get distinct labels. Guards no-op on the first frame when `ActualMajorStep` is 0/NaN.
- **#5 `PlotPanel.GetNiceAxisStep`.** Tie-break uses an absolute tolerance `1e-9`; first-iteration init
  is preserved (`score < +Infinity - 1e-9` is true).

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Column widths persist correctly; time-axis labels distinct at fine zoom; Graph Inputs dialog recomputes once per action. No new controls. |
| Settings/config | None (no keys added/changed). #1 makes the *existing* `treeView.Columns.*.Width` persistence behave as intended. |
| Remote web/API | None. |
| Logging/files | None. |
| Hardware/admin flow | None. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | All edits are in already-fork-local methods/files (graph menu, compact mode, Graph Inputs, plot panel). |
| `net472` vs `net10.0-windows` | Framework-agnostic; both targets build clean. |
| Plot event re-entrancy (#3) | `PlotSelectionChanged` reads but never assigns `PenColor`; not re-entrant. Guard only collapses redundant entries; forced recompute runs guard-down = upstream behavior. |
| Dialog correctness (#3) | Every change path (single toggle, Apply, SetRows bulk) ends in one callback recompute; dialog is modal so no other recompute source is starved. |

## 7. Acceptance Criteria

- [x] Both `net10.0-windows` and `net472` Release x64 build with 0 warnings / 0 errors.
- [ ] (manual) #1: drag the Value column wider → toggle Show Min → width holds; restart → width persists.
- [ ] (manual) #3: open Graph Inputs, toggle a row and use Clear All / Select Visible → plot updates correctly, once per action.
- [ ] (manual) #4: on the default view, the time axis shows no repeated adjacent `HH:mm` labels.
- [x] Non-compact/non-dialog behavior unchanged (code reasoning).

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Build modern app | `dotnet build ... -f net10.0-windows -c Release -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build ... -f net472 -c Release -p:Platform=x64` | 0 errors |
| Smoke launch | relaunch app; web server still serves `data.json` | app runs, no crash |
| Manual #1/#3/#4 | the three manual steps above | as described |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| File the five findings as GH trackers for downstream paper trail | optional | not filed (fixed directly per maintainer choice "fix all 5") |

## 10. Implementation Notes

- `MainForm.cs`: `_suspendPlotSelectionChanged` field; guard at top of `PlotSelectionChanged`;
  `ShowGraphInputsForm` sets/clears it; `ApplySensorTreeLayout` else-branch width restore gated on
  `_compactLayoutActive`.
- `GraphInputsForm.cs`: `_visibleRows` BindingList reused; `RebuildFilter` clears/repopulates in place.
- `PlotPanel.cs`: `FormatLocalTimeAxisLabel` sub-minute `ActualMajorStep` check; `GetNiceAxisStep`
  `1e-9` tie tolerance.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-07 | `-f net10.0-windows` + `-f net472` Release x64 (compile-check, redirected OutDir) | pass | 0 warnings / 0 errors |
| 2026-06-07 | code reasoning (advisor-reviewed design for all five) | pass | **GUI-interaction paths not exercised by automated checks** — manual checklist in §7 outstanding; do not treat as runtime-verified to the standard of the curl/CSV checks used for the web-server / identifier fixes |

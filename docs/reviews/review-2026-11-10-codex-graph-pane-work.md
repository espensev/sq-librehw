# Review - Codex graph pane work and open menu issues

**Date:** 2026-11-10
**Surface:** `baca022a..e890693d` on `master` (`1bf2655f`, `71bfcdfe`, `f793f738`, `e890693d`); the session handoff attributes this range to Codex. Also re-statused: the untracked [pane-controls and repo-issues audit](review-2026-11-10-pane-controls-and-repo-issues.md) (written at `71bfcdfe`) and the external menu-UX audit of the live `1bf2655f` menus (workload root, `LibreHW/audits/2026-11-10-menu-ux/review.html`).
**Spec source:** `docs/features/feature-graph-pane-controls.md` (current source contract and historical sections)
**Standards sources:** `AGENTS.md`, `docs/README.md`, `docs/architecture/upstream-divergence.md`, `eng/ci/README.md`
**Verdict:** PASS WITH NOTES - the code is sound and its claims reproduce; one behavior regression needs an operator decision, current-state docs are still stale, and the menu-design defects reported by the menu-UX audit remain open in source.

## Findings

### High

No findings.

### Medium

- [axis: regression] `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs:1493-1498` - In overlay mode (`Stacked Axes` off) with the default `This pane` scope, no wheel gesture over the plot area zooms anything.
  Evidence: `ZoomTargets` returns when `laneKey` is null, and `GetLaneKeyAt` returns null for the overlay plot area (`:658`); a time gesture also returns when `!_stackedAxes.Value` outside `All panes`. Only the value-axis label tiers still zoom. The live `1bf2655f` build zooms the shared time window from the overlay plot area (spec "Mouse gestures" table, now historical).
  Impact: an overlay user loses wheel time zoom with no feedback unless they find `Zoom scope > All panes`. The spec's current contract sanctions it ("empty overlay space must not silently expand a local gesture"), but the "Compatibility and risk" behavior-change list still describes the old overlay behavior, so the operator never saw this consequence stated.
  Recommendation: operator decision. Either treat overlay's single shared time axis as the local target for `Shift` + wheel, or record the behavior change. Low impact on SND-DESK, which runs stacked.
- [axis: standards] Current-state documents still contradict the runtime manifests (carried over from the preserved audit; `e890693d` edited neighbouring docs but not these).
  Evidence: `docs/features/feature-local-release-system.md:8` says master `aa95772b` is installed; `docs/features/feature-memory-ui-reliability.md:10` says `9e68b348` holds the rollback slot, and `:550` is premised on `aa95772b` being installed; `docs/features/feature-upstream-sync-2026-10-19.md:105` says the runtime stays at `aa95772b`. The SND-DESK runtime manifest declares `1bf2655f` and its rollback manifest `aa95772b`. `docs/README.md:94-97` forbids pinning release identifiers in current-state documents.
  Impact: a reader trusts the wrong installed and rollback builds.
  Recommendation: replace the pinned identifiers with version-free wording that points at the manifests.
- [axis: spec] Right-click and gear menus (`PlotPanel.cs:825-971`, `UI/Themes/ThemedToolStripRenderer.cs`, `UI/ToolStripRadioButtonMenuItem.cs`, `UI/PlotRangePrompt.cs`, `UI/MainForm.cs:2462`) - the menu-design defects the menu-UX audit reported against the live build are still present at `e890693d`, which changed gestures, not menu design.
  Evidence: the check glyph is a fixed 7 px shape at `(10, 6)` regardless of row height or DPI (`ThemedToolStripRenderer.cs:27-38`); radio items paint the OS-themed glyph, which is light on the dark menus (`ToolStripRadioButtonMenuItem.cs:170`; scope captures in the continuation evidence); separators are filled with background (`:8-19`); the pane header is a disabled item, drawn in `SystemColors.GrayText`; the pane right-click appends the whole graph-wide menu including `Lanes`; `Height` stays enabled in overlay mode; `Reset Graph View` calls `ClearValues()` on every sensor; the range prompt uses fixed pixel geometry with a DPI-scaled font, which clips `Minimum`, `Maximum`, `OK` and `Cancel` at the live scale (capture `05-set-range-dialog.png`), and `PlotLaneNamePrompt.cs` repeats the pattern.
  Impact: the operator's current complaint ("the right click menu design").
  Recommendation: fix in the next slice under a spec revision (see the pane-controls spec, "Menu design revision").

### Low

- [axis: spec] `docs/features/feature-graph-pane-controls.md` - The spec holds two contracts: the current wheel/scope contract at the top and the shipped sections below (Ctrl + wheel, zoom rectangle and overlay rows in the gesture table), joined by a precedence sentence. Replace superseded sections in place when the menu revision lands rather than stacking a third contract.
- [axis: regression] `PlotPanel.cs:975-1030` - The options button became a permanent text label (`Zoom: This pane  ⚙`) over the plot's top-right corner, where the newest samples of the top pane are drawn. The removed comment warned about narrow placements; the fixture harness checked only a 900 px panel. Recheck at narrow widths with the menu work.
- [axis: standards] `docs/architecture/upstream-divergence.md:136` - The `PlotPanel.cs` row text describes `e890693d` but its commit list stops at `1bf2655f`. The gate checks paths, so it passes.
- [axis: standards] `eng/ci/README.md:55-67` - The gate-classification table lists 11 gates; `Invoke-LhmGates.ps1:73` defines a twelfth, `pawnio-portable`, which only the prose at `:214` mentions.
- [axis: standards] `docs/campaign-backlog.md` header says `Updated: 2026-08-05` though the file changed on 2026-10-27; `docs/README.md:192` and `.codex/skills/project.toml` still present `live-tracker.md` (content ends 2026-08-03) as current.

### Re-status of the preserved audit (written at `71bfcdfe`)

| Finding | Status at `e890693d` |
|---|---|
| Release-system status line names `aa95772b` (High) | Open - Medium above |
| Tracker gestures throw before the next paint (Medium) | Resolved: `PlotLineSeries.GetNearestPoint` (`UI/PlotTimeAxes.cs:367-373`) skips unprepared series for every caller; `Tracker_AfterSeriesReplacementBeforePaintSkipsUnpreparedLinesAndRecovers` covers plain, Ctrl and Shift left-click |
| Memory-UI spec rollback/precondition lines (Medium) | Open - Medium above |
| `live-tracker.md` presented as current (Medium) | Open - Low above |
| Unified dashboard HTML in the runtime workspace (Medium) | Open; live-machine file, outside the repository, not changed by this review |
| Reflection lookup for `ActualPoints` (Low) | Resolved: the reflection was removed |
| Upstream-sync spec runtime line (Low) | Open - Medium above |
| `AGENTS.md` / `docs/README.md` update headers (Low) | Resolved: both now say 2026-11-10 |
| Divergence row commit list (Low) | Partly resolved: now lists `baca022a`, `1bf2655f`; `e890693d` missing |
| CI gate table missing `pawnio-portable` (Low) | Open |
| Campaign-backlog header date (Low) | Open |
| Candidate store, retired `hidelaunch.exe`, `specs new` folder (hygiene) | Open; live-machine files, not changed |
| Hosted run IDs for `71bfcdfe` unverifiable (Info) | Superseded: the `e890693d` runs are verified below |

## Verification

- `git rev-parse HEAD` - `e890693d2fc4ea63715dfd7ed3e0ca4d8fcc4467`.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64` at HEAD - pass: Contracts 124, Library 193, Application 389 passed with the one established opt-in skip (706 total), matching the handoff.
- `dotnet build ... -c Release -f net10.0-windows -p:Platform=x64` and `-f net472` - pass, 0 warnings, 0 errors each.
- `gh run view 42744309568 --repo espensev/sq-librehw` and `gh run view 42744312583 --repo celine-anime/librehw-host` - both `completed` / `success` on `e890693d`.
- Test-diff audit `f793f738..e890693d`: 41 to 50 facts/theories. Every removed or renamed test maps to a removed binding or a contract change: `ShiftWheel_ZoomsOnlyHoveredValueAxis` to `Wheel_...` (Shift is now time), `Wheel_Ctrl...Detaches...` to `Wheel_Shift...`, both Auto shared-window tests to the new "detach keeps every plotted series" contract, and the three zoom-rectangle tests to `UnassignedMouseZoomBindings_LeaveEveryAxisUnchanged` (the rectangle is unbound). No assertion was weakened to fit unchanged behavior.
- Keyboard menu path: a scratch x64 WinForms probe sent `Shift` + `F10` through `DefWindowProc`; `WM_CONTEXTMENU` arrived with `lParam = 0xFFFFFFFFFFFFFFFF`, so `PanePlotView.WndProc`'s `LParam == new IntPtr(-1)` test matches real keyboard invocation on x64.
- Runtime manifests read: installed `1bf2655f` (`9b924116...8401c`), rollback `aa95772b` (`eea0e775...8403b1`).
- Upstream-divergence gate not re-run for this report (no tracked change); it runs with the next code change.

## Coverage notes

- Deeply reviewed: the `e890693d` diffs of `UI/PlotPanel.cs` and `UI/PlotTimeAxes.cs` (binding set, `ZoomTargets` capture/dedup/anchor math, `GetZoomLaneKeyAt`, `PlotLineSeries`, keyboard menu path, options button); test-method changes in `PlotPanelPaneControlsTests.cs` and `PlotPanelLaneTests.cs` with two new tests read in full; `1bf2655f`'s Set Range float check via the spec log and preserved audit; the current menu code (`PlotPanel.cs:356-660, 825-1060`), renderer, radio item and both prompts.
- Sampled: the documentation diffs of `71bfcdfe`, `f793f738` and `e890693d` (status lines, verification logs, divergence rows). The committed continuation review was read in full.
- Not re-reviewed: `baca022a` itself (reviewed in the preserved audit and the spec's review-fix log) and the upstream merge carried by the promoted range.

## Open questions

- Overlay wheel behavior (Medium above): keep the strict local contract, or let `Shift` + wheel act on overlay's single shared time axis?
- Should the persistent scope label show only while `All panes` is active? The current contract keeps it visible in both states.

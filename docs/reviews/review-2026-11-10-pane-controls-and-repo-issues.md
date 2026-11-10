# Review - Graph Pane Controls Promotion and Repo Issues

**Date:** 2026-11-10
**Surface:** commits `baca022a`, `1bf2655f` (pane-controls code), `71bfcdfe` (promotion record) in `E:/Monitoring/sq-librehw`; deployed workspace `E:/Monitoring/LibreHW`; repo-wide hygiene sweep
**Spec source:** `docs/features/feature-graph-pane-controls.md`
**Standards sources:** `AGENTS.md`, `docs/README.md`, `docs/architecture/upstream-divergence.md`, `docs/features/feature-local-release-system.md`
**Verdict:** PASS WITH NOTES — the promotion itself is sound (identity, hashes, rollback, live health all verified); findings below warrant a docs-fix pass and one small code hardening, not a rollback.

## Findings

### High

- [axis: standards] `docs/features/feature-local-release-system.md:8` - Status line still says master `aa95772b` is installed on SND-DESK since 2026-10-27 with `9e68b348` in the rollback slot.
  Evidence: `E:/Monitoring/LibreHW/Runtime/release.json` = `0.9.6_1bf2655...` commit `1bf2655f185b...`; `Runtime/rollback/release.json` = `0.9.6_aa95772` (aa95772b). Contradicts `AGENTS.md:75`, `docs/README.md:185`, and the pane-controls spec, and violates the repo's own policy at `docs/README.md:94-97` (do not pin release identifiers in current-state documents). `71bfcdfe` updated three docs but missed this one.
  Impact: a reader of the release-system spec believes both the runtime and rollback contents are the 2026-10-27 pair; both halves are wrong.
  Recommendation: rewrite the status line without pinned identifiers (state the machine matrix as authority), or update the pair.

### Medium

- [axis: regression] `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs:684-687` - The `1bf2655f` null-`ActualPoints` guard covers only the fork's own `GetSensorAt` hit test. OxyPlot's default tracker bindings (plain left-click `SnapTrack`, Ctrl+left `Track`, Shift+left `PointsOnlyTrack`) are never unbound — the custom controller at `PlotPanel.cs:167-193` rebinds wheel, pan, and zoom-rectangle only (no `Unbind`/`RemoveBinding` in the file) — and `TrackerManipulator` calls `GetNearestPoint` on every visible series. A left press/drag arriving after `SetSensors` replaces series (`PlotPanel.cs:1296-1321`, reached by the sensor-tree checkbox or the new Remove from Graph menu) but before the next `Update`/paint throws `ArgumentNullException` — the exact crash `1bf2655f` describes closing. Mouse input preempts the lazily generated `WM_PAINT`, so the window is real.
  Impact: unhandled exception from an ordinary gesture immediately after un-plotting/re-plotting a sensor. Hazard class predates the feature; the fix commit claimed more than it delivered.
  Recommendation: guard or route the tracker path the same way (e.g. a custom track command that skips null-`ActualPoints` series), plus a fixture test driving the tracker in the pre-paint window.
- [axis: standards] `docs/features/feature-memory-ui-reliability.md:10,550` - Stale rollback/current-state claims: line 10 says `9e68b348` "now holds the one rollback slot" (it is `aa95772b`); the open checklist item at line 550 is premised on "now that `aa95772b` is installed (2026-10-27)".
  Evidence: `Runtime/rollback/release.json` declares `0.9.6_aa95772`; live release.json declares `1bf2655f`.
  Impact: the open item is still valid work but its stated precondition no longer describes reality.
  Recommendation: reword to version-free preconditions.
- [axis: standards] `live-tracker.md` (repo root) - ~3 months stale (last commit `5ad10473`, 2026-08-05; content ends at Plan-013, 2026-08-03) yet still referenced as current by `docs/README.md:192` and `.codex/skills/project.toml`; `AGENTS.md`'s source-of-truth map omits it.
  Impact: a superseded campaign-era archive presented as a live tracker; three months of shipped work (fleet view, pane controls, upstream sync, recovery merge) absent from it.
  Recommendation: add a one-line superseded-by note or retire the references.
- [axis: regression] `E:/Monitoring/LibreHW/UnifiedDashboard/index.html` - Stale superseded artifact: byte-identical to `ops/deploy/snd-desk/Unified-LibreHardwareMonitor.html`, loads `https://snd-desk...ts.net:9443/` and `https://snd-host...ts.net:8443/` iframes whose owning spec (`feature-unified-tailnet-monitoring-view.md`) is marked "superseded; not live" since 2026-10-23; its replacement (fleet view) was promoted 2026-10-27. Nothing else in the workspace references the file.
  Impact: opening it shows a permanently broken pane and misrepresents the live dashboard (embedded fleet view on port 18085).
  Recommendation: archive or delete from the deployment root.

### Low

- [axis: regression] `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs:36-39` - The `ActualPoints` guard is a one-time reflection lookup by property name. If an OxyPlot upgrade renames the property, the guard silently becomes null and the pre-paint crash returns with no diagnostic and no test pinning the reflection target.
  Recommendation: assert/log when the property lookup fails, or pin against the OxyPlot version.
- [axis: standards] `docs/features/feature-upstream-sync-2026-10-19.md:105` - "The installed SND-DESK runtime stays at `aa95772b`" with no supersession note for the 2026-11-10 promotion.
- [axis: standards] `AGENTS.md:5`, `docs/README.md:4` - "Updated: 2026-10-27" headers predate the 2026-11-09/10 pane-controls edits in the same files.
- [axis: standards] `docs/architecture/upstream-divergence.md:136` - `PlotPanel.cs` row text covers pane controls and links the spec, but the commit provenance list stops at `fbb3a3ea`; `baca022a`/`1bf2655f` are absent (the gate checks paths, not commits, so it passes).
- [axis: standards] `eng/ci/README.md` gate-classification table lists 11 gates; `.codex/skills/project.toml` and `Invoke-LhmGates.ps1:73` define 12 (`pawnio-portable` missing from the table).
- [axis: standards] `docs/campaign-backlog.md` header says "Updated: 2026-08-05" but the file was edited by `5ef5f815` on 2026-10-27.
- [axis: hygiene] `E:/Monitoring/LibreHW/Candidates/` - 8 stale/unpromoted candidates (~88 MB of the 129 MB workspace): 20260930 test, 20261002 merge/release, 20261007 psu x3, 20261008 corsair-failure, 20261023 9e68b348 (twice-displaced rollback). No pruning policy applied.
- [axis: hygiene] `E:/Monitoring/LibreHW/Scripts/hidelaunch.exe` - Retired legacy launcher (2026-08-28), referenced by no live launch path (chain is `librehw.cmd` → `runw.exe` → PowerShell → `Start-LibreHardwareMonitor.ps1`).
- [axis: hygiene] `E:/Monitoring/LibreHW/specs new/` - Space-in-dirname deployment-root clutter: an agent-context note (`note.txt`) and the operator's 1.25 MB screenshot; duplicates nothing repo-tracked but belongs under DevHome/agent state, not a deployment root.

### Info / verifiability

- [axis: spec] Hosted CI gate run IDs `42724517456` / `42724521454` (claimed in the session handoff for `71bfcdfe`) appear nowhere in the repo or its full git history (grep + `git log -S`). Unverifiable locally; the spec's verification log cites only local gate runs.
- [axis: spec] Known-open items already tracked in the spec (not new findings, listed for completeness): live Remove-from-Graph + re-plot check; restart keeping a detached pane; Set Range dialog; DPI above 100%; dark-theme disabled-header contrast; the six Follow-ups (pan detach, Auto refit shift, keyboard-opened menu anchor, pane-label strip on short panes, lane-submenu ordering).
- [axis: hygiene] ~1.3 GB of ignored build output in the working tree (`experiments/` 1.2 GB, root `bin/` 68 MB) — correctly ignored; `Clear-LhmRepositoryBuildOutputs.ps1` is the designed remedy.
- [axis: hygiene] `git fsck` shows one dangling commit (`c7052499`, superseded dependency bump) plus ~20 dangling blobs/trees — expected post-recovery residue.

## Verified clean (evidence)

- Runtime identity: live exe SHA-256 = `9b924116...b8401c`, matching `release.json` and the promotion record; manifest fields self-consistent.
- Rollback slot: `0.9.6_aa95772` manifest and exe hash (`eea0e775...`) both verify; exactly one pair per contract.
- Live health: process running (PID 69672, promoted image path); `http://127.0.0.1:18085/data.json` returns 200.
- Launcher: `Scripts/Start-LibreHardwareMonitor.ps1` byte-identical to `ops/deploy/snd-desk/` source — zero drift; fail-closed checks intact; machine scoping per AGENTS portability rule.
- Candidate `desk-master-20261110-1bf2655f`: complete shape, hashes match the promoted runtime.
- Divergence ledger: `PlotPanel.cs`/`MainForm.cs` rows updated by `baca022a`; `PlotTimeAxes.cs`/`PlotRangePrompt.cs` correctly classified fork-only.
- Code-vs-spec spot checks all match: `laneTime` key prefix/format/clamps (`PlotPanel.cs:31-34,1476-1525,1648-1669`), 1.25 menu zoom, 8 px DPI hit tolerance, alpha-192 strip (`PlotTimeAxes.cs:104`), invariant `R` round-trip, `Set Range` float-cast rejection (`PlotPanel.cs:1593-1612`), `SharesTimeWindowKeys` guarding both `Min/MaxTimeSpan` writers.
- The between-paint layout mirror (`PlotPaneTimeAxis.LayOutOverPlotArea`) reproduces OxyPlot 2.2.0's horizontal `UpdateTransform` exactly; zoom-rectangle and pan manipulator mirrors match upstream clamps/10 px threshold; no net472-incompatible APIs; menu item lifetimes disposed correctly; `AGENTS.md` source-of-truth map has no missing files; no conflict markers; no tracked build output; no tracked file over ~5 MB.

## Verification

- `git rev-parse HEAD origin/master librehw-host/main` - pass (all `71bfcdfe`)
- `git status --porcelain` - clean
- `sha256sum Runtime/LibreHardwareMonitor.exe` (workspace) vs `release.json` - match (via audit agent, output quoted above)
- `curl http://127.0.0.1:18085/data.json` - HTTP 200
- Grep for `Unbind|RemoveBinding` in `PlotPanel.cs` - no hits (confirms tracker bindings remain default)
- Grep for CI run IDs across repo and history - no hits
- Test suites / builds not re-run in this review (read-only pass; spec log records Library 193, Application 371+1 skip, Contracts 124, both Release builds 0 warnings, ledger gate 15 passed)

## Coverage notes

- Deep-reviewed: `UI/PlotPanel.cs`, `UI/PlotTimeAxes.cs`, `UI/PlotRangePrompt.cs`, `UI/MainForm.cs`, `UI/PresentationSurfaceCoordinator.cs`, `UI/WinFormsPresentationAdapters.cs` at HEAD plus both commit diffs; the three docs files of `71bfcdfe`; the workspace runtime/rollback/candidates/scripts; repo hygiene.
- Sampled/excluded: the rest of the promoted range `aa95772b..1bf2655f` (upstream merge `2e3b059b` contents, session-end fix `aa95772b` itself, ops clock `f0dadd06`, timezone `795847fc`, compact density `141da74a`, SDK pin `3ecc4d31`, docs/CI commits) was not deep-reviewed here — the handoff already flags that the promotion includes all of it; prior reviews and gates cover most of it.
- Test files were reviewed for acceptance-criteria mapping, not line-by-line.

## Open questions

- Should the promoted range's non-pane commits (upstream merge above all) get their own post-promotion live sample, given the runtime now carries them?
- Pruning policy for `Candidates/` (keep N? keep promoted lineage only?) — the local-release spec is silent.

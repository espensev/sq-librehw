# Campaign — Separate candidate creation from peer deployment

**Plan ID:** plan-004
**Date:** 2026-07-31
**Status:** approved
**Plan file:** data/plans/plan-004.json
**Plan doc:** docs/campaign-plan-004-separate-candidate-creation-from.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/refactor-roadmap.md
**Source discovery docs:** docs/discovery-librehw-structural-audit.md

---

## 1. Goal

Split ops/release (host-neutral candidate creation), ops/local-release + scripts/local-release (SND-DESK-only deployment), and the repository cleanup tool so the path states the blast radius: ops/candidate (non-deploying), ops/deploy/snd-desk (peer deployment), ops/log-management (unchanged), and eng/Clear-LhmRepositoryBuildOutputs.ps1 (repository maintenance). Preserve every fail-closed guard, the content-based deny-list, history, and all WinForms/live boundaries.

## 2. Exit Criteria

- ops/release/ is relocated to ops/candidate/, ops/local-release/ plus the deploy scripts to ops/deploy/snd-desk/, Clear-LhmRepositoryBuildOutputs.ps1 to eng/, ops/log-management/ is unchanged, and scripts/local-release/ is dissolved - all via git mv so git log --follow resolves through the moves.
- Every internal PSScriptRoot and repository-root reference in the moved scripts resolves after the move: dot-sourcing of LhmLocalRelease.Common.ps1 and LhmRelease.Common.ps1 works, and the relocated Test-LhmLocalRelease.ps1 cleanup reference points at eng/Clear-LhmRepositoryBuildOutputs.ps1.
- dotnet build of the shipping solution, both WinForms x64 Release targets, and the .NET suite pass, and git diff --stat shows no change under inherited product roots.
- .codex/skills/project.toml modules, smart-test mappings, the release-candidate and snd-desk-local-release-fixture build gates, and conflict zones point at the new paths; plan preflight --json is ready with zero errors; and eng/ci/Invoke-LhmGates.ps1 is byte-identical.
- The permanent stale-reference gate (Test-NoStaleReferences.ps1) is extended to cover the ops/release, ops/local-release, and scripts/local-release moves, and passes; no tracked current configuration or current document references a dissolved path.
- The content-based deny-list still refuses every deploying command after the move, proven by Invoke-LhmGates.ps1 -DryRun/-List and the gate-runner regression.
- Every SND-DESK fail-closed guard still fails closed on SND-HOST after the move, proven by the relocated peer-safe fixture (run under pwsh if the Windows PowerShell 5.1 Get-FileHash defect persists).
- The Avalonia 75-test gate still passes after its ops\release\Test-LhmReleaseSystem.ps1 reference is rewired to ops\candidate.
- No product source, live runtime, scheduled task (including the installed ops/log-management SYSTEM task), release store, rollback packet, active log, or archive changed, and LHM_RELEASE_ROOT is untouched.
- The Plan-004 ledger row is added with criterion-specific evidence and is not marked accepted; after guarded cleanup, git clean -ndX lists only data/tasks.json and data/analysis-cache.json.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| ops/release/ (4 files) | git mv | move | medium - host-neutral candidate creation; one self-ref in Test-LhmReleaseSystem.ps1 |
| ops/local-release/ (7 files) | git mv | move | high - SND-DESK fail-closed deployment contract |
| scripts/local-release/{Publish,Test} | git mv + internal refs | move+modify | high - Test cleanup ref and Publish common ref must rewire |
| scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1 | git mv + repo-root depth | move+modify | medium - repository maintenance tool, not deployment |
| .codex/skills/project.toml | modules, mappings, 2 gates, conflict zones | modify | high - single owner |
| experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1 | 1 line | modify | medium - spike gate runs ops\release\Test-LhmReleaseSystem.ps1 |
| eng/ci/tests/Test-NoStaleReferences.ps1 | extend move-map + allow-list | modify | medium - permanent gate extended for ops moves |
| current docs (9 files) | path claims | modify | medium - do not rewrite immutable evidence |
| docs/campaign-history.md | ledger row | modify | high - required so the one-row-per-plan regression stays green |
| Roadmap/backlog/tracker | close + advance | modify | high campaign-truth conflict zone; single owner |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| o | move-candidate-ops | Move ops/release/ to ops/candidate/ (host-neutral candidate creation) with git mv, and update the synthetic ops\release mirror in Test-LhmReleaseSystem.ps1 to ops\candidate. |  | ops/release/ | 0 | medium |
| p | dissolve-local-release | Dissolve scripts/local-release/ and move ops/local-release/: ops/local-release/ and scripts/local-release Publish+Test to ops/deploy/snd-desk/, Clear-LhmRepositoryBuildOutputs.ps1 to eng/, fixing all internal PSScriptRoot/repository-root/cleanup references. |  | ops/local-release/, scripts/local-release/Publish-LibreHardwareMonitor.ps1, scripts/local-release/Test-LhmLocalRelease.ps1, scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1 | 0 | high |
| r | rewire-config-and-gates | Rewire project.toml modules/mappings/gates/conflict-zones to the new ops paths, rewire the spike gate's ops\release reference to ops\candidate, and extend the permanent stale-reference gate (Test-NoStaleReferences.ps1) move-map and allow-list for the ops moves. | o, p | .codex/skills/project.toml, experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1, eng/ci/tests/Test-NoStaleReferences.ps1 | 1 | high |
| s | verify-fail-closed | Verify (read-only) that SND-DESK fail-closed guards still fail closed on SND-HOST after the move via the relocated peer-safe fixture (under pwsh to bypass the Windows PowerShell 5.1 Get-FileHash defect), that the deny-list still refuses deploying commands, and that the extended stale-reference gate passes. Records evidence in its result payload. | r | ops/deploy/snd-desk/Test-LhmLocalRelease.ps1 | 2 | medium |
| t | ops-move-docs | Update current path claims across docs and eng/ci docs to the new ops/candidate, ops/deploy/snd-desk, and eng/ layout, and explicitly update or retire stale ops path claims in the structural discovery audit. | o, p, r | docs/README.md, docs/campaign-playbook.md, docs/feature-local-release-system.md, docs/feature-release-packaging.md, docs/feature-avalonia-fixture-sensor-explorer.md, docs/repository-build-output-cleanup.md, docs/discovery-librehw-structural-audit.md, eng/ci/README.md | 2 | medium |
| u | ops-move-close | Integrate results and close campaign truth surfaces: add the Plan-004 ledger row and criterion evidence in docs/campaign-history.md (without automatic acceptance), record Plan-004 tracker rows in live-tracker.md, close the Phase 2 operations-taxonomy roadmap item, and remove the completed Plan-004 section from the backlog. | o, p, r, s, t | docs/campaign-history.md, live-tracker.md, docs/refactor-roadmap.md, docs/campaign-backlog.md | 3 | high |

## 5. Dependency Graph

```text
Group 0: o, p
Group 1: r
Group 2: s, t
Group 3: u
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| ops/release/ | o |
| ops/local-release/ | p |
| scripts/local-release/Publish-LibreHardwareMonitor.ps1 | p |
| scripts/local-release/Test-LhmLocalRelease.ps1 | p |
| scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1 | p |
| .codex/skills/project.toml | r |
| experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1 | r |
| eng/ci/tests/Test-NoStaleReferences.ps1 | r |
| ops/deploy/snd-desk/Test-LhmLocalRelease.ps1 | s |
| docs/README.md | t |
| docs/campaign-playbook.md | t |
| docs/feature-local-release-system.md | t |
| docs/feature-release-packaging.md | t |
| docs/feature-avalonia-fixture-sensor-explorer.md | t |
| docs/repository-build-output-cleanup.md | t |
| docs/discovery-librehw-structural-audit.md | t |
| eng/ci/README.md | t |
| docs/campaign-history.md | u |
| live-tracker.md | u |
| docs/refactor-roadmap.md | u |
| docs/campaign-backlog.md | u |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| ['ops/candidate/', 'LibreHardwareMonitor.sln', 'Directory.Packages.props'] |  | Only candidate scripts move; the inherited solution and central package props stay byte-identical |
| ['.codex/skills/project.toml', 'eng/ci/Invoke-LhmGates.ps1'] |  | Configuration moves; the runner must remain byte-identical and its content-based deny-list survives the move |
| ['ops/deploy/snd-desk/', 'machine identity guards'] |  | SND-DESK fail-closed guards must still fail closed on SND-HOST after the move |
| ['ops/deploy/snd-desk/Test-LhmLocalRelease.ps1', 'eng/Clear-LhmRepositoryBuildOutputs.ps1'] |  | The deploy fixture and the cleanup tool split apart; one owner fixes the fixture's cleanup reference |
| ['docs/campaign-history.md', 'live-tracker.md', 'data/plans/'] |  | Separate owners; u is the campaign-truth closure owner; rendered plan docs are not hand-edited |

## 8. Integration Points

- o freezes the candidate path (ops/candidate) consumed by r and t.
- p dissolves scripts/local-release and owns every internal reference fix, including Test-LhmLocalRelease.ps1's cleanup reference to eng/.
- r makes project.toml, the spike gate, and the stale-reference gate resolve the new ops paths.
- s proves fail-closed behavior, deny-list survival, and stale-reference cleanliness after r.
- t makes current documentation agree with the moved tree.
- u records combined evidence and keeps the plan-to-ledger criterion text and count exact.

## 9. Schema Changes

- No database, settings, JSON payload, CSV, Prometheus, HTTP, or runtime schema changes are allowed.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| SND-DESK fail-closed guards break after the move | low | high | Machine-identity guards are content-based and depth-independent; proven by the relocated peer-safe fixture under pwsh. |
| LHM_RELEASE_ROOT or the external release store is touched | low | high | Source-only; nothing in this campaign writes to the external store or the env binding. |
| Installed ops/log-management runtime or its SYSTEM task is disturbed | low | high | Source-only; the installed copy and task are out of scope and untouched. |
| The content-based deny-list stops refusing deploying commands | low | high | Matching is on command content, not paths; re-proven by -DryRun and the gate-runner regression. |
| Test-LhmLocalRelease cleanup reference breaks on the deploy/eng split | medium | medium | p owns the fix; s verifies the relocated fixture. |
| The spike gate breaks because its ops\release reference is not rewired | medium | medium | r owns the one-line fix to ops\candidate. |
| Stale ops path claims remain in current docs | medium | medium | The extended stale-reference gate plus agent t. |
| The synthetic test mirror drifts from the real layout | low | low | o updates Test-LhmReleaseSystem.ps1 line 187 from ops\release to ops\candidate. |
| The Windows PowerShell 5.1 Get-FileHash defect blocks the fail-closed fixture | high | medium | Run the relocated fixture under pwsh where Get-FileHash works; this is the same open defect as Plan-003 criterion 4. |
| Existing Plan-003 work is disturbed | low | high | Plan-003 baseline is committed; never run inline merge. |

## 11. Verification Strategy

- Before: python scripts\task_manager.py plan preflight --json; powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List
- After: python scripts\task_manager.py plan validate plan-004; python scripts\task_manager.py plan preflight --json
- After: python -m unittest discover -s scripts -p 'test_*.py' -v; powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
- After: powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -DryRun; then -All (note: the snd-desk-local-release-fixture gate is blocked by the pre-existing Get-FileHash defect; run the relocated fixture under pwsh for the fail-closed proof)
- After: git diff --check; git diff --stat -- Aga.Controls LibreHardwareMonitorLib LibreHardwareMonitor.Windows.Forms LibreHardwareMonitor.Tests LibreHardwareMonitor.sln Directory.Packages.props global.json (must print nothing)
- Configured gate commands: dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64; dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64; dotnet build ...-f net472 -p:Platform=x64. The relocated fail-closed fixture is run under pwsh to bypass the PS 5.1 Get-FileHash defect.

## 12. Documentation Updates

- Update the source map and verification commands in docs/README.md to the new ops/candidate, ops/deploy/snd-desk, and eng/ layout.
- Update docs/feature-local-release-system.md and docs/feature-release-packaging.md path claims and commands.
- Update docs/campaign-playbook.md, docs/repository-build-output-cleanup.md, docs/feature-avalonia-fixture-sensor-explorer.md, and eng/ci/README.md path claims.
- Explicitly update or retire stale ops path claims in docs/discovery-librehw-structural-audit.md; Git history preserves point-in-time detail.
- Mark the Phase 2 operations-taxonomy item complete in docs/refactor-roadmap.md and remove the completed Plan-004 section from docs/campaign-backlog.md.
- Add the Plan-004 ledger row and criterion evidence via agent u only; do not mark it accepted automatically.


## R1. Roadmap Phase

Phase: Phase 2 - Fork-only taxonomy
Roadmap reference: docs/refactor-roadmap.md

## R2. Behavioral Invariants

- SND-DESK deployment paths fail closed on SND-HOST before and after the move.
- ops/log-management installed runtime and its SYSTEM scheduled task are untouched; this campaign is source-only.
- LHM_RELEASE_ROOT and the external release store are untouched; nothing in this campaign writes to them.
- WinForms remains the sole hardware, process, and scheduled-task owner.
- data.json shape/order/IDs, HTTP routes, Prometheus, CSV, settings, and hardware behavior do not change.
- Both net10.0-windows and net472 WinForms builds remain green.
- Inherited product roots are unchanged and eng/ci/Invoke-LhmGates.ps1 stays byte-identical; the content-based deny-list survives the move.

## R3. Rollback Strategy

This campaign has no runtime or schema migration. Roll back by reverting the campaign commits, which restores the original ops/release, ops/local-release, and scripts/local-release paths and references. Before any revert, preserve the Plan-003 baseline commit fa5b3fd and inspect the exact campaign commit range. Do not use git reset --hard, do not delete ignored ledger/cache files (data/tasks.json, data/analysis-cache.json), and do not touch external live/candidate/rollback/log roots or the installed log-management runtime.

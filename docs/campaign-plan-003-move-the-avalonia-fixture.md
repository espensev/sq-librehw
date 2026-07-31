# Campaign — Move the Avalonia Fixture Explorer into experiments/

**Plan ID:** plan-003
**Date:** 2026-07-31
**Status:** executed
**Plan file:** data/plans/plan-003.json
**Plan doc:** docs/campaign-plan-003-move-the-avalonia-fixture.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/refactor-roadmap.md
**Source discovery docs:** docs/discovery-librehw-structural-audit.md

---

## 1. Goal

Move the fixture-only, non-shipping Avalonia explorer out of the repository root and into experiments/avalonia-fixture-explorer/, preserving project/assembly names, behavior, history, package isolation, and every WinForms/live boundary. Add a durable stale-reference test so later taxonomy moves cannot leave broken current paths silently.

## 2. Exit Criteria

- The three projects, the .slnx, and the test runner live under experiments/avalonia-fixture-explorer/, moved with git mv so git log --follow resolves through the move.
- dotnet build of the moved .slnx succeeds in Release from a clean output state.
- The Avalonia gate passes 75/75, unchanged in count.
- eng/ci/Invoke-LhmGates.ps1 -All passes 8/8 with no edit to the runner, proving configuration-only rewiring.
- Both WinForms x64 Release targets and the .NET suite pass, and git diff --stat shows no change under inherited product roots.
- The permanent stale-reference test passes and the CI dispatcher reports three discovered test scripts.
- A clean candidate built from the moved source contains zero Avalonia or spike entries in both WinForms package inventories. It is not promoted.
- No product source, live runtime, scheduled task, release store, rollback packet, active log, or archive changed.
- plan preflight --json is ready with zero errors and, after guarded cleanup, git clean -ndX lists only data/tasks.json and data/analysis-cache.json.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| Three LibreHardwareMonitor.Avalonia.Spike* directories | git mv | move | medium - fork-only but wide references |
| LibreHardwareMonitor.Avalonia.Spike.slnx | 3 project paths | modify | high - central project graph; one owner |
| App/tests .csproj files | verify only | verify | medium - clean build proves resolution; relative refs preserved by the cluster move |
| scripts/Test-AvaloniaSpike.ps1 | repository-root resolution | move+modify | medium - preserve CLI/75-test contract |
| .codex/skills/project.toml | modules, mappings, gates, conflict zone, cross-cutting | modify | high - single owner |
| scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1 | 6 paths | modify | medium - review -WhatIf before cleanup |
| eng/ci/tests/Test-NoStaleReferences.ps1 | new | create | medium - allow-list historical evidence as data |
| Current docs (README, feature spec) | path claims | modify | medium - do not rewrite immutable evidence |
| docs/campaign-history.md | ledger row | modify | high - required so the one-row-per-plan regression stays green |
| Roadmap/backlog/tracker | close + advance | modify | high campaign-truth conflict zone; single owner |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| j | move-spike-projects | Move the three Avalonia spike projects and the .slnx into experiments/avalonia-fixture-explorer/ using git mv, preserving history and the cluster internal relative references. |  | LibreHardwareMonitor.Avalonia.Spike/, LibreHardwareMonitor.Avalonia.Spike.Core/, LibreHardwareMonitor.Avalonia.Spike.Tests/, LibreHardwareMonitor.Avalonia.Spike.slnx | 0 | medium |
| k | rewire-spike-config | Rewire .codex/skills/project.toml (module paths, smart-test mappings, gate commands, conflict zones, cross-cutting paths), move Test-AvaloniaSpike.ps1 into the experiment root and fix its repository-root resolution, and update the six Avalonia bin/obj paths in Clear-LhmRepositoryBuildOutputs.ps1. | j | .codex/skills/project.toml, scripts/Test-AvaloniaSpike.ps1, scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1 | 1 | high |
| l | stale-reference-gate | Add the permanent stale-current-reference regression test eng/ci/tests/Test-NoStaleReferences.ps1 with a centralized allow-list for immutable historical evidence (completed agent specs and rendered campaign documents). | j, k | eng/ci/tests/Test-NoStaleReferences.ps1 | 2 | medium |
| m | spike-move-docs | Update current path claims in docs/README.md and the Avalonia feature spec, and explicitly update or retire the structural discovery document so no current document references a stale path. | j, k | docs/README.md, docs/feature-avalonia-fixture-sensor-explorer.md, docs/discovery-librehw-structural-audit.md | 2 | medium |
| n | spike-move-close | Integrate results and close the campaign truth surfaces: add the Plan-003 ledger row and criterion evidence in docs/campaign-history.md (without automatic acceptance), record Plan-003 tracker rows in live-tracker.md, close the roadmap item, and remove the completed Plan-003 section from the backlog. | j, k, l, m | docs/campaign-history.md, live-tracker.md, docs/refactor-roadmap.md, docs/campaign-backlog.md | 3 | high |

## 5. Dependency Graph

```text
Group 0: j
Group 1: k
Group 2: l, m
Group 3: n
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| LibreHardwareMonitor.Avalonia.Spike/ | j |
| LibreHardwareMonitor.Avalonia.Spike.Core/ | j |
| LibreHardwareMonitor.Avalonia.Spike.Tests/ | j |
| LibreHardwareMonitor.Avalonia.Spike.slnx | j |
| .codex/skills/project.toml | k |
| scripts/Test-AvaloniaSpike.ps1 | k |
| scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1 | k |
| eng/ci/tests/Test-NoStaleReferences.ps1 | l |
| docs/README.md | m |
| docs/feature-avalonia-fixture-sensor-explorer.md | m |
| docs/discovery-librehw-structural-audit.md | m |
| docs/campaign-history.md | n |
| live-tracker.md | n |
| docs/refactor-roadmap.md | n |
| docs/campaign-backlog.md | n |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| ['Directory.Packages.props', 'LibreHardwareMonitor.sln', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.slnx'] |  | Only the moved spike .slnx changes; package props and the inherited solution stay byte-identical |
| ['.codex/skills/project.toml', 'eng/ci/Invoke-LhmGates.ps1'] |  | Configuration moves; the runner must remain byte-identical and continue reading config dynamically |
| ['docs/README.md', 'live-tracker.md', 'docs/campaign-history.md', 'data/plans/'] |  | Separate owners; n is the campaign-truth closure owner; rendered plan docs are not hand-edited |
| ['experiments/avalonia-fixture-explorer/', 'scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1'] |  | k updates all six explicit Avalonia output paths in the same dependent change |
| ['historical agent specs and rendered campaign documents', 'eng/ci/tests/Test-NoStaleReferences.ps1'] |  | The test uses a centralized allow-list for immutable historical evidence, not scattered conditionals |

## 8. Integration Points

- j establishes the final paths consumed by k, l, and m.
- k makes builds, tests, and configuration resolve those paths; it owns project.toml and the two script surfaces.
- l proves no tracked current configuration or current document points at a nonexistent path while explicitly allowing immutable historical evidence.
- m makes current documentation agree with the moved tree.
- n records the combined result only after all earlier owners return evidence and keeps the tracked plan-to-ledger criterion text and count exact.

## 9. Schema Changes

- No database, settings, JSON payload, CSV, Prometheus, HTTP, or runtime schema changes are allowed.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Relative project reference resolves accidentally from old outputs | medium | medium | Delete generated outputs with the guarded cleanup tool, then build the moved solution clean. |
| Config and path move drift | medium | high | Permanent stale-reference test plus the gate listing. |
| Runner is edited to accommodate the move | low | high | Treat that as a runner defect; this campaign must prove configuration-only rewiring. |
| Cleanup stops seeing moved outputs | medium | medium | Update all six explicit paths and verify -WhatIf before removal. |
| Delete/recreate loses history | low | high | Use git mv; check git log --follow. |
| Current docs become half-true | medium | medium | Update or retire the discovery document explicitly; never leave partial path claims. |
| Historical evidence is rewritten merely to remove stale strings | low | high | Preserve completed specs and rendered campaign docs; allow-list them in the test. |
| A source candidate is mistaken for deployment | low | high | Validate package isolation only; never promote or change live runtime. |
| Existing Plan-002 work is lost | low | high | Commit the protected baseline first; never run inline merge. |

## 11. Verification Strategy

- Before: python scripts\task_manager.py plan preflight --json; powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
- After: python scripts\task_manager.py plan validate plan-003; python scripts\task_manager.py plan preflight --json
- After: python -m unittest discover -s scripts -p 'test_*.py' -v
- After: powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1; ...\Invoke-LhmGates.ps1 -List; ...\Invoke-LhmGates.ps1 -All
- After: git diff --check; git diff --stat -- Aga.Controls LibreHardwareMonitorLib LibreHardwareMonitor.Windows.Forms LibreHardwareMonitor.Tests LibreHardwareMonitor.sln Directory.Packages.props global.json (must print nothing)
- Configured gate commands run via Invoke-LhmGates.ps1 -All: dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64; dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64; dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64

## 12. Documentation Updates

- Update the current source map and paths in docs/README.md.
- Update the Avalonia feature spec path claims and verification commands.
- Explicitly update or retire docs/discovery-librehw-structural-audit.md; Git history preserves its point-in-time detail.
- Mark the Phase 2 Avalonia move complete in docs/refactor-roadmap.md.
- Remove the completed Plan-003 section from docs/campaign-backlog.md only after it lands; retain the durable record in Git/campaign history.
- Add the Plan-003 tracker rows through agent n only.
- Create the matching Plan-003 ledger row when the tracked plan is created, then record criterion-specific evidence through agent n; do not mark it accepted automatically.


## R1. Roadmap Phase

Phase: Phase 2 - Fork-only taxonomy
Roadmap reference: docs/refactor-roadmap.md

## R2. Behavioral Invariants

- WinForms remains the sole hardware, process, and scheduled-task owner.
- The Avalonia explorer stays fixture-only, non-elevated, non-shipping, and outside LibreHardwareMonitor.sln and release packages.
- Project and assembly names do not change.
- The Avalonia test count remains 75.
- data.json shape/order/IDs, HTTP routes, Prometheus, CSV, settings, hardware behavior, and AssemblyVersion do not change.
- Both WinForms framework builds remain green.
- Inherited product roots remain at their current paths and unchanged.

## R3. Rollback Strategy

This campaign has no runtime or schema migration. Roll back by reverting the campaign commits, which restores the original source paths and references. Before any revert, preserve the Plan-002 baseline commit 21c6d13 and inspect the exact campaign commit range. Do not use git reset --hard, do not delete ignored ledger/cache files (data/tasks.json, data/analysis-cache.json), and do not touch external live/candidate/rollback roots.

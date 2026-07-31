# Campaign — Documentation and engineering taxonomy

**Plan ID:** plan-005
**Date:** 2026-07-31
**Status:** executed
**Plan file:** data/plans/plan-005.json
**Plan doc:** docs/campaign-plan-005-documentation-and-engineering-taxonomy.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/architecture/refactor-roadmap.md

---

## 1. Goal

Group the hand-authored content docs under docs/features/ and docs/architecture/, retire the completed point-in-time discovery reviews, and update every reference, while leaving tooling-coupled docs (README, campaign-backlog/history/playbook, rendered campaign-plan-*.md) at docs/ root and scripts/task_manager.py unmodified.

## 2. Exit Criteria

- The 11 feature-*.md live under docs/features/, refactor-roadmap.md and repository-build-output-cleanup.md under docs/architecture/, and the 2 discovery-*.md are retired with a fold note - all via git mv / git rm so history is preserved.
- Tooling-coupled docs stay at docs/ root: docs/README.md, campaign-backlog.md, campaign-history.md, campaign-playbook.md, and the 4 rendered campaign-plan-*.md are unmoved.
- scripts/task_manager.py is unmodified (git diff --stat empty) and test_campaign_history.py LEDGER_PATH still resolves; plan_doc_path still renders to docs/campaign-plan-*.md.
- Every current reference to a moved doc path is updated: AGENTS.md source map, docs/README, campaign-playbook, campaign-backlog, and inter-doc links.
- .codex/skills/project.toml module globs, smart-test mappings, and conflict-zone paths agree with the new layout; plan preflight --json is ready with zero errors.
- The permanent stale-reference gate is extended to cover the doc moves (move-map entries + dissolved discovery-path check) and passes; no tracked current configuration or document references a retired path.
- Both WinForms x64 Release targets and the .NET suite pass, and git diff --stat shows no change under inherited product roots.
- eng/ci/Invoke-LhmGates.ps1 is byte-identical and the full gate sweep passes (modulo the pre-existing snd-desk-local-release-fixture Get-FileHash defect).
- No product source, live runtime, scheduled task, release store, rollback packet, active log, or archive changed.
- The Plan-005 ledger row is added with criterion-specific evidence and is not marked accepted; after guarded cleanup, git clean -ndX lists only data/tasks.json and data/analysis-cache.json.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| 11 docs/feature-*.md | git mv | move | medium - referenced by AGENTS.md source map + docs/README + inter-doc links |
| docs/refactor-roadmap.md | git mv | move | high continuation-checkpoint; widely referenced |
| docs/repository-build-output-cleanup.md | git mv | move | low record doc |
| 2 docs/discovery-*.md | git rm | retire | medium - point-in-time reviews; must fold note, not silently delete |
| AGENTS.md | source map | modify | medium - enumerates most docs by hand |
| docs/README.md, campaign-playbook.md, campaign-backlog.md | cross-links | modify | medium - reference moved paths |
| .codex/skills/project.toml | globs, mappings | modify | medium - docs module glob + smart-test |
| eng/ci/tests/Test-NoStaleReferences.ps1 | move-map + allow-list | modify | medium - extend for doc moves |
| docs/campaign-history.md | ledger row | modify | high - one-row-per-plan regression |
| scripts/task_manager.py | 0 - NOT MODIFIED | verify | high if touched - canonical-surface provenance-pinned; this campaign leaves it unchanged |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| v | reorganize-docs | git mv the 11 feature-*.md to docs/features/, refactor-roadmap.md and repository-build-output-cleanup.md to docs/architecture/, and retire the 2 discovery-*.md point-in-time reviews after recording a fold note. Tooling-coupled docs (README, campaign-backlog/history/playbook, rendered campaign-plan-*.md) stay at docs/ root. |  | docs/feature-avalonia-fixture-sensor-explorer.md, docs/feature-host-log-management.md, docs/feature-host-operator-utilities.md, docs/feature-independent-text-scaling.md, docs/feature-local-release-system.md, docs/feature-memory-ui-reliability.md, docs/feature-native-ui-modernization.md, docs/feature-release-packaging.md, docs/feature-sensor-workspace.md, docs/feature-standard-context-layouts.md, docs/feature-thermal-trends.md, docs/feature-upstream-sync-2026-07-25.md, docs/feature-web-dashboard-studio-view.md, docs/refactor-roadmap.md, docs/repository-build-output-cleanup.md, docs/discovery-librehw-structural-audit.md, docs/discovery-pre-avalonia-readiness.md | 0 | medium |
| w | rewire-doc-references | Update every reference to the moved docs: AGENTS.md source map, docs/README, campaign-playbook, campaign-backlog, and inter-doc links; update project.toml smart-test/conflict-zone/module globs; extend the stale-reference gate move-map and allow-list to cover the doc moves. | v | .codex/skills/project.toml, AGENTS.md, eng/ci/tests/Test-NoStaleReferences.ps1 | 1 | high |
| x | docs-move-close | Add the Plan-005 ledger row and criterion evidence in docs/campaign-history.md (without automatic acceptance), record Plan-005 tracker rows in live-tracker.md, close the Phase 2 documentation-grouping roadmap item (at its new docs/architecture path), and remove the completed Plan-005 section from the backlog. | v, w | docs/campaign-history.md, live-tracker.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md | 2 | high |

## 5. Dependency Graph

```text
Group 0: v
Group 1: w
Group 2: x
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| docs/feature-avalonia-fixture-sensor-explorer.md | v |
| docs/feature-host-log-management.md | v |
| docs/feature-host-operator-utilities.md | v |
| docs/feature-independent-text-scaling.md | v |
| docs/feature-local-release-system.md | v |
| docs/feature-memory-ui-reliability.md | v |
| docs/feature-native-ui-modernization.md | v |
| docs/feature-release-packaging.md | v |
| docs/feature-sensor-workspace.md | v |
| docs/feature-standard-context-layouts.md | v |
| docs/feature-thermal-trends.md | v |
| docs/feature-upstream-sync-2026-07-25.md | v |
| docs/feature-web-dashboard-studio-view.md | v |
| docs/refactor-roadmap.md | v |
| docs/repository-build-output-cleanup.md | v |
| docs/discovery-librehw-structural-audit.md | v |
| docs/discovery-pre-avalonia-readiness.md | v |
| .codex/skills/project.toml | w |
| AGENTS.md | w |
| eng/ci/tests/Test-NoStaleReferences.ps1 | w |
| docs/campaign-history.md | x |
| live-tracker.md | x |
| docs/architecture/refactor-roadmap.md | x |
| docs/campaign-backlog.md | x |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| ['docs/campaign-history.md', 'live-tracker.md', 'data/plans/'] |  | Campaign-truth closure; x owns them; rendered plan docs are not hand-edited |
| ['docs/feature-*.md', 'AGENTS.md', 'docs/README.md'] |  | Moved docs and their two hand-enumerated source maps must stay in sync |
| ['scripts/task_manager.py', 'docs/campaign-plan-*.md'] |  | Tooling-coupled: plan_doc_path renders to docs/ root and must not be moved; task_manager.py is provenance-pinned and unmodified |
| ['docs/discovery-*.md', 'docs/architecture/refactor-roadmap.md'] |  | Retired point-in-time reviews vs the durable roadmap contract that absorbs their findings |

## 8. Integration Points

- v performs all doc moves and retirements, freezing the new paths.
- w makes every current reference and the config/gate agree with the new tree.
- x records combined evidence and closes the roadmap item at its new docs/architecture location.

## 9. Schema Changes

- No database, settings, JSON payload, CSV, Prometheus, HTTP, runtime, or plan-artifact schema changes. docs-sync.tier2 already globs docs/**/*.md so nesting needs no schema change.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| A moved doc path is referenced but not updated | medium | medium | The extended stale-reference gate scans every tracked text file; AGENTS.md and docs/README source maps are updated in the same change. |
| scripts/task_manager.py plan_doc_path is accidentally modified | low | high | Criterion 3 asserts git diff --stat over task_manager.py is empty; the campaign never moves rendered campaign-plan-*.md. |
| test_campaign_history.py LEDGER_PATH breaks | low | high | campaign-history.md stays at docs/ root; the regression is run in verification. |
| Inter-doc links (feature specs linking each other) go stale | medium | low | The stale-reference gate plus a git grep sweep before commit. |
| Retiring a discovery doc loses an unresolved finding | low | medium | Fold the live findings into the roadmap before git rm; Git history preserves the full text. |
| docs-sync tiers stop covering nested docs | low | low | tier2 uses docs/**/*.md which already matches nested paths; verify after the move. |
| Existing Plan-003/004 work is disturbed | low | high | Plan-004 baseline is committed; this worktree branches from it; never run inline merge. |

## 11. Verification Strategy

- Before: python scripts\task_manager.py plan preflight --json
- After: python scripts\task_manager.py plan validate plan-005; python scripts\task_manager.py plan preflight --json
- After: python -m unittest discover -s scripts -p 'test_*.py' -v; powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
- After: powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-NoStaleReferences.ps1; git diff --stat -- scripts/task_manager.py (must be empty)
- After: git diff --stat -- Aga.Controls LibreHardwareMonitorLib LibreHardwareMonitor.Windows.Forms LibreHardwareMonitor.Tests LibreHardwareMonitor.sln Directory.Packages.props global.json (must print nothing)
- Configured gate commands via Invoke-LhmGates.ps1 -All: dotnet test ...Tests.csproj -p:Platform=x64; dotnet build ...Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64; dotnet build ...-f net472 -p:Platform=x64.
- Configured gate commands run via Invoke-LhmGates.ps1 -All: dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64; dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64; dotnet build ...-f net472 -p:Platform=x64.

## 12. Documentation Updates

- Update the AGENTS.md source-of-truth map to the new docs/features/ and docs/architecture/ paths.
- Update docs/README source map and any cross-links.
- Update campaign-playbook and campaign-backlog references to the moved refactor-roadmap (now docs/architecture/refactor-roadmap.md).
- Mark the Phase 2 documentation-grouping item complete in docs/architecture/refactor-roadmap.md and remove the completed Plan-005 section from the backlog.
- Add the Plan-005 ledger row via agent x only; do not mark it accepted automatically.


## R1. Roadmap Phase

Phase: Phase 2 - Fork-only taxonomy
Roadmap reference: docs/architecture/refactor-roadmap.md

## R2. Behavioral Invariants

- Tooling-coupled docs stay at their current paths: docs/README.md, campaign-backlog/history/playbook, and rendered campaign-plan-*.md are not moved (task_manager.py plan_doc_path and test_campaign_history.py LEDGER_PATH depend on them).
- Only hand-authored content docs move: 11 feature specs to docs/features/, refactor-roadmap and repository-build-output-cleanup to docs/architecture/, discovery point-in-time reviews retired.
- scripts/task_manager.py is canonical-surface and provenance-pinned; it is not modified.
- WinForms remains the sole hardware, process, and scheduled-task owner; no product source, live runtime, scheduled task, release store, rollback packet, active log, or archive changes.
- Both net10.0-windows and net472 WinForms builds remain green and data.json/HTTP/Prometheus/CSV/settings behavior do not change.
- eng/ci/Invoke-LhmGates.ps1 stays byte-identical.

## R3. Rollback Strategy

This campaign has no runtime or schema migration. Roll back by reverting the campaign commits, which restores the original flat docs/ layout and references. Before any revert, preserve the Plan-004 baseline commit 100d8ae. Do not use git reset --hard, do not delete ignored ledger/cache files (data/tasks.json, data/analysis-cache.json), and do not touch external live/candidate/rollback/log roots.

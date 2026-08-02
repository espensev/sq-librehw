# Campaign — Settings projection and persistence coordination

**Plan ID:** plan-011
**Date:** 2026-08-02
**Status:** executed
**Plan file:** data/plans/plan-011.json
**Plan doc:** docs/campaign-plan-011-settings-projection-and-persistence.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/architecture/refactor-roadmap.md

---

## 1. Goal

Extract MainForm's settings projection and save sequencing behind one internal coordinator so UI-state projection, unchanged autosave dirty-skip behavior, safe-path validation, ordered persistence, retry behavior, and final shutdown save have one deterministic owner while PersistentSettings remains the unchanged atomic backup-aware store and all persisted/external/live contracts remain compatible.

## 2. Exit Criteria

- Outside campaign/configuration/documentation truth, the diff is exactly two new files (UI/SettingsPersistenceCoordinator.cs and SettingsPersistenceCoordinatorTests.cs) plus MainForm.cs; PersistentSettings.cs, RuntimePaths.cs, StartupManager.cs, existing settings tests, projects, packages, gates, golden, HTTP, lifecycle, hardware, web, and operations files remain unchanged.
- AP adds one internal sealed net472-compatible delegate-driven SettingsPersistenceCoordinator with no typed dependency on Form, controls, MainForm, HttpServer, hardware, snapshot DTOs, or concrete runtime UI types, plus exactly seven deterministic Fact tests.
- The coordinator owns only the project-current-state then dirty-skip then safe-path-validation then PersistentSettings.Save sequence; unchanged autosave skips disk I/O, projected changes persist, final save writes even when clean, autosave IOException/UnauthorizedAccessException is non-modal and retryable, final-save failures remain caller-visible, and concurrent saves retain latest-snapshot ordering.
- MainForm remains the composition root and exact owner of UI control reads, settings-key/value selection, autosave timer, shutdown timing, dialogs, and debug policy; it projects plot, tree-column, text-scale, listener, and authentication state on the UI thread through the coordinator without changing any key, value, default, timing, or message.
- PersistentSettings remains byte-identical and its ordered, atomic, backup-aware, transient-load-blocking, duplicate-key normalization, and stale-history compaction facts all pass; RuntimePaths managed-path safeguards and StartupManager behavior remain unchanged.
- The seven new coordinator facts pass 7/7; the focused SettingsProjectionTests, SettingsPersistenceCoordinatorTests, SettingsPersistenceTests, RuntimePathsTests, and StartupManagerTests pass; Application is exactly 167 discovered, 166 passed, and the one established live-config opt-in skip; the deterministic aggregate is exactly 308 discovered, 307 passed, and that same skip.
- Contracts remain 73/73; the protected data.json golden blob and SHA-256 remain exact; Plan-008 snapshot, Plan-009 HTTP, Plan-010 lifecycle/polling, CSV, Prometheus, hardware identity, settings schema, and runtime-path contracts do not change.
- Both WinForms x64 Release targets build with zero warnings/errors; Avalonia remains 75/75 with only established AVLN3001; web remains 315/315 plus 18/18; and all eight included non-deploying CI gates pass without changing a gate.
- Exclusive worktree ownership, dependency-first integration, commit/file/patch checks, plan validation/preflight/analyzer, docs synchronization, Git checks, and guarded output cleanup pass; only proven Plan-011 worktrees/branches and reproducible outputs are removed while ignored ledger/cache and preserved Plan-001 branches remain.
- Read-only VERIFIED SND-HOST proof retains the exact separate live process/task, HTTP 200 for root/data.json/metrics, and growing current-day CSV without any source-to-live, candidate, task, configuration, log, or operations mutation.
- Plan state remains executed and the ledger records implemented, never automatic acceptance; Phase 2 remains partial, Phase 4 item 4 completes while Phase 4 remains in progress, Plan-012/agent as becomes next, A1 remains person-only, and no candidate, deployment, promotion, or live cutover is claimed.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| LibreHardwareMonitor.Windows.Forms/UI/SettingsPersistenceCoordinator.cs | new | create | high |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsPersistenceCoordinatorTests.cs | new | create | medium |
| LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs | 2526 | modify | high |
| .codex/skills/project.toml | 131 | modify | medium |
| data/plans/plan-011.json | new | create | medium |
| docs/campaign-plan-011-settings-projection-and-persistence.md | new | create | low |
| docs/README.md | 443 | modify | low |
| docs/architecture/refactor-roadmap.md | 321 | modify | medium |
| docs/campaign-backlog.md | 140 | modify | low |
| docs/campaign-history.md | 259 | modify | medium |
| docs/features/feature-memory-ui-reliability.md | 449 | modify | medium |
| live-tracker.md | 128 | modify | low |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| aq | wire-mainform-settings | Wire the tested settings persistence coordinator into MainForm, moving only SaveConfiguration sequencing behind the seam while preserving UI-thread projection, exact keys/values, autosave and final-save timing, messages, lifecycle shutdown order, and PersistentSettings/RuntimePaths behavior. | ap | LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs | 1 | high |
| ap | settings-persistence-coordinator | Add and deterministically test one internal delegate-driven settings persistence coordinator that projects current UI state, applies the unchanged autosave dirty-skip policy, validates the primary/backup/staging paths, invokes PersistentSettings Save, suppresses only autosave I/O/access failures for retry, and preserves final-save propagation without changing PersistentSettings. |  | LibreHardwareMonitor.Windows.Forms/UI/SettingsPersistenceCoordinator.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsPersistenceCoordinatorTests.cs | 0 | high |
| ar | verify-document-close | Independently verify the integrated settings seam, persistence/external/lifecycle contracts, both framework targets, non-deploying gates, repository ownership, and read-only live separation; then update only campaign/configuration/documentation truth and record implemented rather than accepted. | ap, aq | .codex/skills/project.toml, data/plans/plan-011.json, docs/campaign-plan-011-settings-projection-and-persistence.md, docs/README.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, docs/features/feature-memory-ui-reliability.md, live-tracker.md | 2 | high |

## 5. Dependency Graph

```text
Group 0: ap
Group 1: aq
Group 2: ar
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs | aq |
| LibreHardwareMonitor.Windows.Forms/UI/SettingsPersistenceCoordinator.cs | ap |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsPersistenceCoordinatorTests.cs | ap |
| .codex/skills/project.toml | ar |
| data/plans/plan-011.json | ar |
| docs/campaign-plan-011-settings-projection-and-persistence.md | ar |
| docs/README.md | ar |
| docs/architecture/refactor-roadmap.md | ar |
| docs/campaign-backlog.md | ar |
| docs/campaign-history.md | ar |
| docs/features/feature-memory-ui-reliability.md | ar |
| live-tracker.md | ar |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| settings, runtime paths, and startup ownership | Yes: the new coordinator composes PersistentSettings and RuntimePaths guards, but those existing files remain untouched | AP exclusively owns the new coordinator and tests; AQ starts after AP and exclusively owns MainForm; AR later adds the new file to the existing project.toml zone. |
| application lifecycle and hardware ownership | MainForm wiring only | AQ is the sole MainForm owner and preserves Plan-010 shutdown ordering; ApplicationLifecycleCoordinator and Computer remain byte-identical. |
| external data.json snapshot, projection, listener, and HTTP mutation contract | Verification only | All listed files and golden bytes remain untouched and the full Contracts suite is required. |
| campaign truth | Yes | AR is the sole writer of plan/configuration/documentation truth and live-tracker after source integration. |
| gate definition drift | project.toml inventory only | AR adds one conflict-zone member only; Invoke-LhmGates.ps1 and all commands remain unchanged. |
| project graph and Avalonia startup surfaces | No | Projects, packages, solutions, manifests, and Avalonia source stay byte-identical. |
| live operations boundary | Read-only separation proof only | No candidate, deployment, task, configuration, log, CSV, rollback, or operational-tree write is authorized. |

## 8. Integration Points

- AP defines and tests the delegate-driven persistence coordinator first and commits only its new source and seven-fact test file.
- AQ starts only after AP is integrated; MainForm supplies the UI-thread projection delegate and retains exact settings keys, values, timers, shutdown timing, dialogs, and debug policy.
- The coordinator composes PersistentSettings and RuntimePaths behavior without changing either file; existing SettingsProjectionTests and SettingsPersistenceTests remain the compatibility baseline.
- AR starts only after integrated source is clean, independently verifies exact diffs, counts, hashes, builds, gates, and read-only live separation, then writes campaign/configuration/documentation truth once.
- AP and AQ return tracker-row evidence to AR; live-tracker.md has one writer for the campaign.
- No candidate, deployment, promotion, acceptance, scheduled-task, live configuration, log, CSV, rollback, or operational mutation is an integration step.

## 9. Schema Changes

- {'migration': 'None. No data, configuration, candidate, deployment, or live-runtime migration.', 'schema': 'No persisted or external schema change', 'compatibility': 'No settings key, value format, XML layout, backup name, runtime path, HTTP route, data.json payload, CSV, Prometheus, hardware identifier, project graph, package graph, or public API change.'}

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Projection moves behind a coordinator and changes key/value order or UI-thread access. | medium | high | AQ keeps the projection delegate in MainForm, preserves the exact statement order and keys, and invokes it synchronously from the UI-owned autosave/final-save paths. |
| Autosave dirty-skip or exception handling changes, causing disk churn or modal errors. | medium | high | Seven coordinator facts pin project-before-skip, clean skip, changed save, autosave suppression/retry, final-save propagation, and latest-snapshot ordering. |
| Wrapping PersistentSettings weakens atomic rotation, backup recovery, transient-read blocking, or stale-history compaction. | low | high | PersistentSettings and existing settings tests remain byte-identical; focused and full Application suites must pass. |
| MainForm shutdown saves too early/late relative to lifecycle drain or disposes settings dependencies prematurely. | medium | high | AQ is the sole MainForm owner and preserves the Plan-010 server stop, lifecycle drain, final save, and disposal order; lifecycle families are rerun. |
| The new coordinator uses APIs unavailable to net472. | low | high | Use established synchronous delegate/exception patterns, add no dependency or project change, and build both framework targets. |
| Closure overstates acceptance or deployment. | low | medium | AR records implemented rather than accepted, keeps A1 person-only, performs read-only live separation proof, and creates no candidate or deployment. |
| Analyzer unassigned inventory is mistaken for authorized scope. | low | high | Reject every diff outside the explicit ownership map; the analysis warning is inventory only. |

## 11. Verification Strategy

- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --filter FullyQualifiedName~SettingsPersistenceCoordinatorTests
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --filter FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~SettingsPersistenceCoordinatorTests|FullyQualifiedName~SettingsPersistenceTests|FullyQualifiedName~RuntimePathsTests|FullyQualifiedName~StartupManagerTests
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1; node webtests\selftest.node.js; node --test webtests\console.tests.js webtests\workspace.tests.js
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
- python scripts\task_manager.py plan validate plan-011 --json; python scripts\task_manager.py plan preflight --json; python scripts\task_manager.py analyze --json; git diff --check

## 12. Documentation Updates

- Update .codex/skills/project.toml only to add SettingsPersistenceCoordinator.cs to the existing settings/runtime/startup conflict zone; the WinForms wildcard already maps it, so add no redundant mapping or gate.
- Update docs/README.md, docs/architecture/refactor-roadmap.md, and docs/campaign-backlog.md to mark Phase-4 item 4 implemented, keep Phase 2 partial and A1 open, and advance to Plan-012/agent as without a deploy claim.
- Update docs/features/feature-memory-ui-reliability.md with the extracted settings projection/persistence ownership boundary while preserving the underlying PersistentSettings contract and open unrelated follow-ups.
- Update data/plans/plan-011.json, generated campaign markdown, docs/campaign-history.md, and live-tracker.md with criterion-specific implemented evidence; acceptance remains person-only.


## R1. Roadmap Phase

Phase: Phase 4 - Application and adapter seams
Roadmap reference: docs/architecture/refactor-roadmap.md

## R2. Behavioral Invariants

- MainForm remains the WinForms composition root and PersistentSettings remains the sole ordered atomic backup-aware settings store.
- Autosave still projects current plot, tree-column, text-scale, listener, and authentication state before applying the unchanged dirty-write skip; final shutdown save still waits behind any in-flight autosave and persists the latest projection.
- Stale sensor-history and duplicate-key compaction, transient-load write blocking, backup recovery, settings keys, formats, and runtime paths remain unchanged.
- Both WinForms framework targets, deterministic suites, data.json golden bytes, HTTP contracts, hardware identities, and lifecycle/shutdown ordering remain compatible.
- No candidate, deployment, promotion, scheduled-task, live configuration, log, CSV, rollback, or operational-tree mutation occurs.

## R3. Rollback Strategy

Revert the Plan-011 source, test, configuration, and documentation commits; no schema, candidate, deployment, or live-runtime rollback is required.

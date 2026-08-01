# Campaign — Application lifecycle and polling coordinator

**Plan ID:** plan-010
**Date:** 2026-08-01
**Status:** executed
**Plan file:** data/plans/plan-010.json
**Plan doc:** docs/campaign-plan-010-application-lifecycle-and-polling.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/architecture/refactor-roadmap.md

---

## 1. Goal

Extract the application hardware lifecycle and polling coordination that is currently fragmented across MainForm fields into one internal delegate-driven coordinator, so initialization, ordered option/reset work, single-flight polling admission, shutdown quiescence, and hardware close have one testable owner while MainForm remains the composition root and all external behavior stays compatible.

## 2. Exit Criteria

- Outside campaign/configuration/documentation truth, the diff is exactly two new files (UI/ApplicationLifecycleCoordinator.cs and ApplicationLifecycleCoordinatorTests.cs) plus MainForm.cs and a comment-only correction in UiShutdownCoordinator.cs; Computer.cs, existing coordinator tests, projects, packages, gates, golden, HTTP, settings, snapshot, web, and operations files remain unchanged.
- AM adds one internal sealed net472-compatible delegate-driven ApplicationLifecycleCoordinator with no typed dependency on Form, controls, Node, HttpServer, PersistentSettings, snapshot DTOs, or concrete hardware implementation types, plus exactly eight deterministic [Fact] tests.
- The coordinator exclusively owns the lifecycle cancellation source, lifecycle gate, initialization task/barrier/state, the existing HardwareOperationCoordinator instance, polling admission/completion/drain state, and idempotent stop/close sequencing; MainForm no longer owns parallel copies of those fields.
- Pre-initialization option values still apply synchronously; after initialization starts, option coalescing/order, reset ordering, failure continuation, initialization barrier, cancellation admission, and drain semantics remain exact. Polling remains timer-driven, drops busy ticks, and is not newly serialized against option/reset work.
- MainForm remains the composition root and retains PawnIO prompting, Computer construction and option mapping, timer/BackgroundWorker scheduling, update then log poll body, UI-thread redraw order, server quit ordering, UI resource disposal, final settings save, and Application.Exit behavior.
- Shutdown stops new admission, cancels pending lifecycle work, stops the server, awaits initialization, ordered option/reset drain, and any active poll before closing Computer exactly once; late completion cannot redraw after shutdown and gates are not disposed while work remains.
- The eight new lifecycle facts pass 8/8; the unchanged HardwareOperationCoordinator and UiShutdownCoordinator families plus the new facts pass 27/27; the existing Computer lifetime families pass 18/18.
- The Application suite is exactly 160 discovered, 159 passed, and the one established live-config opt-in skip; the deterministic aggregate is exactly 301 discovered, 300 passed, and that same one skip. Contracts remain 73/73 and the protected data.json golden blob/SHA remain exact.
- Both WinForms x64 Release targets build with zero warnings/errors; Avalonia remains 75/75 with only established AVLN3001; web remains 315/315 plus 18/18; and all eight included non-deploying CI gates pass without changing a gate.
- Exclusive worktree ownership, dependency-first integration, commit/file/patch checks, plan validation/preflight/analyzer, docs synchronization, Git checks, and guarded output cleanup pass; only proven Plan-010 worktrees/branches and reproducible outputs are removed while ignored ledger/cache and preserved Plan-001 branches remain.
- Read-only VERIFIED SND-HOST proof retains the exact separate live process/task, HTTP 200 for root/data.json/metrics, and growing current-day CSV without any source-to-live, candidate, task, configuration, log, or operations mutation.
- Plan state remains executed and the ledger records implemented, never automatic acceptance; Phase 2 remains partial, Phase 4 item 3 completes while Phase 4 remains in progress, Plan-011/agent-ap becomes next, A1 remains person-only, and no candidate, deployment, promotion, or live cutover is claimed.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| LibreHardwareMonitor.Windows.Forms/UI/ApplicationLifecycleCoordinator.cs | new | create | high |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/ApplicationLifecycleCoordinatorTests.cs | new | create | medium |
| LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs | 2574 | modify | high |
| LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs | 109 | modify | low |
| .codex/skills/project.toml | 131 | modify | medium |
| data/plans/plan-010.json | new | create | medium |
| docs/campaign-plan-010-application-lifecycle-and-polling.md | new | create | low |
| docs/README.md | 439 | modify | low |
| docs/architecture/refactor-roadmap.md | 312 | modify | medium |
| docs/campaign-backlog.md | 141 | modify | low |
| docs/campaign-history.md | 234 | modify | medium |
| docs/features/feature-memory-ui-reliability.md | 422 | modify | medium |
| live-tracker.md | 116 | modify | low |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| am | lifecycle-polling-coordinator | Add and deterministically test one delegate-driven application lifecycle coordinator that owns initialization, existing ordered option/reset work, single-flight poll admission/completion, lifecycle cancellation/gating, and idempotent drain/stop without UI or hardware implementation dependencies. |  | LibreHardwareMonitor.Windows.Forms/UI/ApplicationLifecycleCoordinator.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/ApplicationLifecycleCoordinatorTests.cs | 0 | high |
| an | wire-mainform-lifecycle | Wire the tested lifecycle coordinator into MainForm while preserving timer, BackgroundWorker, PawnIO, server, UI-thread, logging, redraw, settings-save, and disposal behavior; correct the stale BeginInvoke shutdown comment only. | am | LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs, LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs | 1 | high |
| ao | verify-document-close | Independently verify the integrated lifecycle seam, exact deterministic and external contracts, both framework targets, non-deploying gates, repository ownership, and read-only live separation; then update only campaign/configuration/documentation truth and record implemented rather than accepted. | am, an | .codex/skills/project.toml, data/plans/plan-010.json, docs/campaign-plan-010-application-lifecycle-and-polling.md, docs/README.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, docs/features/feature-memory-ui-reliability.md, live-tracker.md | 2 | high |

## 5. Dependency Graph

```text
Group 0: am
Group 1: an
Group 2: ao
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| LibreHardwareMonitor.Windows.Forms/UI/ApplicationLifecycleCoordinator.cs | am |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/ApplicationLifecycleCoordinatorTests.cs | am |
| LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs | an |
| LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs | an |
| .codex/skills/project.toml | ao |
| data/plans/plan-010.json | ao |
| docs/campaign-plan-010-application-lifecycle-and-polling.md | ao |
| docs/README.md | ao |
| docs/architecture/refactor-roadmap.md | ao |
| docs/campaign-backlog.md | ao |
| docs/campaign-history.md | ao |
| docs/features/feature-memory-ui-reliability.md | ao |
| live-tracker.md | ao |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| application lifecycle and hardware ownership | initialization, polling, option/reset admission, and shutdown ordering | AM exclusively owns the new coordinator and tests; AN starts only after AM integration and exclusively owns MainForm plus the comment-only UiShutdown correction. HardwareOperationCoordinator and Computer remain byte-identical, and poll/option concurrency is preserved. |
| deterministic lifecycle tests | new lifecycle facts and the existing coordinator characterization baseline | AM owns only the new test file; existing test files remain byte-identical. Verify exactly 8 new facts and exactly 27 facts across all three families. |
| external snapshot and HTTP contracts | integration regression only; no authorized source change | Keep every listed file untouched and prove the exact protected golden blob/SHA plus the full Contracts suite. |
| settings, runtime paths, and PawnIO composition | MainForm continues to compose these dependencies while their implementations and persisted contracts remain unchanged | AN preserves the existing startup, PawnIO, settings-save, and cleanup ordering; all non-MainForm files in this zone remain untouched and their later extraction stays in Plan-011. |
| campaign truth and gate configuration | single-source campaign status, conflict-zone inventory, and implementation evidence | AO is the sole writer after source integration, changes project.toml only for the new conflict-zone member, and renders campaign markdown only from the plan JSON. |
| live operations boundary | read-only runtime separation proof only | Use verified SND-HOST read-only process, task, endpoint, and CSV observations; make no live, candidate, task, configuration, log, or operations write. |

## 8. Integration Points

- AM defines and tests the delegate-driven coordinator API first and commits only the new coordinator and its eight-fact test file from a clean baseline.
- AN starts only after AM is integrated; MainForm consumes the coordinator, removes duplicate lifecycle fields, preserves BackgroundWorker and timer ownership, and does not newly serialize polling with option/reset work.
- The new coordinator wraps the existing HardwareOperationCoordinator rather than rewriting it; HardwareOperationCoordinator.cs, its tests, UiShutdownCoordinatorTests.cs, and Computer.cs remain untouched.
- AN corrects only the stale BeginInvoke wording in UiShutdownCoordinator.cs; no executable behavior changes in that file.
- AO starts only after integrated source is clean, verifies exact diff/count/golden/build/gate/live evidence, and then writes campaign/configuration/documentation truth once.
- AM and AN return tracker-row evidence to AO; live-tracker.md has one writer for the campaign.
- No candidate, deployment, promotion, acceptance, scheduled-task, configuration, or live-runtime mutation is an integration step.

## 9. Schema Changes

- {'schema': 'No external or persisted schema change', 'compatibility': 'No settings keys, routes, payloads, CSV, Prometheus, hardware identifiers, project graph, package graph, or public API change; the new coordinator is internal.', 'migration': 'None. No data, configuration, candidate, or runtime migration.'}

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| The extracted coordinator changes option/reset ordering or admission behavior. | medium | high | Wrap HardwareOperationCoordinator unchanged, retain pre-initialization synchronous option application, and pass all 19 existing coordinator facts plus the eight new lifecycle facts. |
| An active poll races Computer.Close or polling becomes serialized with option/reset work. | medium | high | Give polling an independent single-flight admission/completion barrier, preserve poll/option overlap, and require stop to drain any admitted poll before close. |
| Open failure or non-cancellable initialization leaves shutdown waiting forever or closes more than once. | medium | high | Always complete the initialization barrier, have stop await the same initialization task, and prove concurrent stop calls share one completion and invoke close exactly once. |
| MainForm wiring drifts PawnIO, server, logging, redraw, UI-thread, settings-save, or disposal ordering. | medium | high | AN is the sole MainForm owner, retains the existing BackgroundWorker and timer, preserves update-then-log and shutdown call order, and receives an exact diff review before integration. |
| The coordinator uses APIs unavailable to the net472 target. | low | high | Use the repository's established Task, CancellationToken, SemaphoreSlim, and lock patterns; change no project/package files and build both net10.0-windows and net472. |
| Closure documentation overstates acceptance, Phase 2 completion, or deployment. | low | medium | AO updates only the exact listed truth surfaces, records implemented rather than accepted, keeps A1 person-only and Phase 2 partial, and makes no deployment claim. |
| The analyzer's 147 unassigned inventory files are mistaken for authorized scope. | low | high | Treat the warning as inventory only and reject any product/test/config/docs diff outside the exact agent ownership map. |

## 11. Verification Strategy

- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --filter FullyQualifiedName~ApplicationLifecycleCoordinatorTests
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --filter FullyQualifiedName~ApplicationLifecycleCoordinatorTests|FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --filter FullyQualifiedName~ComputerOpenLifetimeTests|FullyQualifiedName~HardwareGroupLifetimeCharacterizationTests
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1; node webtests\selftest.node.js; node --test webtests\console.tests.js webtests\workspace.tests.js
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
- python scripts\task_manager.py plan validate plan-010 --json; python scripts\task_manager.py plan preflight --json; python scripts\task_manager.py analyze --json; git diff --check

## 12. Documentation Updates

- Update .codex/skills/project.toml only to add ApplicationLifecycleCoordinator.cs to the existing MainForm/Computer lifecycle conflict zone; the existing WinForms smart-test wildcard already maps it, so add no redundant mapping or gate.
- Update docs/README.md, docs/architecture/refactor-roadmap.md, and docs/campaign-backlog.md to mark Phase-4 item 3 implemented, keep Phase 2 partial and A1 open, and advance to Plan-011/agent-ap without a deploy claim.
- Update docs/features/feature-memory-ui-reliability.md with the extracted ownership and verified polling/shutdown drain boundary, without closing the separate transactional runtime-option or failed-reset policy follow-ups.
- Update data/plans/plan-010.json, generated campaign markdown, docs/campaign-history.md, and live-tracker.md with criterion-specific implemented evidence; acceptance remains person-only.


## R1. Roadmap Phase

Phase: Phase 4 - Application and adapter seams
Roadmap reference: docs/architecture/refactor-roadmap.md

## R2. Behavioral Invariants

- MainForm remains the WinForms composition root and the sole current process and hardware owner.
- Computer Open, option, Reset, Close, event, and transactional rollback semantics remain unchanged.
- Polling cadence, single-flight admission, logging threshold, redraw ordering, and late-completion suppression remain behaviorally compatible.
- Plan-008 snapshot and data.json golden bytes, HTTP routes, CSV, Prometheus, settings, and hardware identities remain unchanged.
- No candidate, deployment, promotion, live-runtime, scheduled-task, configuration, or operations mutation occurs.

## R3. Rollback Strategy

Revert the Plan-010 source, test, configuration, and documentation commits; no schema, persisted-state, candidate, deployment, or live-runtime rollback is required.

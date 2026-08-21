# Campaign — Characterization tests before extraction

**Plan ID:** plan-007
**Date:** 2026-08-01
**Status:** executed — implemented; maintainer acceptance pending
**Baseline:** `e506fa62ab070ce088fb9a871c516a8020267905`
**Plan file:** `data/plans/plan-007.json`
**Planner kind:** `planner-refactor`
**Source roadmap:** `docs/architecture/refactor-roadmap.md`

## 1. Goal

Add a deterministic, test-only safety net that pins the current hardware group lifetime, ordered option/reset, settings projection/persistence, and shutdown coordination behavior required by the Phase-4 and Phase-5 extraction sequence, without creating a new production seam or touching live runtime state.

## 2. Exit criteria

- The campaign diff outside campaign documentation is limited to six exclusively owned test files: one new Library characterization file, one new Application projection file, and four existing Application test files. No production, project, package, gate, golden-master, or operational file changes.
- AC adds four passing hardware lifetime facts covering `Computer` dynamic-group registration, forwarding, detachment, reverse close, rollback/retry, NVIDIA partial factory cleanup and lease release, and storage partial factory cleanup and unsubscribe.
- AD adds eight passing facts covering active same-key option coalescing, reset follow-up ordering, typed failure events with continued drain, cancellation admission, UI-close takeover, BeginInvoke-style shutdown waiting, shared fault completion, and dispatch-failure retry.
- AE adds eight passing facts covering `UserOption` and `UserRadioGroup` projection/event ordering and null-name behavior, malformed typed-value fallbacks, duplicate-key last-wins normalization, and managed-path startup fail-closed behavior.
- All characterization tests use test-local fakes, in-memory settings, managed-path injection, or unique temporary directories. They do not construct `MainForm` or access live hardware, Task Scheduler, registry, listener ports, production configuration, or attended test surfaces.
- The deterministic filter discovers exactly 279 cases: 278 pass and the one existing `LHM_LIVE_CONFIG_PATH` opt-in case skips. Every new case passes, and no baseline case is lost, duplicated, or newly skipped.
- Focused Library and Application filters, the complete deterministic filter, both WinForms x64 Release framework builds, the full CI sweep, planner preflight, and Git diff checks pass.
- Exclusive worktree ownership is preserved, implementation lanes return committed test-only changes, integration is smallest-diff-first, and unrelated dirty or ignored state is neither merged nor removed.
- Read-only SND-HOST proof confirms the live deployment remains separate and healthy while tasks, settings, logs, release store, rollback store, and the non-Git operations tree remain untouched.
- The plan and campaign history record criterion-specific evidence with ledger state `implemented`, never automatic acceptance; Phase 3 closes, the backlog advances to Plan-008, and the tracker records all four lanes.

## 3. Impact assessment

| File | Change | Risk and guard |
|---|---|---|
| `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupLifetimeCharacterizationTests.cs` | add | High-complexity lifecycle fakes; AC owns the file and proves four focused facts plus the full Library suite. |
| `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/HardwareOperationCoordinatorTests.cs` | extend | Async ordering can race; AD uses explicit barriers and bounded waits. |
| `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/UiShutdownCoordinatorTests.cs` | extend | Exactly-once/fault semantics; AD asserts shared task identity and exact exception identity. |
| `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsProjectionTests.cs` | add | WinForms menu projection only; AE uses in-memory settings and no `MainForm`. |
| `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsPersistenceTests.cs` | extend | Temp XML only; duplicate-key normalization must not alter production code. |
| `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/StartupManagerTests.cs` | extend | Managed-path injection only; real scheduler/registry branches are forbidden. |
| `data/plans/plan-007.json` and this document | record | AF is the sole closure writer after all implementation commits are integrated. |
| `docs/architecture/refactor-roadmap.md` | close Phase 3 | AF closes only the characterization item; Phase 4 remains not started. |
| `docs/campaign-backlog.md` | advance queue | AF preserves person-only A1 and moves the executable head to Plan-008. |
| `docs/campaign-history.md` | add ledger evidence | AF records `implemented`, not maintainer acceptance. |
| `live-tracker.md` | add four rows | AF is the sole tracker writer and uses actual commit/test evidence. |

## 4. Agent roster

| Letter | Lane | Scope | Dependencies | Group |
|---|---|---|---|---|
| AC | hardware-lifetime-characterization | One new Library file; four hardware group lifetime facts. | none | 0 |
| AD | option-reset-shutdown-characterization | Two existing Application files; eight ordering/shutdown facts. | none | 0 |
| AE | settings-projection-characterization | One new and two existing Application files; eight settings facts. | none | 0 |
| AF | verify-document-close | Independent full verification and six campaign-truth files. | AC, AD, AE integrated | 1 |

The executable details and exact fact names live in `agents/agent-ac-hardware-lifetime-characterization.md`, `agents/agent-ad-option-reset-shutdown-characterization.md`, `agents/agent-ae-settings-projection-characterization.md`, and `agents/agent-af-verify-document-close.md`.

## 5. Dependency graph

```text
Group 0: AC ─┐
         AD ─┼─> integrate test-only commits ─> AF verify/document/close
         AE ─┘
```

## 6. File ownership map

| Owner | Exclusive files |
|---|---|
| AC | `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupLifetimeCharacterizationTests.cs` |
| AD | `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/HardwareOperationCoordinatorTests.cs`; `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/UiShutdownCoordinatorTests.cs` |
| AE | `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsProjectionTests.cs`; `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsPersistenceTests.cs`; `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/StartupManagerTests.cs` |
| AF | `data/plans/plan-007.json`; this document; `docs/architecture/refactor-roadmap.md`; `docs/campaign-backlog.md`; `docs/campaign-history.md`; `live-tracker.md` |

The repository analyzer also reports 131 non-project files as “unassigned.” They are outside this campaign’s intended touch set; all twelve intended implementation and closure paths above have one owner. This warning is acknowledged, not treated as permission to edit those files.

## 7. Conflict-zone analysis

| Zone | Affected | Mitigation |
|---|---|---|
| `MainForm.cs` + `Computer.cs` lifecycle ownership | read-only behavior source | No production edit; tests target existing coordinator/group seams. |
| `PersistentSettings.cs` + `RuntimePaths.cs` + `StartupManager.cs` | read-only behavior source | AE uses in-memory/temp/managed-path injection; default machine-local branches are forbidden. |
| Application test project | AD and AE work in the same project but different files | Exclusive file lists; no csproj edit; integrate smallest diff first. |
| Campaign plan + history + roadmap + backlog + tracker | closure truth | AF is the sole writer after implementation integration and receives result payloads from all lanes. |
| Live deployment and `E:\SQ_HQ\Monitoring` operations tree | explicitly out of source campaign | Read-only separation proof only; no restart, publication, config, task, log, or operations write. |

## 8. Integration points

- AC started from `e506fa6`, committed source result `f019266`, and was integrated as `691c6dc`; its isolated change is the one owned Library file.
- AD started from the same baseline, committed source result `f3ce3f7`, and was integrated as `74592e4`; its isolated change is the two owned Application files.
- AE started from the same baseline, committed source result `628debb`, and was integrated as `ca5b471`; its isolated change is the three owned Application files.
- AF started from integrated `ca5b471`, independently reproduced every focused, aggregate, build, CI, planner, diff, ownership, and live-separation gate, and wrote all six campaign-truth surfaces once.
- Aggregate population was not inferred from individual exits: the integrated solution filter proved exactly 279/278/1.
- No `MainForm` test seam is introduced. That extraction belongs to the later application-lifecycle/settings campaigns.

## 9. Schema changes

None. Test population grows by 20 `[Fact]` cases; settings formats, `data.json`, HTTP, CSV, Prometheus, project files, package versions, and plan schema remain unchanged.

## 10. Risk assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| A characterization test encodes desired behavior instead of current behavior | medium | high | Fact names and assertions come from source-level discovery; no production change is allowed to make a fact pass. |
| Async option/shutdown facts are flaky | medium | high | Use explicit completion barriers, run-continuations-asynchronously sources, bounded waits, and exact ordered assertions. |
| Hardware lifetime fakes miss cleanup order | medium | high | Assert handler counts, close timeline, owner counts, exact exception identity, and retry on the same `Computer`. |
| A settings test touches machine state | low | high | Ban `MainForm`, `RuntimePaths.Current`, default `StartupManager`, live-config variables, registry, and Task Scheduler. |
| Parallel lanes overlap | low | medium | Exclusive file ownership and worktree preflight; AD and AE share only a project boundary, not a file. |
| Test population silently changes | medium | high | Baseline is 259/258/1; close requires exact integrated 279/278/1 with the same named skip. |
| Broad analyzer “unassigned” warning is mistaken for campaign scope | medium | medium | Only the twelve mapped paths are authorized; the remaining inventory is explicitly out of scope. |

## 11. Verification strategy

- Pre-implementation baseline at `e506fa6`: the recorded deterministic population is 259 discovered, 258 passed, one opt-in skip.
- AC focused filter: exactly 4/4; full Library: 68/68.
- Each isolated AD or AE lane grows Application from 131 to 139 cases (138 passed and one existing skip); after both lanes integrate, Application must be 147 discovered, 146 passed, and that same skip.
- AE focused new facts: exactly 8/8; settings regression includes `SettingsPersistenceTests`, `RuntimePathsTests`, and `StartupManagerTests` with both live-config variables unset.
- Integrated deterministic filter: exactly 279 discovered, 278 passed, one existing skip.
- Configured builds:
  - `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`
  - `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64`
- Full source gates: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All`, planner preflight, `git diff --check`, ownership diff, and worktree inventory.
- Read-only live proof: verified SND-HOST identity, process/executable ownership, scheduled-task action and working directory, proxy-bypassed `/`, `/data.json`, `/metrics` HTTP status, and current-day CSV growth.

## 12. Implementation and closure evidence

- Integrated source sequence: AC `f019266` → `691c6dc`, AD `f3ce3f7` → `74592e4`, AE `628debb` → `ca5b471`; `git diff --name-status e506fa6..ca5b471` contains exactly the six owned test files and no production, project, package, gate, golden-master, or operations path.
- Independent focused filters: hardware lifetime 4/4; option/reset and shutdown 19/19; settings regression 59 discovered, 58 passed, and the one existing `LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets` opt-in skip.
- Independent aggregate: Library 68/68, Contracts 64/64, Application 147 discovered/146 passed/one existing skip — exactly 279/278/1.
- Both WinForms x64 Release builds passed with zero warnings and zero errors. The final non-deploying CI sweep passed all eight included gates with the self-referential `ci-gates` entry skipped by design. Pre-commit planner preflight was ready with zero errors and only the expected dirty-worktree warning; the required post-commit clean-tree preflight returned zero errors and warnings. Git diff checks passed.
- Identity was `VERIFIED` for `snd-host`. At 2026-08-01 12:50:40+01:00, exactly one live process (PID 14876) owned the declared executable, root task `\LibreHardwareMonitor` was `Running` with result 267009 and matching action/working directory, and proxy-bypassed `/`, `/data.json`, and `/metrics` returned HTTP 200 at `192.168.2.5:8080`. The current-day CSV was 102,429,441 bytes, 1,506,420 bytes larger than the supplied 12:35:42 sample. The final sample at 2026-08-01 12:59:02+01:00 retained the same process/task/endpoint proof and measured 103,246,180 bytes: +816,739 from the first AF sample and +2,323,159 from the supplied before sample. No live or operations write occurred.
- Every exit criterion is recorded `met` in `docs/campaign-history.md`. Ledger state is `implemented`, not `accepted`; acceptance remains person-only.

## 13. Documentation updates

- This document and `data/plans/plan-007.json`: execution state, exact counts, per-criterion evidence, rollback boundary.
- `docs/architecture/refactor-roadmap.md`: Phase-3 characterization complete; Phase 4 still not started.
- `docs/campaign-backlog.md`: Plan-007 removed; Plan-008 becomes next executable campaign; A1 retained.
- `docs/campaign-history.md`: Plan-007 ledger row and criterion evidence, state `implemented` only.
- `live-tracker.md`: actual AC, AD, AE, and AF commits/results, written once by AF.

## R1. Roadmap phase

Phase 3 — Verification suite boundaries, final contracts/baseline step before extraction. Roadmap authority: `docs/architecture/refactor-roadmap.md`.

## R2. Behavioral invariants

- Production source and external contracts remain byte-identical; Plan-007 adds deterministic characterization tests and campaign documentation only.
- The deterministic baseline remains green and grows from 259/258/1 only by 20 new passing cases.
- Tests assert observed behavior without `MainForm`, live hardware, Task Scheduler, registry, listener ports, production configuration, or attended tests.
- Both WinForms x64 Release targets and the full CI sweep remain green.
- Live SND-HOST runtime, tasks, settings, logs, release store, rollback store, and operations tree remain untouched.

## R3. Rollback strategy

Revert the Plan-007 test-and-documentation commits and remove only campaign-owned Git worktrees/branches. No production or live-runtime rollback is required because the campaign performs no deployment or runtime mutation.

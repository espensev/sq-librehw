# Campaign — Hardware lifecycle seams

**Plan ID:** plan-013
**Date:** 2026-08-03
**Status:** executed
**Plan file:** data/plans/plan-013.json
**Plan doc:** docs/campaign-plan-013-hardware-lifecycle-seams.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/architecture/refactor-roadmap.md

---

## 1. Goal

Introduce explicit internal lifecycle boundaries around the Computer group registry and separate discovery, update, and close behavior in the NVIDIA and storage groups, so hardware lifetimes have replaceable tested seams while Computer keeps lifecycle guards and category policy, the public library API stays upstream-identical, per-group Hardware exposure semantics stay pinned, and WinForms remains the sole hardware owner.

## 2. Exit Criteria

- Outside campaign/configuration/documentation truth, the exact diff is Computer.cs plus the new HardwareGroupRegistry.cs and its Library test file for AW, NvidiaGroup.cs plus one new NVIDIA lifecycle collaborator file and its Library test file for AX, and StorageGroup.cs plus one new storage lifecycle collaborator file and its Library test file for AY; every other LibreHardwareMonitorLib, WinForms, test, gate, golden, web, and operations file remains unchanged.
- AW extracts one internal net472-compatible HardwareGroupRegistry owning the group list, Add/Remove/RemoveType, reverse-order drain, IHardwareChanged forwarding, notification ordering, and failure aggregation, with new deterministic Fact tests pinning registration/dedupe, forwarding only while registered, reverse drain, notifications outside the lock, and first-failure aggregation; Computer keeps lifecycle guards, retry/versioning, OpenDependencies, SMBios, and category policy.
- AW adds characterization facts pinning the AddGroups category order, the IntelGpu-depends-on-CpuGroup discovery quirk, and the preserved AMD/Intel live-list Hardware exposure semantics, proving the registry never assumes snapshot publishing.
- AX separates NVIDIA discovery, background-monitor update, and close into internal collaborators behind the unchanged NvidiaGroup IGroup facade, with new deterministic facts pinning driver-restart lease-first ordering, late lease release on in-flight refresh, the bounded monitor error queue, handle-diff commit ordering, and the bounded monitor join that never runs on the monitor task itself.
- AY separates storage discovery, change application, and close/unsubscribe-retry into internal collaborators behind the unchanged StorageGroup IGroup facade, with new deterministic facts pinning subscribe-before-enumerate, the bounded pending-change coalescing, redundant late-add close, and unsubscribe-failure retry ownership.
- Every new seam type is internal and snapshot-native: new internal contracts expose IReadOnlyList hardware snapshots and no mutable IHardware tree to any host; the public LibreHardwareMonitorLib API including IComputer remains byte-identical and WinForms remains the sole hardware owner.
- The new deterministic facts pass exactly, the Library suite total grows by exactly the new facts with no regressions, Application remains 178 passed plus the one established live-config opt-in skip of 179, Contracts remain 73/73, and the deterministic aggregate grows by exactly the new facts with that same single skip.
- Both WinForms x64 Release targets build with zero warnings/errors; Avalonia remains 75/75 with only established AVLN3001; web remains 315/315 plus 18/18; and all eight non-deploying CI gates pass without changing a gate.
- Plan-007 hardware lifetime characterization, Plan-008 immutable snapshot and protected data.json golden, Plan-009 HTTP wire contract, Plan-010 lifecycle/polling/shutdown, Plan-011 persistence boundary, Plan-012 presentation seam, CSV, Prometheus, hardware identity, settings schema, runtime paths, project graph, and package graph remain unchanged.
- Exclusive inline ownership, dependency-first integration, source diff checks, plan validation/preflight/analyzer, documentation synchronization, Git checks, generated-plan render idempotence, and guarded output cleanup pass.
- Read-only VERIFIED SND-HOST proof confirms the separate live process/task, HTTP 200 root/data.json/metrics, and growing current-day CSV without any candidate, deployment, promotion, task, configuration, log, or operational-tree mutation.
- Plan status remains executed and ledger state becomes implemented, never automatic acceptance; Phase 5 completes, Plan-014 stays gated behind explicit maintainer authorization, agent ba becomes next, and A1 remains person-only.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| LibreHardwareMonitorLib/Hardware/Computer.cs | 1255 | modify | high |
| LibreHardwareMonitorLib/Hardware/HardwareGroupRegistry.cs | new | create | high |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupRegistryTests.cs | new | create | medium |
| LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGroup.cs | 689 | modify | high |
| LibreHardwareMonitorLib/Hardware/Gpu/Nvidia/NvidiaGroupLifecycle.cs | new | create | high |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/NvidiaGroupLifecycleCharacterizationTests.cs | new | create | medium |
| LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs | 442 | modify | high |
| LibreHardwareMonitorLib/Hardware/Storage/StorageGroupLifecycle.cs | new | create | high |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/StorageGroupLifecycleCharacterizationTests.cs | new | create | medium |
| .codex/skills/project.toml | 132 | modify | medium |
| data/plans/plan-013.json | new | create | medium |
| docs/campaign-plan-013-hardware-lifecycle-seams.md | new | create | low |
| docs/README.md | 458 | modify | low |
| docs/architecture/refactor-roadmap.md | 349 | modify | medium |
| docs/campaign-backlog.md | 116 | modify | low |
| docs/campaign-history.md | 314 | modify | medium |
| docs/features/feature-hardware-lifecycle-seams.md | new | create | low |
| live-tracker.md | 155 | modify | low |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| aw | hardware-group-registry | Extract the group registry out of Computer.cs into an internal HardwareGroupRegistry owning the group list, Add/Remove/RemoveType, reverse-order drain, IHardwareChanged forwarding, notification ordering, and failure aggregation, with new deterministic characterization facts pinning AddGroups category order, the IntelGpu-depends-on-CpuGroup quirk, and AMD/Intel live-list Hardware semantics; Computer keeps lifecycle guards, retry/versioning, OpenDependencies, SMBios, and category policy. |  | LibreHardwareMonitorLib/Hardware/Computer.cs, LibreHardwareMonitorLib/Hardware/HardwareGroupRegistry.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupRegistryTests.cs | 0 | high |
| ax | nvidia-lifecycle-seams | Separate discovery, background-monitor update, and close behavior inside NvidiaGroup into internal collaborators while NvidiaGroup remains the IGroup facade, with new deterministic characterization facts pinning the driver-restart lease-first ordering, late lease release, monitor error cap, handle-diff commit order, and bounded monitor join; no public API, hardware behavior, or NVML lifetime change. | aw | LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGroup.cs, LibreHardwareMonitorLib/Hardware/Gpu/Nvidia/NvidiaGroupLifecycle.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/NvidiaGroupLifecycleCharacterizationTests.cs | 1 | high |
| ay | storage-lifecycle-seams | Separate discovery, change application, and close/unsubscribe-retry behavior inside StorageGroup into internal collaborators while StorageGroup remains the IGroup facade, with new deterministic characterization facts pinning subscribe-before-enumerate, pending-change coalescing bounds, redundant late-add close, and unsubscribe-failure retry ownership; no public API, device, or DiskInfoToolkit behavior change. | aw | LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs, LibreHardwareMonitorLib/Hardware/Storage/StorageGroupLifecycle.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/StorageGroupLifecycleCharacterizationTests.cs | 1 | high |
| az | verify-document-close | Independently verify the integrated hardware lifecycle seams, protected external/lifecycle/settings/snapshot contracts, dual-target builds, non-deploying gates, source ownership, and read-only live separation; then update the sole campaign/documentation/tracker truth surfaces. | aw, ax, ay | .codex/skills/project.toml, data/plans/plan-013.json, docs/campaign-plan-013-hardware-lifecycle-seams.md, docs/README.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, docs/features/feature-hardware-lifecycle-seams.md, live-tracker.md | 2 | high |

## 5. Dependency Graph

```text
Group 0: aw
Group 1: ax, ay
Group 2: az
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| LibreHardwareMonitorLib/Hardware/Computer.cs | aw |
| LibreHardwareMonitorLib/Hardware/HardwareGroupRegistry.cs | aw |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupRegistryTests.cs | aw |
| LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGroup.cs | ax |
| LibreHardwareMonitorLib/Hardware/Gpu/Nvidia/NvidiaGroupLifecycle.cs | ax |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/NvidiaGroupLifecycleCharacterizationTests.cs | ax |
| LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs | ay |
| LibreHardwareMonitorLib/Hardware/Storage/StorageGroupLifecycle.cs | ay |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/StorageGroupLifecycleCharacterizationTests.cs | ay |
| .codex/skills/project.toml | az |
| data/plans/plan-013.json | az |
| docs/campaign-plan-013-hardware-lifecycle-seams.md | az |
| docs/README.md | az |
| docs/architecture/refactor-roadmap.md | az |
| docs/campaign-backlog.md | az |
| docs/campaign-history.md | az |
| docs/features/feature-hardware-lifecycle-seams.md | az |
| live-tracker.md | az |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| Computer group registry and lifecycle guards | Computer.cs and the new HardwareGroupRegistry.cs only | AW is the sole owner of Computer.cs and the registry. The registry takes the group list, add/remove/drain, forwarding, and notification machinery; Computer keeps guards, retry/versioning, OpenDependencies, SMBios, and category policy. No other campaign file may edit Computer.cs. |
| NVIDIA discovery, monitor, and close pipeline | NvidiaGroup.cs and the new NvidiaGroupLifecycle.cs only | AX is the sole owner. NvidiaGroup remains the IGroup facade; collaborators stay internal; NVML lease owner, NvApi interop, and monitor timing behavior are unchanged. Existing NvidiaGroupSnapshotTests.cs is not modified. |
| Storage discovery, change application, and close pipeline | StorageGroup.cs and the new StorageGroupLifecycle.cs only | AY is the sole owner. StorageGroup remains the IGroup facade; DiskInfoToolkit interop and device classes are unchanged. Existing StorageGroupLifetimeTests.cs is not modified. |
| Library suite population and existing characterization | Three new Library test files only | Each implementation agent owns exactly one new test file; Plan-007 characterization files (HardwareGroupLifetimeCharacterizationTests.cs, ComputerOpenLifetimeTests.cs, NvidiaGroupSnapshotTests.cs, StorageGroupLifetimeTests.cs) stay byte-identical, and totals are reconciled exactly at close. |
| Plans 007-012 protected seams and external contracts | Verification only | Snapshot/projector, data.json golden, HTTP listener/facade, lifecycle coordinators, SettingsPersistenceCoordinator, PersistentSettings, RuntimePaths, presentation coordinator/adapters, CSV, and Prometheus files stay unchanged; focused and full suites must remain green. |
| campaign truth surfaces | docs/README.md, live-tracker.md, data/plans/, campaign history/backlog/roadmap, feature doc, project.toml | AZ is the single writer of every campaign/documentation truth surface. AW, AX, and AY return proposed tracker row text in their result payloads and never edit the tracker. |

## 8. Integration Points

- AW commits the internal HardwareGroupRegistry extraction and its new deterministic facts first; Computer delegates group storage, forwarding, notification, and drain while retaining every guard and policy decision.
- AX starts only after AW is integrated and separates NVIDIA discovery, monitor update, and close behind the unchanged NvidiaGroup facade; its facts run against the existing internal-constructor injection seams.
- AY starts only after AW is integrated and separates storage discovery, change application, and close/unsubscribe-retry behind the unchanged StorageGroup facade; AX and AY own disjoint files and may integrate in either order.
- The registry and both collaborator sets expose IReadOnlyList hardware snapshots at their internal boundaries and never hand a mutable IHardware tree to a new host; WinForms MainForm remains the sole hardware owner and no host is added.
- AW, AX, and AY each return proposed tracker evidence to AZ. AZ starts from a clean integrated tree, independently verifies exact diffs, counts, dual targets, non-deploying gates, protected contracts, and read-only live separation, then writes truth once.
- Execution is inline in the primary checkout in dependency order; no task_manager merge, candidate creation, deployment, promotion, acceptance, scheduled-task, configuration, log, CSV, rollback, or operational mutation is an integration step.

## 9. Schema Changes

- {'migration': 'None. No data, configuration, hardware-enumeration, candidate, deployment, or live-runtime migration.', 'schema': 'No persisted, external, or public schema change.', 'compatibility': 'No settings key/value, XML layout, data.json bytes, HTTP route, CSV, Prometheus, hardware identifier, sensor identity, public Lib API, package graph, or project graph change.'}

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| The registry extraction changes group add/remove/drain order or notification timing and breaks dynamic NVIDIA/storage behavior. | medium | high | Move the Add/Remove/RemoveType/RemoveGroups and forwarding machinery verbatim into the internal registry; pin order, dedupe, forwarding-while-registered, reverse drain, notifications outside the lock, and first-failure aggregation with new deterministic facts plus the existing Plan-007 lifetime characterization. |
| The registry assumes immutable snapshot publishing and breaks the AMD/Intel groups that expose live hardware lists. | medium | high | Characterization facts pin per-group Hardware exposure semantics before the move; the registry stores groups and reads their Hardware property without normalizing snapshot versus live-list behavior. |
| IntelGpu discovery depends on the CPU group through a temporary GetIntelCpus group and the seam silently drops that ordering quirk. | medium | high | A new characterization fact pins AddGroups category order including the IntelGpu-depends-on-CpuGroup quirk; Computer retains the category factory policy verbatim. |
| NVIDIA collaborator split disturbs driver-restart recovery, NVML lease lifetime, or the bounded monitor loop. | medium | high | Pin lease-first unavailable transitions, late lease release on in-flight refresh, the eight-entry monitor error queue, handle-diff commit ordering, and the bounded join that never runs on the monitor task; existing NvidiaGroupSnapshotTests must stay green unmodified. |
| Storage collaborator split disturbs the subscribe-before-enumerate blind window or unsubscribe-retry ownership and leaks devices. | medium | high | Pin subscribe-before-enumerate, the 256-entry pending-change coalescing, redundant late-add close, and retry ownership; existing StorageGroupLifetimeTests must stay green unmodified. |
| New seam types become public and break upstream mergeability or leak a mutable hardware tree to a future host. | low | high | All new types are internal, net472-compatible, and snapshot-native at their boundaries; IComputer and the public Lib surface stay byte-identical; the net472 Release build proves framework compatibility. |
| Protected external or application contracts drift even though the campaign is library-internal. | low | high | Reject diffs in snapshot, golden, HTTP, settings, lifecycle, presentation, CSV, Prometheus, project/package, web, and operations surfaces; run Library/Application/Contracts, aggregate, web, Avalonia, and all eight non-deploying gates. |
| Hardware-dependent behavior cannot be exercised deterministically on this host and a regression hides behind hardware absence. | medium | medium | Use the established internal-constructor dependency-injection fakes; no test touches real hardware, and read-only live separation proof confirms the untouched running instance. |

## 11. Verification Strategy

- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
- node webtests\selftest.node.js; node --test webtests\console.tests.js webtests\workspace.tests.js
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
- python scripts\task_manager.py plan validate plan-013 --json; python scripts\task_manager.py plan preflight --json; python scripts\task_manager.py analyze --json
- git diff --check; git status --short

## 12. Documentation Updates

- Regenerate docs/campaign-plan-013-hardware-lifecycle-seams.md only from the canonical plan JSON and prove byte-identical second render.
- Update docs/README.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, and live-tracker.md once through AZ after integrated verification, and add docs/features/feature-hardware-lifecycle-seams.md recording the new internal seams.


## R1. Roadmap Phase

Phase: Phase 5 - Hardware lifecycle seams
Roadmap reference: docs/architecture/refactor-roadmap.md

## R2. Behavioral Invariants

- WinForms remains the sole hardware, process, and scheduled-task owner.
- data.json shape/order/IDs, HTTP routes, Prometheus output, CSV behavior, settings schema, and hardware discovery, update, and close behavior do not change.
- The public LibreHardwareMonitorLib API stays upstream-identical; every new seam type is internal.
- Per-group Hardware exposure semantics are preserved, never normalized: NVIDIA and Storage publish immutable snapshots; AMD and Intel keep their live lists.
- Both net10.0-windows and net472 WinForms x64 Release builds remain gates.
- No live runtime, scheduled task, configuration, candidate, rollback, or operational tree changes.

## R3. Rollback Strategy

Revert the Plan-013 source commits in reverse dependency order (storage seams, NVIDIA seams, group registry) while preserving the pre-Plan-013 commit ce33b82; closure truth is reverted separately. No operational rollback packet is involved because the campaign creates no candidate or deployment.

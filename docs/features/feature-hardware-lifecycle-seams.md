# Hardware Lifecycle Seams: Computer Group Registry, NVIDIA and Storage Collaborators

**Status:** implemented in source (Plan-013, ledger `implemented`, 2026-08-03); source-only, no deployment
**Updated:** 2026-08-03

## Problem

`Computer` carried the hardware group registry mechanics inline — the group
list and lock, add/remove/drain, `IHardwareChanged` forwarding, notification
ordering, and failure aggregation — and `NvidiaGroup`/`StorageGroup` each mixed
discovery, background update, and close behavior into a single class. Phase 5
of `docs/architecture/refactor-roadmap.md` required explicit, replaceable,
tested lifecycle seams without changing the public API or hardware behavior.

## Boundary contract

- Every new seam type is `internal`; the public LibreHardwareMonitorLib API
  including `IComputer` is byte-identical (blob `d8933d84` at both ends).
- New internal boundaries expose `IReadOnlyList<IHardware>` snapshots; no
  mutable `IHardware` tree is handed to any host.
- WinForms remains the sole hardware, process, and scheduled-task owner.
- Per-group `Hardware` exposure semantics are preserved, never normalized:
  NVIDIA and Storage publish immutable snapshots; AMD and Intel keep their
  live lists.

## The registry boundary (AW, `8745660`)

`HardwareGroupRegistry` (internal sealed, net472-compatible) owns:

- the group list and its synchronization root;
- `Add` / `Remove` / `RemoveType` and the reverse-order `RemoveGroups` drain;
- `IHardwareChanged` attach/detach and event forwarding;
- hardware-added/removed notifications raised outside the lock;
- first-failure aggregation with `ExceptionDispatchInfo` rethrow.

It is constructed with `Func<HardwareEventHandler>` accessors so subscriber
presence is evaluated at raise time, preserving the pre-extraction semantics
where notifications and `Hardware` reads happen only when subscribers exist.

`Computer` keeps lifecycle guards, configuration-build retry/versioning
(`MaxConfigurationBuildAttempts`), `OpenDependencies`, SMBios ownership, and
category policy (the `AddGroups` order and per-category enable setters), with
`_groups`/`_lock` aliased to the registry.

## NVIDIA collaborator split (AX, `ff5c913`)

`NvidiaGroup` remains the `IGroup` facade. `NvidiaGroupLifecycle.cs` carries
four internal collaborators:

- `NvidiaGroupLifecycle` — discovery, update orchestration, and close;
- `NvidiaMlLeaseTracker` — NVML lease acquire/release with driver-restart
  lease-first ordering and late release while a refresh is in flight;
- `NvidiaMonitorLoop` — bounded background monitor with an eight-entry error
  queue and a bounded join that never runs on the monitor task itself;
- `NvidiaDiscovery` — device discovery and handle-diff commit ordering.

## Storage collaborator split (AY, `5de6307`)

`StorageGroup` remains a 72-line `IGroup` facade. `StorageGroupLifecycle.cs`
carries:

- `StorageGroupLifecycle` — subscribe-before-enumerate discovery, change
  application, and close with unsubscribe-failure retry ownership;
- `StorageInitializationChangeBuffer` — bounded 256-entry pending-change
  coalescing; redundant late additions are closed instead of leaked.

## Pinned quirks (25 new deterministic facts)

- `HardwareGroupRegistryTests` (11): registration order/dedupe/null tolerance,
  forwarding only while registered, unsubscribe failure still
  removes/notifies/closes and rethrows the first failure, reverse drain order,
  notifications outside the lock (a reentrant drain cannot deadlock),
  `RemoveType` first-failure with complete removal, and snapshot/close failure
  aggregation — plus `Computer` characterization pinning the `AddGroups`
  category order, the `GetIntelCpus` registered-lookup plus temporary-fallback
  quirk (IntelGpu depends on CpuGroup), and the AMD/Intel live-list `Hardware`
  exposure that proves the registry never assumes snapshot publishing.
- `NvidiaGroupLifecycleCharacterizationTests` (6): driver-restart lease-first
  ordering, late lease release on in-flight refresh, the bounded monitor error
  queue, handle-diff commit ordering, and the bounded monitor join.
- `StorageGroupLifecycleCharacterizationTests` (8): subscribe-before-enumerate,
  256-cap pending-change coalescing, redundant late-add close, and
  unsubscribe-failure retry ownership.

## What remains in the facades

- `Computer`: lifecycle guards, retry/versioning, `OpenDependencies`, SMBios,
  category policy, and the full `IComputer` public surface.
- `NvidiaGroup`: `IGroup` facade members (`Hardware`, `GetReport`, `Close`)
  delegating to the lifecycle collaborator.
- `StorageGroup`: the same facade shape, with the empty-hardware default before
  initialization.

## Verification

- Library 93/93 (68 + 11 + 6 + 8), Application 178 passed plus the one
  established live-config skip of 179, Contracts 73/73, deterministic aggregate
  344 passed plus that skip of 345.
- Both WinForms x64 Release targets 0W/0E; Avalonia 75/75 with only the
  established AVLN3001; web 315/315 plus 18/18; all eight non-deploying CI
  gates green with no gate or runner change.
- The data.json golden (blob `05113704a`, SHA-256 `BEBDE807…D2F3`) and every
  Plan-007 through Plan-012 protected surface are byte-identical.
- Read-only VERIFIED SND-HOST proof: the live process, root task, HTTP
  endpoints, and growing CSV were observed unchanged; nothing was restarted or
  written.

Full per-criterion evidence lives in `docs/campaign-history.md` (plan-013) and
`live-tracker.md` (Plan-013). This is a source-only structural change: it
created no candidate, deployment, promotion, or live mutation authority.

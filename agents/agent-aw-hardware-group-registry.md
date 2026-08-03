# Agent Task — Extract the Computer Hardware Group Registry

**Plan:** `plan-013`

**Baseline:** `ce33b82` (Plan-012 close, clean tree)

**Depends on:** none

**Exclusive output:**

- `LibreHardwareMonitorLib/Hardware/Computer.cs`
- `LibreHardwareMonitorLib/Hardware/HardwareGroupRegistry.cs` (new)
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupRegistryTests.cs` (new)

## Goal

Extract the group-registry concern out of `Computer` into one internal net472-compatible `HardwareGroupRegistry` that owns the group list, add/remove/drain mechanics, `IHardwareChanged` forwarding, notification ordering, and failure aggregation, while `Computer` keeps every lifecycle guard, retry/versioning loop, `OpenDependencies`, SMBios, mutex/opcode ownership, and category factory policy. No behavior changes.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-013-hardware-lifecycle-seams.md`
3. `LibreHardwareMonitorLib/Hardware/Computer.cs` (all of it; the registry candidate region is the `Add`/`Remove`/`RemoveType`/`RemoveGroups` quartet plus the forwarding/notification machinery, roughly lines 484–648 and 1100–1118)
4. `LibreHardwareMonitorLib/Hardware/IGroup.cs` and `IHardwareChanged.cs`
5. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupLifetimeCharacterizationTests.cs` and `ComputerOpenLifetimeTests.cs` (established fakes: `DynamicTestGroup`, `OpenProbe`)
6. `LibreHardwareMonitorLib/Hardware/Gpu/AmdGpuGroup.cs` and `IntelGpuGroup.cs` (live-list `Hardware` exposure; note `AmdGpuGroup.cs` is Latin-1 encoded — do not re-encode it)

Before editing, run the full Library suite plus Application and Contracts and both Release builds. Stop if the baseline is not green at `ce33b82`.

## Task

### Part 1 — Extract the registry verbatim

Create `internal sealed class HardwareGroupRegistry` in the new file. Move, do not rewrite:

- the `List<IGroup>` storage and its lock;
- `Add` semantics: null-guard, dedupe under lock, subscribe `IHardwareChanged` forwarding while registered, raise `HardwareAdded` per snapshot item outside the lock with first-failure capture;
- `Remove` semantics: remove plus best-effort unsubscribe under lock, raise `HardwareRemoved` outside the lock, then `group.Close()`, first failure rethrown via `ExceptionDispatchInfo`;
- `RemoveType<T>` first-failure aggregation;
- `RemoveGroups` reverse-of-registration drain.

`Computer` delegates to the registry and keeps: `_open/_opening/_closing/_resetting` guards, the enabled-change version and deferred reconfiguration, `ShouldApplyEnabledChange`/`CommitEnabledChange`, the stabilize-or-rollback `Open`/`Reset` loops, `RollbackOpen`, `Close`, `AddGroups` category policy, `GetIntelCpus`, SMBios, `OpenDependencies`, and `SharedOwner`. Keep `Traverse`, `Accept`, and `GetReport` snapshot behavior exactly as today (they may read group `Hardware` through the registry or directly — preserve the exact captured-snapshot semantics and ordering).

### Part 2 — Pin the boundary with deterministic facts

Add `HardwareGroupRegistryTests.cs` with deterministic `[Fact]` tests (reuse the established fake-group pattern; no hardware, no threads beyond what the existing fakes already use) covering at minimum:

1. registration order, dedupe, and null-guard;
2. `IHardwareChanged` events forward only while the group is registered, and unsubscribe is best-effort on removal;
3. `RemoveGroups` drains in exact reverse registration order;
4. `HardwareAdded`/`HardwareRemoved` are raised outside the registry lock (a handler that re-enters the registry does not deadlock);
5. first-failure aggregation: a throwing group close/snapshot does not mask earlier behavior and the first failure is the one rethrown;
6. `AddGroups` category order is pinned exactly (Motherboard → Cpu → Memory → AmdGpu → Nvidia → IntelGpu gated on Cpu → PowerMonitor → Controllers → Storage → Network → Psu → Battery), using the `OpenDependencies.AddGroups`-adjacent seams or recorded factory order;
7. the IntelGpu-depends-on-CpuGroup discovery quirk (`GetIntelCpus` temporary group) is preserved;
8. a group that exposes a **live mutable** `Hardware` list (AMD/Intel shape) is stored and read without the registry assuming immutable snapshots.

Facts 6–8 may live in this file using `Computer.OpenDependencies` injection; keep each fact deterministic and hardware-free.

## Exit Criteria

- Exactly the three owned files differ from baseline (two new, one modified).
- `HardwareGroupRegistry` and every new member are `internal`; the public Lib surface, including `IComputer`, is byte-identical.
- Existing `HardwareGroupLifetimeCharacterizationTests` (4 facts) and `ComputerOpenLifetimeTests` (13 facts) pass **unmodified**.
- New facts pass exactly; Library suite total is 68 plus exactly your new facts with zero regressions; Application remains `178+1/179`; Contracts remain `73/73`; deterministic aggregate grows by exactly your new facts with the same single skip.
- Both WinForms x64 Release targets build `0W/0E`.

## Constraints

- Modify only the owned files. Do not edit `NvidiaGroup.cs`, `StorageGroup.cs`, `AmdGpuGroup.cs` (Latin-1 — do not re-encode), `IntelGpuGroup.cs`, any existing test, project, or gate file.
- Preserve exact event order, lock scope, snapshot timing, exception types/messages, and deferred-enabled-change behavior.
- Add no public API, dependency, project edit, thread, timer, or behavior change. `git mv` is not applicable; this is an extraction, not a file move.
- Report any contract defect in `IGroup`/`Computer` in your result payload instead of working around it.
- Return proposed tracker-row text; do not edit `live-tracker.md`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HardwareGroupRegistryTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

Expected: new facts green; Library `68 + N` with no failures; Application `179/178/1`; Contracts `73/73`; aggregate grows by exactly `N`; both targets `0W/0E`.

## Do NOT

- Do not normalize per-group `Hardware` semantics (snapshot versus live list), reorder `AddGroups`, or touch NVIDIA/storage internals — those belong to AX/AY.
- Do not edit campaign truth surfaces, run `merge`, deploy, promote, push, or clean another checkout.
- Do not claim attended acceptance or touch the live runtime.

## Post-completion

Commit with `refactor(hardware): extract Computer group registry`. Return the commit SHA, new-fact count `N`, focused/Library/Application/Contracts/aggregate results, both build results, exact file list, explicit order/lock/failure-aggregation proof, any concern, and one concise proposed tracker row.

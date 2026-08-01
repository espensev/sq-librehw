# Agent Task — Hardware Lifetime Characterization

**Plan:** `plan-007`
**Baseline:** `9762f4d69882ff9d3fb7c98fef0c6d1c3e981232`
**Depends on:** none
**Exclusive output:** `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupLifetimeCharacterizationTests.cs`

## Goal

Add four deterministic, test-only facts that pin current `Computer`, `NvidiaGroup`, and `StorageGroup` ownership behavior before lifecycle seams are extracted. Do not change production or project files.

## Read first

- `AGENTS.md`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/ComputerOpenLifetimeTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/NvidiaGroupSnapshotTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/StorageGroupLifetimeTests.cs`
- `LibreHardwareMonitorLib/Hardware/Computer.cs`
- `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGroup.cs`
- `LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs`

## Required facts

Create one new class, `HardwareGroupLifetimeCharacterizationTests`, containing exactly these four `[Fact]` tests:

1. `Computer_OpenClose_DynamicGroupsForwardOnlyWhileRegisteredAndDrainInReverseOrder`
   - Inject two `IGroup` + `IHardwareChanged` fakes through `Computer.OpenDependencies.AddGroups`.
   - Prove initial publication follows group registration order and live add/remove events forward once while registered.
   - Make the second group raise events during `Close()` and then throw; prove handlers were detached first, both groups still close in reverse order, process-global owners close, hardware becomes empty, and the exact first close failure is rethrown.
2. `Computer_Open_WhenRegisteredGroupSnapshotThrows_RollsBackSubscriptionAndCanRetry`
   - First open uses a dynamic group whose `Hardware` getter throws after handler attachment.
   - Prove both handlers detach once, the group and owners close, no post-rollback event forwards, and hardware is empty.
   - Retry on the same `Computer` with a healthy group and prove balanced one-time add/remove publication and cleanup.
3. `NvidiaGroup_Constructor_WhenSecondHardwareFactoryThrows_ClosesFirstHardwareAndReleasesLease`
   - Inject two NVAPI handles, a successful process lease, no display handles, and a factory that creates the first GPU then throws on the second.
   - Disable the monitor and prove timeline `acquire -> create first -> fail second -> close first -> release`, exact exception identity, and one close/release.
4. `StorageGroup_Constructor_WhenSecondDeviceFactoryThrows_UnsubscribesAndClosesFirstDevice`
   - Inject a tracked publisher, two synthetic disks, and a factory that creates the first device then throws on the second.
   - Prove exact exception identity, one subscribe/unsubscribe, zero active handlers, and one close of the partial device.

Use test-local fakes only. Follow the existing `OpenProbe`, NVAPI handle, storage publisher, and tracked-device patterns. Keep assertions about observed order and exception identity explicit.

## Boundaries

- Edit only the exclusive output file.
- Do not construct live hardware, touch the registry, Task Scheduler, ports, deployment paths, or the non-Git operations tree.
- Do not modify `live-tracker.md`; return a proposed tracker row in the completion summary for Agent AF.
- Do not add a production seam or project reference.

## Verification

```powershell
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HardwareGroupLifetimeCharacterizationTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
git diff --check
git status --short
```

The focused run must discover and pass 4 tests. The full Library suite must grow from 64 to 68 passing tests.

## Handoff

Commit the owned file with `test(lifecycle): characterize hardware group ownership`. Return the commit SHA, exact test counts, files changed, any issue, and one concise tracker-row update. Do not merge or edit other lanes.

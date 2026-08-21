# Agent Task — Separate NVIDIA Discovery, Monitor, and Close Seams

**Plan:** `plan-013`

**Baseline:** integrated Agent AW

**Depends on:** Agent AW

**Exclusive output:**

- `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGroup.cs`
- `LibreHardwareMonitorLib/Hardware/Gpu/Nvidia/NvidiaGroupLifecycle.cs` (new)
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/NvidiaGroupLifecycleCharacterizationTests.cs` (new)

## Goal

Separate NVIDIA discovery (GPU enumeration, display handles, NVML lease acquisition), background-monitor update (handle-diff refresh, commit, publish, error queue), and close (cancel/join, lease release, device close) into internal collaborators while `NvidiaGroup` remains the unchanged `IGroup`/`IHardwareChanged` facade. No hardware, timing, or NVML lifetime behavior changes.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-013-hardware-lifecycle-seams.md`
3. integrated Agent AW source (registry boundary conventions)
4. `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGroup.cs` (all 689 lines)
5. `LibreHardwareMonitorLib/Interop/NvidiaML.cs` (`LeaseOwner`, process-global lease)
6. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/NvidiaGroupSnapshotTests.cs` (6 established facts and their injection fakes — do not modify this file)
7. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupLifetimeCharacterizationTests.cs` (the NVIDIA partial-factory fact)

Before editing, run the full Library suite plus Application and Contracts and both Release builds. Stop if the integrated AW baseline is not green.

## Task

### Part 1 — Pin the quirks first

Write `NvidiaGroupLifecycleCharacterizationTests.cs` **before** moving code, against the current `NvidiaGroup` internal-constructor injection seams, covering at minimum:

1. driver-restart/unavailable transition drops the NVML lease **before** change callbacks and device close (`CommitUnavailableHardware` ordering);
2. late lease release: a close requested while a refresh is in flight releases NVML when the refresh completes, not before, and never from the monitor task;
3. the monitor error queue is bounded at 8 retained errors with accurate count/last-error observability;
4. handle-diff commit ordering: additions are created outside the lock, committed, and published as one immutable snapshot before removals are closed and notified;
5. the monitor join is bounded (never waits unboundedly) and is skipped when close runs on the monitor task itself.

Run them green against the **unmodified** `NvidiaGroup` first; that green run is your characterization baseline.

### Part 2 — Extract the collaborators

Move discovery, monitor-loop, and close mechanics into internal collaborators in `NvidiaGroupLifecycle.cs` (one file; multiple internal types are fine). `NvidiaGroup` keeps the public constructors, the `IGroup`/`IHardwareChanged` surface, the settings fields, and delegates the mechanics. Preserve exactly:

- the 1-second default monitor interval and its `> 0` guard;
- `MaxRetainedMonitorErrors = 8` and the observability surface (`LastMonitorError`, `MonitorErrorCount`, `MonitorErrors`);
- lease ensure/release reentrancy semantics, including `_releaseNvidiaMlWhenRefreshCompletes`;
- partial-factory cleanup (close already-created GPUs) and snapshot publication under `_sync`;
- idempotent close, ≤ 1 s monitor join, NVML-release-before-device-close ordering;
- Unix report-only early return.

Your Part-1 facts and the six existing `NvidiaGroupSnapshotTests` facts must pass **unmodified** against the extracted shape.

## Exit Criteria

- Exactly the three owned files differ from the AW baseline (two new, one modified).
- Every new type is `internal`; the Lib public surface stays byte-identical; `NvidiaGroupSnapshotTests.cs` and `HardwareGroupLifetimeCharacterizationTests.cs` are unmodified.
- New facts pass exactly; Library total grows by exactly your new facts over the AW result with zero regressions; Application remains `178+1/179`; Contracts remain `73/73`; aggregate grows by exactly your new facts with the same single skip.
- Both WinForms x64 Release targets build `0W/0E`.

## Constraints

- Modify only the owned files. Do not edit `Computer.cs`, `HardwareGroupRegistry.cs`, `StorageGroup.cs`, `NvidiaML.cs`, `NvApi*`, existing tests, projects, packages, or gates.
- No new public API, thread, timer, synchronization primitive, or behavior change; the monitor task remains the only background thread and its cadence is unchanged.
- Keep the injection-seam constructor signatures compatible with the existing fakes so no existing test needs edits.
- Report contract defects in your result payload instead of editing another agent's files.
- Return proposed tracker-row text; do not edit `live-tracker.md`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~NvidiaGroupLifecycleCharacterizationTests|FullyQualifiedName~NvidiaGroupSnapshotTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

## Do NOT

- Do not normalize NVIDIA behavior toward AMD/Intel shapes, change NVML lease ownership, alter monitor cadence, or add hardware enumeration paths.
- Do not edit campaign truth surfaces, run `merge`, deploy, promote, push, or clean another checkout.
- Do not touch the live runtime or claim attended acceptance.

## Post-completion

Commit with `refactor(hardware): separate NVIDIA lifecycle seams`. Return the commit SHA, pre/post extraction fact results with counts, Library/Application/Contracts/aggregate results, both build results, exact file list, explicit lease-ordering/error-queue/commit-order proof, any concern, and one concise proposed tracker row.

# Agent Task — Separate Storage Discovery, Change Application, and Close Seams

**Plan:** `plan-013`

**Baseline:** integrated Agent AW

**Depends on:** Agent AW

**Exclusive output:**

- `LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs`
- `LibreHardwareMonitorLib/Hardware/Storage/StorageGroupLifecycle.cs` (new)
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/StorageGroupLifecycleCharacterizationTests.cs` (new)

## Goal

Separate storage discovery (subscribe-before-enumerate with buffered synchronous changes), change application (diff/coalesce/close/notify), and close/unsubscribe-retry into internal collaborators while `StorageGroup` remains the unchanged `IGroup`/`IHardwareChanged` facade. No device, DiskInfoToolkit, or timing behavior changes.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-013-hardware-lifecycle-seams.md`
3. integrated Agent AW source (registry boundary conventions)
4. `LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs` (all 442 lines)
5. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/StorageGroupLifetimeTests.cs` (9 established facts and `StorageChangePublisher` — do not modify this file)
6. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/HardwareGroupLifetimeCharacterizationTests.cs` (the storage constructor-rollback fact)

Before editing, run the full Library suite plus Application and Contracts and both Release builds. Stop if the integrated AW baseline is not green.

## Task

### Part 1 — Pin the quirks first

Write `StorageGroupLifecycleCharacterizationTests.cs` **before** moving code, against the current `StorageGroup` internal-constructor injection seams, covering at minimum:

1. subscription happens **before** enumeration so no device-change callback falls into the blind window;
2. synchronous callbacks during initialization are buffered, add/remove-coalesced, capped at 256 pending changes, and replayed in order after enumeration;
3. a redundant late addition (same `device.Storage` reference) is closed rather than added, and notifications fire only after commit;
4. a failed unsubscribe keeps ownership of the devices, and a later `Close` retries the unsubscribe only (devices are not closed twice);
5. removals close devices after commit and notify afterward.

Run them green against the **unmodified** `StorageGroup` first; that green run is your characterization baseline.

### Part 2 — Extract the collaborators

Move discovery, change application, and close/unsubscribe-retry mechanics into internal collaborators in `StorageGroupLifecycle.cs` (one file; multiple internal types are fine). `StorageGroup` keeps the constructors, the `IGroup`/`IHardwareChanged` surface, `GetReport() => null`, and delegates the mechanics. Preserve exactly:

- `MaxPendingInitializationChanges = 256` and the coalescing semantics;
- the `_initializing` buffer/replay window;
- dedupe by `device.Storage == storage` reference equality;
- notify-after-commit and close-after-commit ordering;
- idempotent close and unsubscribe-retry ownership.

Your Part-1 facts and the nine existing `StorageGroupLifetimeTests` facts must pass **unmodified** against the extracted shape.

## Exit Criteria

- Exactly the three owned files differ from the AW baseline (two new, one modified).
- Every new type is `internal`; the Lib public surface stays byte-identical; `StorageGroupLifetimeTests.cs` and `HardwareGroupLifetimeCharacterizationTests.cs` are unmodified.
- New facts pass exactly; Library total grows by exactly your new facts over the AW result with zero regressions; Application remains `178+1/179`; Contracts remain `73/73`; aggregate grows by exactly your new facts with the same single skip.
- Both WinForms x64 Release targets build `0W/0E`.

## Constraints

- Modify only the owned files. Do not edit `Computer.cs`, `HardwareGroupRegistry.cs`, `NvidiaGroup.cs`, `StorageDIT`/DiskInfoToolkit interop, device classes, existing tests, projects, packages, or gates.
- No new public API, thread, timer, subscription source, or behavior change.
- Keep the injection-seam constructor signatures compatible with the existing fakes so no existing test needs edits.
- Report contract defects in your result payload instead of editing another agent's files.
- Return proposed tracker-row text; do not edit `live-tracker.md`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~StorageGroupLifecycleCharacterizationTests|FullyQualifiedName~StorageGroupLifetimeTests" --logger "console;verbosity=minimal" --tl:off
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

- Do not change provider semantics, device identity, subscription timing, or the null report contract; do not add hardware enumeration paths.
- Do not edit campaign truth surfaces, run `merge`, deploy, promote, push, or clean another checkout.
- Do not touch the live runtime or claim attended acceptance.

## Post-completion

Commit with `refactor(hardware): separate storage lifecycle seams`. Return the commit SHA, pre/post extraction fact results with counts, Library/Application/Contracts/aggregate results, both build results, exact file list, explicit subscribe-window/coalesce-cap/retry-ownership proof, any concern, and one concise proposed tracker row.

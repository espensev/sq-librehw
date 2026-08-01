# Agent Task — Wire MainForm Lifecycle Coordinator

**Plan:** `plan-010`
**Depends on:** Agent AM
**Exclusive outputs:**

- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs`

## Goal

Wire Agent AM's tested coordinator into `MainForm` and remove MainForm's
duplicate lifecycle state. Preserve every hardware, PawnIO, server, timer,
BackgroundWorker, logging, redraw, UI-thread, settings-save, and shutdown
ordering contract. In `UiShutdownCoordinator`, correct one stale comment only.

## Read first

- `AGENTS.md`
- `docs/campaign-plan-010-application-lifecycle-and-polling.md`
- Agent AM's committed coordinator and eight tests
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/HardwareOperationCoordinator.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs`
- all three coordinator test families

Before editing, run the integrated 8/8 and 27/27 slices. Stop if either fails.

## Wiring contract

- Replace MainForm's lifecycle cancellation source, lifecycle gate,
  initialization completion/task-state fields, and
  `HardwareOperationCoordinator` field with one
  `ApplicationLifecycleCoordinator` field. MainForm remains the composition
  root and sole owner of the actual `Computer`, process lifetime, UI objects,
  server, PawnIO prompts/installation, and persisted settings.
- Construct the coordinator immediately after `Computer` using delegates for
  `Task.Run(_computer.Open)`, `SetHardwareOption`, `_computer.Reset`, and the
  existing final hardware/UI close body. Preserve all existing coordinator
  failure/state event handling and debug messages.
- Keep PawnIO detection, prompt, optional installation, and pre-open shutdown
  checks in `InitializeHardwareAsync`. Delegate only the hardware-open
  lifecycle segment. Enable the existing timer only after successful open;
  retain cancellation handling, exact failure dialog, and final UI refresh.
- `ApplyHardwareOption` delegates to the coordinator. Constructor-time
  `UserOption` notifications still apply synchronously before initialization;
  later option/reset behavior remains the unchanged ordered coordinator policy.
- Keep the existing WinForms timer and `BackgroundWorker`, with update then
  logging in `DoWork`. `Timer_Tick` uses lifecycle poll admission before
  `RunWorkerAsync`; if launch throws, immediately release that admission.
  `RunWorkerCompleted` releases poll admission before error logging or any
  shutdown/redraw decision. Busy ticks remain dropped.
- Poll admission is independent of option/reset serialization. Do not acquire
  the lifecycle gate in polling and do not block option/reset merely because a
  poll is active.
- At shutdown, retain the existing unsubscribe/hide/timer-stop sequence, call
  lifecycle `BeginStop` before `Server.QuitAsync`, then await lifecycle
  `StopAsync`. Its close delegate performs the existing `HardwareNode`, root,
  gadget, tray, and `Computer.Close` body in the same order. It must run once
  after initialization, option/reset, and active-poll drain.
- After stop completes, retain configuration save and UI-resource disposal in
  the existing order. Unsubscribe lifecycle events and dispose the coordinator
  only after its stop completes. Remove direct gate/token/operation disposal and
  duplicate `Computer.Close` calls.
- `IsShutdownPending` and hardware-menu/tray state read coordinator state while
  preserving `_closing` and `UiShutdownCoordinator` semantics. Late worker and
  lifecycle completions continue to drop UI work after shutdown.
- In `UiShutdownCoordinator.cs`, change only the stale comment that says
  MainForm supplies synchronous `Control.Invoke`; MainForm actually supplies a
  queued `BeginInvoke` handoff. Do not change executable code there.
- Do not edit the Designer, `Computer`, `HardwareOperationCoordinator`, tests,
  project/package graph, settings, HTTP, snapshot, golden, web, docs, tracker,
  operations, or runtime.

## Exit Criteria

- Exactly the two exclusive outputs change; the UiShutdown diff is comment-only.
- MainForm has one lifecycle owner and no duplicate token, gate,
  initialization-barrier, operation-coordinator, or polling-admission fields.
- Initialization, pre-init options, ordered runtime option/reset work,
  timer/worker single-flight polling, update-then-log, redraw-after-success,
  late-completion drop, and exactly-once drained close follow the wiring contract.
- All three coordinator families pass 27/27; library lifetime families pass
  18/18; Application is exactly 160 discovered, 159 passed, one established
  opt-in skip.
- Both x64 Release WinForms targets build with zero warnings/errors.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ApplicationLifecycleCoordinatorTests|FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ComputerOpenLifetimeTests|FullyQualifiedName~HardwareGroupLifetimeCharacterizationTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

## Do not

- Do not edit outside the two exclusive outputs or alter AM's API/tests to make
  wiring easier; report a genuine contract defect to the coordinator.
- Do not replace the WinForms timer or BackgroundWorker, add polling workers,
  move PawnIO/settings/server policy, or redesign shutdown.
- Do not edit `live-tracker.md`, merge, deploy, promote, mutate the operational
  tree, or clean another checkout.

## Handoff

Commit with `refactor(lifecycle): wire mainform coordinator`. Return the commit
SHA, exact focused/lifetime/Application/build results, exact file list, explicit
poll concurrency and shutdown-order proof, any concern, and one concise
proposed tracker row.

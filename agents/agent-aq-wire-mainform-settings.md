# Agent Task — Wire MainForm Settings Coordination

**Plan:** `plan-011`

**Depends on:** Agent AP

**Exclusive output:**

- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`

## Goal

Wire Agent AP’s tested settings persistence coordinator into `MainForm` and remove the in-method persistence sequencing while preserving every UI-thread projection, key/value/default, autosave timer, final shutdown save, message, debug, lifecycle, and disposal behavior.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-011-settings-projection-and-persistence.md`
3. Agent AP’s committed coordinator and seven tests
4. `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`
5. `LibreHardwareMonitor.Windows.Forms/UI/ApplicationLifecycleCoordinator.cs`
6. `LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs`
7. `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs`
8. `LibreHardwareMonitor.Windows.Forms/Utilities/RuntimePaths.cs`
9. all focused settings and lifecycle test families named in the plan

Before editing, run AP’s `7/7` slice and the focused settings family. Stop if either fails.

## Task

### Part 1 — Compose the coordinator

Add one readonly `SettingsPersistenceCoordinator` field. Construct it only after `_settings` and `_runtimePaths` are available, passing:

- the existing `PersistentSettings` instance;
- `_runtimePaths.SettingsFilePath`;
- a synchronous MainForm-owned projection delegate;
- an autosave-failure observer that preserves the exact `Debug.WriteLine("Autosave of settings failed: " + ex.Message)` policy.

MainForm remains the composition root and sole owner of all concrete UI/server reads, the settings keys and values, the autosave timer, session-ended UI dispatch, final shutdown timing, and modal error messages.

### Part 2 — Preserve the exact projection

Extract only the existing projection statements from `SaveConfiguration` into a small MainForm method or local delegate called by the coordinator. Preserve their current order and semantics exactly:

- `_plotPanel.SetCurrentSettings()`;
- each tree column width, including base Value/Min/Max widths;
- `uiTextScale` and `plotTextScale`;
- `listenerIp`, `listenerPort`, `authenticationEnabled`, `authenticationUserName`, and `authenticationPassword`.

Keep the existing null guard for `_plotPanel`/`_settings`. Do not move control reads into the coordinator, rename keys, normalize values, change defaults, add settings, or move any projection to a worker thread.

### Part 3 — Replace persistence sequencing without changing policy

`SaveConfiguration(bool autoSave = false)` should delegate the project/skip/validate/save sequence to the coordinator.

- Autosave still returns quietly when unchanged.
- Autosave I/O/access failures still produce no modal dialog and are debug-reported once for retry.
- Catch only AP's `SettingsPersistenceException` for final/manual persistence failures. Inspect its exact `InnerException`: `UnauthorizedAccessException` and `IOException` still produce the exact existing message text, caption, buttons, and icon using the same settings path. Projection-origin I/O/access exceptions remain unwrapped and must retain their existing propagation behavior.
- Other exceptions keep their existing propagation behavior.
- `AutoSaveTimer_Tick` still refuses work after shutdown begins.
- `SystemEvents.SessionEnded` still marshals the complete shutdown onto the UI thread and waits as already characterized.
- `CloseApplicationCoreAsync` still stops the autosave timer, begins lifecycle stop, quits the server, drains lifecycle work, performs the final save, then disposes timers/coordinators/UI resources in the same order.

Do not change `PersistentSettings`, `RuntimePaths`, `UiShutdownCoordinator`, `ApplicationLifecycleCoordinator`, `HttpServer`, tests, or projects. If AP’s API cannot support this exact wiring, report the contract defect instead of editing AP-owned files.

## Exit Criteria

- Exactly `MainForm.cs` changes.
- MainForm composes one settings persistence coordinator and retains exact UI-thread projection keys, values, ordering, timers, messages, and shutdown-save placement.
- The focused settings and lifecycle families, full Application and Contracts suites, and both x64 Release WinForms builds pass at the planned counts.
- `PersistentSettings`, `RuntimePaths`, existing tests, projects/packages, gates, docs, operations, candidates, and live runtime remain unchanged.

## Constraints

- Modify exactly `MainForm.cs`.
- Preserve every existing setting key, value, default, UI thread, timer interval, exception category, message, and shutdown order.
- Add no retries, background saves, async persistence, locks, settings snapshots, public API, dependency, or schema change.
- Return proposed tracker-row text; do not edit `live-tracker.md`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~SettingsPersistenceCoordinatorTests|FullyQualifiedName~SettingsPersistenceTests|FullyQualifiedName~RuntimePathsTests|FullyQualifiedName~StartupManagerTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ApplicationLifecycleCoordinatorTests|FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

Expected: settings coordinator `7/7`; Application `167/166/1`; Contracts `73/73`; both WinForms targets `0W/0E`.

## Do NOT

- Do not edit outside `MainForm.cs` or alter AP’s implementation/tests to make wiring easier.
- Do not redesign settings, lifecycle, shutdown, timers, dialogs, HTTP, or runtime paths.
- Do not edit campaign truth, merge, deploy, promote, push, or clean another checkout.

## Post-completion

Commit with `refactor(settings): wire mainform persistence`. Return the commit SHA, focused/full/build results, exact file list, explicit projection/order/message proof, any concern, and one concise proposed tracker row.

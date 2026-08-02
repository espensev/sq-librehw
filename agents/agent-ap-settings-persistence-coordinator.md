# Agent Task — Settings Persistence Coordinator

**Plan:** `plan-011`

**Product baseline:** `af843706f0c1679f4952b7df11bf17fc5b306cef`

**Depends on:** none

**Exclusive outputs:**

- `LibreHardwareMonitor.Windows.Forms/UI/SettingsPersistenceCoordinator.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsPersistenceCoordinatorTests.cs`

## Goal

Add one internal, delegate-driven coordinator for settings projection and persistence. It must own the synchronous sequence “project current state → apply autosave dirty skip → validate primary/backup/staging paths → call the unchanged `PersistentSettings.Save`,” while preserving retry and final-save error behavior. Add exactly seven deterministic facts. Do not wire `MainForm`; Agent AQ owns that step.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-011-settings-projection-and-persistence.md`
3. `docs/campaign-playbook.md`
4. `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`, especially `AutoSaveTimer_Tick`, `SaveConfiguration`, `SystemEvents_SessionEnded`, and `CloseApplicationCoreAsync`
5. `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs`
6. `LibreHardwareMonitor.Windows.Forms/Utilities/RuntimePaths.cs`
7. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsProjectionTests.cs`
8. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsPersistenceTests.cs`
9. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/RuntimePathsTests.cs`

Before editing, unset both live-config variables and run the focused existing settings tests. Stop if they fail.

## Task

### Part 1 — Add the internal coordinator

Create `SettingsPersistenceCoordinator` in namespace `LibreHardwareMonitor.Windows.Forms.UI`. Keep it `internal sealed`, synchronous, and compatible with `net472`. It may depend on `PersistentSettings`, the settings-file path, a synchronous `Action` that projects current application state into the store, and a delegate that records a suppressed autosave failure. It must not depend on `Form`, controls, `MainForm`, `HttpServer`, hardware types, snapshot DTOs, tasks, timers, or concrete UI objects.

Expose the smallest API needed by `MainForm`, preferably a single `Save(bool autoSave)` operation plus a small internal result enum if callers need to distinguish saved, unchanged, and suppressed-failure outcomes. Names may vary only for C# clarity.

The operation must:

1. synchronously invoke the projection delegate;
2. for autosave only, return without validation or file I/O when `PersistentSettings.Modified` is false after projection;
3. validate the primary path and its `.backup` and `.new` siblings using the existing `RuntimePaths.EnsureSafeMutableFile` contract;
4. call the unchanged `PersistentSettings.Save`;
5. suppress only `IOException` and `UnauthorizedAccessException` from the validation/save stage during autosave, report the exact exception through the injected observer, and leave the store dirty for retry;
6. wrap those same validation/save-stage failures during a final/manual save in a small internal `SettingsPersistenceException` that preserves the exact original exception as `InnerException`, so `MainForm` can preserve its existing messages without catching projection-origin I/O;
7. allow all other exceptions, including every projection defect and safety violations outside the two established I/O/access categories, to propagate unchanged.

Do not add independent storage, settings snapshots, locks, retries, background work, XML handling, or new path semantics. `PersistentSettings` remains the sole owner of ordered writes, atomic replacement, backup recovery, transient-load blocking, modified-state transitions, duplicate normalization, and stale-history compaction.

### Part 2 — Add exactly seven deterministic facts

Create `SettingsPersistenceCoordinatorTests.cs` with exactly these behavioral facts (method names may be adjusted without changing meaning):

1. `Autosave_ProjectsBeforeCleanCheckAndSkipsUnchangedStore`
2. `Autosave_ProjectedChangePersistsAndClearsDirtyState`
3. `FinalSave_WritesEvenWhenStoreIsClean`
4. `Autosave_IOExceptionIsReportedSuppressedAndRemainsDirty`
5. `Autosave_UnauthorizedAccessIsReportedSuppressedAndRemainsDirty`
6. `FinalSave_IoFailurePropagatesAndRemainsDirty` (the coordinator wrapper must preserve each exact I/O/access failure as `InnerException`)
7. `FinalSave_WaitsBehindInFlightAutosaveAndPersistsLatestProjection`

Use `PersistentSettings`’ internal injected writer, unique temporary directories where path guards require physical parents, `TaskCompletionSource` barriers with `RunContinuationsAsynchronously`, bounded waits, and event/order records. Do not sleep, construct a Form, read live configuration, access hardware/registry/Task Scheduler/listeners, or write outside the test temp root. Release every barrier in `finally` so failed assertions cannot hang the suite.

The concurrency fact must prove the established `PersistentSettings` I/O ordering is retained through the wrapper: an in-flight autosave completes before the final save snapshots/writes the later projected value, and a reload sees the later value.

## Exit Criteria

- Exactly the two exclusive new files change and the test file contains exactly seven `[Fact]` tests.
- The coordinator follows the projection, skip, validation, save, autosave-suppression, and final-save propagation contract without duplicating `PersistentSettings` ownership.
- Coordinator facts pass `7/7`; the focused settings family and full Application suite pass at the planned counts.
- Both x64 Release WinForms targets build with zero warnings and errors.
- No existing source, test, project, package, gate, documentation, candidate, operations, or live-runtime file changes.

## Constraints

- Modify exactly the two exclusive output files.
- Keep `PersistentSettings.cs`, `RuntimePaths.cs`, `MainForm.cs`, all existing tests, project/package/solution files, gates, docs, tracker, operations, candidates, and live runtime unchanged.
- Add no public API, package, project, interface, async-disposal surface, storage format, setting key, retry loop, or background worker.
- Return proposed tracker-row text in the result; do not edit `live-tracker.md`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsPersistenceCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~SettingsPersistenceCoordinatorTests|FullyQualifiedName~SettingsPersistenceTests|FullyQualifiedName~RuntimePathsTests|FullyQualifiedName~StartupManagerTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

Expected after this agent: coordinator tests `7/7`; Application `167` discovered / `166` passed / the one established live-config opt-in skip; both WinForms builds have zero warnings and errors.

## Do NOT

- Do not wire `MainForm` or edit existing settings/path tests to fit the implementation.
- Do not move atomic write, backup, compaction, or load policy out of `PersistentSettings`.
- Do not create a candidate, deploy, promote, mutate the operational tree, merge, push, or clean another checkout.

## Post-completion

Commit with `refactor(settings): add persistence coordinator`. Return the commit SHA, exact 7/7 and full/focused counts, both build results, exact file list, concurrency/no-hang evidence, any concern, and one concise proposed tracker row.

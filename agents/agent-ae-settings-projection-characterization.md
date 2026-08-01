# Agent Task — Settings Projection Characterization

**Plan:** `plan-007`
**Baseline:** `9762f4d69882ff9d3fb7c98fef0c6d1c3e981232`
**Depends on:** none
**Exclusive outputs:**

- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsProjectionTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SettingsPersistenceTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/StartupManagerTests.cs`

## Goal

Add eight deterministic facts that pin existing option/radio projection, typed settings fallback, duplicate-key normalization, and managed startup fail-closed behavior before the settings seam is extracted.

## Read first

- `AGENTS.md`
- both existing owned test files
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/RuntimePathsTests.cs` as a read-only boundary baseline
- `LibreHardwareMonitor.Windows.Forms/UI/UserOption.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/UserRadioGroup.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/StartupManager.cs`
- `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs`

## Required facts

Create `SettingsProjectionTests` with these six tests:

1. `UserOption_ProjectsStoredValueWithoutDirtyingSettings` — construction reads and projects the stored value without setting `Modified`.
2. `UserOption_ChangedInitializesOnlyNewSubscriberAndNotifiesAfterProjection` — adding a handler invokes only that new handler; later change callbacks observe both persistence and menu state already updated.
3. `UserOption_NullNameProjectsExternalStateWithoutPersistence` — the auto-start-style null key projects and raises changes without creating a settings entry or dirtying the store.
4. `UserRadioGroup_ClampsStoredSelectionWithoutNormalizingPersistence` — a stored index of 99 over three items projects index 2 and one checked item while retaining stored 99 and a clean store.
5. `UserRadioGroup_ChangedInitializesOnlyNewSubscriberAndNotifiesAfterProjection` — click/change persists and checks the new selection before callbacks; subscription initialization is per-handler.
6. `PersistentSettings_InvalidTypedTextUsesCurrentPerTypeFallbacks` — invalid int/float/double/color use the caller fallback, while bool recognizes only exact `true`, so malformed or case-mismatched text projects false even if fallback is true.

Extend `SettingsPersistenceTests` with:

7. `Load_DuplicateKeysUsesLastValueAndArmsNormalizationSave` — duplicate XML keys are last-wins, load is dirty, save writes one normalized key, and reload is clean.

Extend `StartupManagerTests` with:

8. `ManagedInstall_EnablingPortableStartupThrowsAndLeavesStateDisabled` — use only the injected managed task path; prove enabling a portable build throws and the state stays disabled without entering scheduler/registry branches.

Use in-memory injected settings/writer and `ToolStripMenuItem` for projection tests. Existing persistence helpers must use their unique temp root.

## Boundaries

- Edit only the three exclusive files.
- Before tests, ensure `LHM_LIVE_CONFIG_PATH` and `LHM_LIVE_CONFIG_EXPECTED_LENGTH` are unset for the process.
- Do not construct `MainForm`, call `RuntimePaths.Current`, use the default `StartupManager`, or touch real registry/Task Scheduler/configuration.
- Do not add a production helper; that would begin Plan-011.
- Do not modify `live-tracker.md`; return tracker evidence in the completion summary for Agent AF.

## Verification

```powershell
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~Load_DuplicateKeysUsesLastValueAndArmsNormalizationSave|FullyQualifiedName~ManagedInstall_EnablingPortableStartupThrowsAndLeavesStateDisabled" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~SettingsPersistenceTests|FullyQualifiedName~RuntimePathsTests|FullyQualifiedName~StartupManagerTests" --logger "console;verbosity=minimal" --tl:off
git diff --check
git status --short
```

The first focused run must pass exactly 8 new facts. The regression filter must keep the existing live-config fact skipped unless its opt-in variable is explicitly supplied, which this lane must not do.

## Handoff

Commit the three owned files with `test(settings): characterize projection boundaries`. Return the commit SHA, exact counts, files changed, any issue, and one concise tracker-row update. Do not merge or edit other lanes.

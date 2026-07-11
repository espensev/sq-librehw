# Review - Settings Persistence Fix

**Date:** 2026-07-07
**Surface:** `HEAD~1..HEAD` (`e0758bf fix(settings): bound sensor history and harden save durability`)
**Spec source:** User-reported settings revert/restart issue; `docs/local-ui-customizations.md`
**Standards sources:** `AGENTS.md`; `docs/ai-guide.md`
**Verdict:** FAIL

## Findings

### Medium

- [axis: regression] `PersistentSettings.cs:238` - fallback write path can delete the live config after failing to preserve a backup.
  - Mechanism: after `File.Replace` falls back, `File.Copy(fileName, backupFileName, overwrite:true)` is wrapped in an empty `catch` (238-242), yet execution still proceeds to `File.Delete(fileName)` + `File.Move(tempFileName, fileName)` (247-253). Failed backup copy + any later crash/move failure -> no valid live config and no backup.
  - Impact: durability gap in the loss/revert fix; primary NTFS `File.Replace` path is fine, but the copy-based fallback does not guarantee "last good config survives" on non-NTFS/network cases.
  - Fix: make backup preservation mandatory - throw before deleting the live file if the backup copy fails; add a regression test that forces the fallback with a failed backup and verifies the original config survives.

## Verification

- `git diff --check HEAD~1..HEAD` - pass.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --no-restore` - pass; 63/63 tests passed, with the existing xUnit2020 analyzer warning in `DataJsonGoldenTests.cs`.
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore` - pass; 0 warnings, 0 errors.

## Coverage Notes

- Deep-reviewed: `PersistentSettings.cs`, `Sensor.cs`, `MainForm.cs`, `SettingsPersistenceTests.cs`.
- Standards/traceability: `LibreHardwareMonitorLib.csproj`, `.gitignore`, `docs/local-ui-customizations.md`, `AGENTS.md`, `docs/ai-guide.md`.
- Excluded from verdict: unrelated dirty webserver changes + separate doc-tree churn in the working tree.
- `net10.0-windows` Release build not run - review scoped to the committed settings diff; the test run did compile the `net10.0-windows` Debug test surface.

## Open Questions

- Preserve sensor history across a crash after autosave? First autosave after startup can save without `/values` (live sensors remove loaded histories, only rewrite refreshed ones on `_computer.Close()`). Does not affect user settings; open whether history durability matters.

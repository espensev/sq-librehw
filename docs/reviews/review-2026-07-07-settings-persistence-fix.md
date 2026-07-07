# Review - Settings Persistence Fix

**Date:** 2026-07-07
**Surface:** `HEAD~1..HEAD` (`e0758bf fix(settings): bound sensor history and harden save durability`)
**Spec source:** User-reported settings revert/restart issue; `docs/local-ui-customizations.md`
**Standards sources:** `AGENTS.md`; `docs/ai-guide.md`
**Verdict:** FAIL

## Findings

### Medium

- [axis: regression] `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs:238` - The fallback write path can delete the live config after failing to preserve a backup.
  Evidence: after `File.Replace` falls back, `File.Copy(fileName, backupFileName, overwrite: true)` is wrapped in an empty `catch` at lines 238-242, but execution still proceeds to `File.Delete(fileName)` and `File.Move(tempFileName, fileName)` at lines 247-253. If the backup copy failed, any crash or move failure after deleting the live file can leave no valid live config and no backup.
  Impact: this is a direct durability gap in the fix for settings loss/revert. The primary NTFS `File.Replace` path is good, but the documented copy-based fallback does not actually guarantee "last good config survives" on non-NTFS/network/fallback cases.
  Recommendation: make backup preservation mandatory when replacing an existing file. If copying the existing file to backup fails, throw before deleting the live file. Add a regression test that forces the fallback path and verifies failed backup creation leaves the original config intact.

## Verification

- `git diff --check HEAD~1..HEAD` - pass.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --no-restore` - pass; 63/63 tests passed, with the existing xUnit2020 analyzer warning in `DataJsonGoldenTests.cs`.
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore` - pass; 0 warnings, 0 errors.

## Coverage Notes

- Files reviewed deeply: `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs`, `LibreHardwareMonitorLib/Hardware/Sensor.cs`, `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`, `LibreHardwareMonitor.Tests/SettingsPersistenceTests.cs`.
- Files reviewed for standards/traceability: `LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj`, `.gitignore`, `docs/local-ui-customizations.md`, `AGENTS.md`, `docs/ai-guide.md`.
- Current working tree has unrelated dirty webserver changes and separate doc-tree churn; they were not included in this settings-fix verdict.
- `net10.0-windows` Release build was not run for this review because the working tree is dirty and this review stayed scoped to the committed settings diff; the test run did compile the `net10.0-windows` Debug test surface.

## Open Questions

- Should sensor history be preserved across a crash after autosave? The first autosave after startup can save without `/values` entries because live sensors remove loaded histories and only write refreshed histories on `_computer.Close()`. That does not affect user settings, but it is worth deciding whether history durability matters.

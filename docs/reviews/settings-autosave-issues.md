# Settings Autosave Issues

**Status:** accepted residual risks.
**Decision:** no more work in this pass.
**Format:** intentionally compact todo list; do not expand into a full review.

## Todo / Issues

- [ ] Prevent stale autosave writes after newer final/logoff saves.
  - Risk: older autosave snapshot can acquire file write lock last.
  - Path: `PersistentSettings.Save`, `MainForm.SaveConfiguration`.
  - Future fix: serialize snapshot + write, or add save generation.

- [ ] Preserve graph history across crash-after-autosave.
  - Risk: autosave can write config without live sensor `/values`.
  - Path: `Sensor.SetSensorValuesToSettings`, autosave timer.
  - Future fix: keep last bounded on-disk histories until clean close refreshes them.

## Already Fixed

- [x] Fallback save no longer deletes live config when backup cannot be preserved.
  - Covered by `Save_KeepsLiveConfig_WhenBackupCannotBePreserved`.

## Verification Recorded

- `dotnet test ... -p:Platform=x64 --no-restore` passed 64/64.
- `dotnet build ... -f net472 -p:Platform=x64 --no-restore` passed 0/0.

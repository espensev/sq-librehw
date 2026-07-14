# Settings Autosave Issues

**Status:** one residual risk remains.
**Decision:** stale-save ordering is addressed by the 2026-07-14 reliability work.
**Format:** intentionally compact todo list; do not expand into a full review.

## Todo / Issues

- [ ] Preserve graph history across crash-after-autosave.
  - Risk: autosave can write config without live sensor `/values`.
  - Path: `Sensor.SetSensorValuesToSettings`, autosave timer.
  - Future fix: keep last bounded on-disk histories until clean close refreshes them.

## Already Fixed

- [x] Snapshot creation and file replacement share one serialization boundary, so an older save cannot overwrite a newer snapshot.
  - Covered by the overlapping-save persistence tests.
- [x] Fallback save no longer deletes live config when backup cannot be preserved.
  - Covered by `Save_KeepsLiveConfig_WhenBackupCannotBePreserved`.

## Verification Recorded

- `dotnet test ... -p:Platform=x64 --no-restore` passed 64/64.
- `dotnet build ... -f net472 -p:Platform=x64 --no-restore` passed 0/0.

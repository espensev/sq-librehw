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

- The x64 .NET suite passed 124, skipped one intentionally opt-in
  live-config-copy test, and failed 0.
- Isolated Release builds for `net10.0-windows` and `net472` both passed with
  0 warnings and 0 errors.
- A deployed clean restart loaded 475 bounded history entries. The first
  five-minute autosave compacted the primary config from 2,889,088 to 22,643
  bytes; a later autosave left both primary and backup at 22,643 bytes. This
  confirms both the cleanup behavior and the still-open continuity risk above.

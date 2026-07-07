# Review - Settings Autosave Rereview

**Date:** 2026-07-07
**Verdict:** PASS WITH NOTES
**Disposition:** accepted for now; no more work in this pass.

## Summary

- Settings persistence is materially better than before.
- Backup-preservation failure is fixed and tested.
- Two non-blocking autosave edge cases remain.
- Track them in [`settings-autosave-issues.md`](settings-autosave-issues.md).

## Remaining Issues

- Stale autosave can write after a newer final/logoff save.
- Autosave can drop graph history before a clean hardware close rewrites `/values`.

## Verification

- `dotnet test ... -p:Platform=x64 --no-restore` passed 64/64.
- `dotnet build ... -f net472 -p:Platform=x64 --no-restore` passed 0/0.

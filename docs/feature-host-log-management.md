# Feature Spec: Host-Neutral Log Management

**Status:** deployed on SND-HOST; task/current-day retention smoke verified
**Updated:** 2026-07-18

## Problem

The retired host workspace contains useful LibreHardwareMonitor log archival
ideas, but its scripts live inside the runtime slated for deletion and mix
machine-specific paths with operational behavior. The live host also has
multiple gigabytes of logs, so unsafe cleanup or an unverified archive could
destroy useful evidence.

## Goal

Keep a source-controlled, host-neutral package that archives completed daily
LibreHardwareMonitor CSV files, prunes only verified archives under an explicit
root, and can install a daily task into a stable runtime directory during a
separate approved deployment.

## Behavior and safety contract

- All source directories, archive root, machine label, runtime directory, task
  name, schedule, and retention are parameters/configuration. Product scripts
  contain no machine-specific deployment path.
- Only files matching `LibreHardwareMonitorLog-YYYY-MM-DD*.csv` and dated before
  the supplied/current local day are candidates. Current-day, locked, missing,
  and unrecognized files are retained and reported.
- Archives live at
  `<archive-root>/<machine>/<yyyy>/<MM-MMM>/<csv-base>.zip`.
- Each ZIP contains exactly one CSV entry. Source length and SHA-256 are checked
  against the ZIP entry before the source is removed.
- A matching pre-existing verified ZIP is an idempotent duplicate. A name/hash
  collision is reported as failure and the source remains intact.
- Publishing uses a temporary ZIP in the destination directory followed by a
  rename. A failed or partial archive never authorizes source deletion.
- Retention removes only old, readable, one-entry ZIPs in the recognized
  machine/year/month layout. Unknown or corrupt files are retained.
- Archive, cleanup, and installer entry points support `-WhatIf`. The installer
  copies runtime scripts/configuration and registers or updates one scheduled
  task only when explicitly run with administrative authority.
- Installation never disables or removes a legacy task automatically. Cutover
  requires target identity verification, a dry run, a successful manual run,
  archive inspection, and explicit retirement of the old owner.

## Non-goals

- No CSV analytics, hardware-specific column processing, or dashboard import.
- No deployment, task mutation, live log cleanup, or deletion on SND-HOST as
  part of this source change.
- No cloud upload, encryption/key management, or cross-machine archive copy.
- No deletion of unrecognized, corrupt, current-day, or locked-source data.

## Compatibility

Scripts target Windows PowerShell 5.1 and PowerShell 7 using built-in .NET ZIP,
SHA-256, and ScheduledTasks facilities. Paths may be on any local drive. The
deployment operator must ensure the task identity can read every configured
source and write the archive/runtime roots.

## Acceptance

- [x] A completed prior-day CSV archives once and is removed only after ZIP
  entry, length, and hash verification.
- [x] Exact duplicates converge safely; conflicting content is retained.
- [x] Current-day and locked files remain untouched.
- [x] Retention deletes only recognized, readable, expired archives.
- [x] `-WhatIf` changes no source, archive, runtime, or scheduled-task state.
- [x] A temporary-directory integration test covers archive, duplicate,
  collision, retention, and installer preview behavior.
- [x] No live runtime or scheduled task is changed during repository verification.
- [x] Identity-verified SND-HOST deployment retains the current-day live CSV,
  and the installed SYSTEM task completes with result `0`.
- [ ] Inspect the first real completed prior-day SND-HOST ZIP after rollover;
  the cleaned target had no prior-day source candidate during deployment.

## Verification and deployment gate

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-management\Test-LhmLogManagement.ps1
```

Before any later target deployment, run the shared verified-machine identity
script on the explicit controller/target transport and require the expected
machine ID. Then run archive and installer previews, inspect exact paths and
counts, execute one manual archive cycle, validate ZIP contents/hashes, and only
then register the new task. Legacy task retirement is a separate approved step.

## Verification log

- 2026-07-18 SND-DESK -> SND-HOST deployment: controller and target identities
  matched their enrolled IDs before each mutation. Previews and the first
  manual cycle found no completed source after the user's old-runtime cleanup.
  After live logging began, the stable runtime at
  `E:\SQ_HQ\Thermal_Control\Monitoring\LhmLogManagement` retained the growing
  current-day CSV, and `\SevGrp\Log-mangment\SQ LibreHardwareMonitor Log
  Management` completed under SYSTEM with result `0`. The verified-dead legacy
  `librehwlogs` task was retired only after replacement health passed.
- 2026-07-18: the isolated temporary-directory integration suite passed under
  Windows PowerShell 5.1 and PowerShell 7. It covered verified archive publish,
  exact duplicate convergence, collision retention, current-day and locked-file
  retention, archive and cleanup previews, config-driven invocation, verified
  retention, installer preview, and automatic cleanup of the validated temp
  root. No runtime directory or scheduled task was installed or modified.

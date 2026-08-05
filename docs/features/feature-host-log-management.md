# Feature Spec: Host-Neutral Log Management

**Status:** deployed on SND-HOST; repaired-path rollover, current paths, archives, and dry-run retention verified; 2026-08-05 hardening landed in source
**Updated:** 2026-08-05

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
- Source removal is itself verified: the source is renamed aside, its length and
  SHA-256 are re-checked against the archived snapshot under the pending name,
  and only then deleted. A source that changed after compression is restored and
  reported as failure; a removal interrupted mid-flight is restored by the next
  run, or retained and reported when its original name is occupied.
- A matching pre-existing verified ZIP is an idempotent duplicate. A mismatched
  pre-existing ZIP diverts publication to a deterministic
  `<base>-conflict-<hash8>.zip` name: the source is removed only after the
  conflict archive verifies, the mismatched original is retained for review, and
  the orchestrated run raises a one-shot alert instead of failing permanently.
- Publishing uses a temporary ZIP in the destination directory followed by a
  rename. A failed or partial archive never authorizes source deletion, and a
  published archive that fails its post-publish verification is removed by the
  same failure path so a bad publish cannot poison later runs.
- Each file's archival is fault-isolated: one failing file yields a `Failed`
  record while the remaining files, retention, and result reporting continue.
- Retention expiry is keyed to the log date embedded in the validated archive
  filename, not the file timestamp. Retention removes only readable, one-entry
  ZIPs in the recognized machine/year/month layout whose log date is expired.
  Unknown files are retained; expired archives that fail validation are retained
  as `RetainedInvalid` with the actual validation reason, and any such archive
  fails the orchestrated run. Orphaned `*.zip.tmp-*` temporaries older than one
  day are swept.
- Configuration validation reports absent and null required properties with the
  same curated error under strict mode.
- Archive, cleanup, and installer entry points support `-WhatIf`. The installer
  copies runtime scripts/configuration and registers or updates one scheduled
  task only when explicitly run with administrative authority. It accepts
  `-TaskPath` for non-root task folders, and `-ReconcileFromExistingConfig`
  re-reads the installed configuration instead of rebuilding it from arguments,
  so a reconcile cannot silently rewrite deployment values; reconcile without an
  existing configuration fails closed.
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
  entry, length, and hash verification, with the removal re-verified under a
  pending rename before deletion.
- [x] Exact duplicates converge safely; conflicting content is archived under a
  deterministic conflict name, the mismatched original is retained for review,
  and repeated conflict content converges as a duplicate.
- [x] Current-day and locked files remain untouched.
- [x] Interrupted removals are restored and re-archived by the next run;
  occupied restores are retained and reported.
- [x] Retention deletes only recognized, readable archives whose embedded log
  date is expired, in both directions: a fresh-timestamp expired-date archive is
  removed and an ancient-timestamp recent-date archive is retained.
- [x] Expired archives that fail validation are retained, flagged
  `RetainedInvalid`, and fail the orchestrated run; conflict archival raises a
  one-shot orchestrator alert; orphaned temporaries older than one day are
  swept while recent temporaries are retained.
- [x] Absent required configuration properties produce the curated error under
  strict mode.
- [x] `-WhatIf` changes no source, archive, runtime, or scheduled-task state,
  including the installer's reconcile mode; reconcile without an existing
  configuration fails closed.
- [x] A temporary-directory integration test covers archive, duplicate,
  conflict, restore, retention, sweep, alert, configuration, and installer
  preview behavior.
- [x] No live runtime or scheduled task is changed during repository verification.
- [x] Identity-verified SND-HOST deployment retains the current-day live CSV,
  and the installed SYSTEM task completes with result `0`.
- [x] Inspect real completed prior-day SND-HOST ZIPs after rollover. The seven
  July 18-24 archives and the first post-repair July 25 archive have the
  recognized one-entry layout and are readable.

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

- 2026-08-05 hardening (source-side): closed the archival removal race by
  renaming the source aside and re-verifying length/SHA-256 before deletion
  (`Remove-LhmVerifiedSourceFile`), fault-isolated each file's archival so one
  failure cannot abort the run or discard results, replaced the permanent
  collision failure with verified deterministic conflict-name publication plus a
  one-shot orchestrator alert, made a failed post-publish verification remove
  its own bad destination, keyed retention to the embedded log date instead of
  the file timestamp, flagged expired invalid archives as run-failing
  `RetainedInvalid` with the true validation reason, added a one-day sweep for
  orphaned `*.zip.tmp-*` temporaries, added `-Confirm:$false` to the orchestrated
  archive call, made absent required configuration properties produce the
  curated error under StrictMode 3.0, and added installer `-TaskPath` reconcile
  usage plus `-ReconcileFromExistingConfig`. An adversarial review then drove
  four follow-ups: the orphan-restore path is fault-isolated per file, a failed
  post-publish verification deletes its own bad destination only on a confirmed
  content mismatch (never on a transient `unreadable`), the schema/version gate
  reports absent properties with the curated error, and the temporary-file sweep
  checks age before name. The expanded integration suite passed under Windows
  PowerShell 5.1 and PowerShell 7. Deploy note: retention keying changes from
  file timestamp to embedded log date on the first reconciled run — review a
  `-WhatIf` retention preview against the production archive before the first
  scheduled run (on SND-HOST the archive's oldest entry is 2026-07-18, so the
  switchover deletes nothing today). No live runtime, task, installed script, or
  archive changed during this source work; the SND-HOST reconcile of the
  installed copies is a separate identity-verified operation recorded when
  performed.
- 2026-07-31 SND-HOST workspace migration: the installed scripts moved to
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\operations\log-management`,
  the configured log source moved to `deployments\current`, and the archive moved to
  `data\logs\archive`. The SYSTEM task retained its principal, highest run
  level, daily 03:45 trigger, and 365-day policy while receiving the new
  physical action/config/working paths. A manual post-move run returned `0`,
  retained the growing current-day CSV, and left all 13 archives intact.
- 2026-07-30 SND-HOST first repaired-path rollover: read-only inspection found
  `E:\SQ_HQ\Monitoring\LogArchive\SND-HOST\2026\07-Jul\LibreHardwareMonitorLog-2026-07-25.zip`,
  created by the July 26 03:45 cycle. It opens fully and contains exactly one
  expected CSV entry of 166,129,155 uncompressed bytes; the ZIP SHA-256 is
  `177DEA9CA7FA46AAB228FC53F00869EF374122BF4E3E08727062ABF54FF6FA14`.
  The installed task's latest run was 2026-07-30 03:45 with result `0`, and the
  latest July 29 ZIP is also readable with one CSV. Task Scheduler's Operational
  event channel is disabled, so per-run event history was unavailable without a
  host change; it was not enabled. No archive command, retention command, task
  mutation, or deployment was run for this verification.
- 2026-07-25 SND-HOST path repair: the installed configuration was backed up
  with the LibreHardwareMonitor deployment rollback packet, then its only stale
  values were corrected from the retired `Thermal_Control` tree to
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor` and
  `E:\SQ_HQ\Monitoring\LogArchive`. The SYSTEM task, scripts, working
  directory, 03:45 schedule, machine name, and 365-day retention stayed
  unchanged. The isolated integration suite passed. Live `-WhatIf` retained the
  growing current-day CSV, retention selected nothing, and all seven existing
  archives remained present. The config-driven destructive invoker was not run.
- 2026-07-18 SND-DESK -> SND-HOST deployment: controller and target identities
  matched their enrolled IDs before each mutation. Previews and the first
  manual cycle found no completed source after the user's old-runtime cleanup.
  After live logging began, the stable runtime at
  `E:\SQ_HQ\Monitoring\LhmLogManagement` retained the growing
  current-day CSV, and `\SevGrp\Log-mangment\SQ LibreHardwareMonitor Log
  Management` completed under SYSTEM with result `0`. The verified-dead legacy
  `librehwlogs` task was retired only after replacement health passed.
- 2026-07-18: the isolated temporary-directory integration suite passed under
  Windows PowerShell 5.1 and PowerShell 7. It covered verified archive publish,
  exact duplicate convergence, collision retention, current-day and locked-file
  retention, archive and cleanup previews, config-driven invocation, verified
  retention, installer preview, and automatic cleanup of the validated temp
  root. No runtime directory or scheduled task was installed or modified.

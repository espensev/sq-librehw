# Feature Spec: Live State Verification

**Status:** implemented; report-only
**Updated:** 2026-08-05

## Problem

The SND-HOST stack runbook
(`E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\docs\OPERATIONS.md`) defines a
live-verification gate — exact-path process, task states, endpoint health, CSV
growth, payload hash — that has so far been executed as hand-typed commands.
Hand-typed verification drifts, skips steps under time pressure, and leaves no
uniform record.

The maintainer authorized a **read-only verifier** on 2026-08-05 while
explicitly keeping `ops/deploy` SND-DESK-only. This tool deploys nothing,
mutates nothing, and does not relax that roadmap non-negotiable: it is
report-only operator tooling in the same lane as
`docs/features/feature-host-operator-utilities.md`.

## Goal

One command, `ops/live-verification/Test-LhmLiveState.ps1`, that reads the
stack's own manifests and reports whether the live deployment matches them.

## Behavior contract

- Input is an explicit `-StackRoot`; the script contains no machine-specific
  path. All expectations come from the stack's `manifests\layout.json`,
  `manifests\consumers.json`, and `manifests\channels\live.json`.
- Checks, each emitted as a `Check`/`Status`/`Detail` record with `Pass`,
  `Fail`, or `Info`:
  - `manifests`: all three manifests exist and parse;
  - `machine`: the manifest `computerName` matches the running machine;
  - `live-root`: the declared live root exists, is not a reparse point, and
    contains the declared entry point;
  - `process`: exactly one process machine-wide carries the entry-point image
    name, and it runs the exact live entry-point path; same-name processes whose
    paths are unreadable from the calling session are reported as an elevation
    hint rather than as absence (run the verifier elevated when the application
    runs elevated);
  - `entry-point-hash`: the live entry point's SHA-256 matches
    `channels\live.json`, or `-ExpectedSha256` when supplied (used during a
    cutover before the manifest refresh);
  - `task:<name>`: every `scheduledTask` consumer binding exists and its action
    references the bound target; the entry-point task must be `Running`, other
    tasks must not be disabled and must show result `0`/`267009` within
    26 hours;
  - `listener-config`: the executable-adjacent configuration declares the
    listener; wildcard binds (`0.0.0.0`, `::`, `+`, `*`) are verified by
    confirming a listener on the configured port and probed via loopback;
  - `endpoints`: `/`, `/data.json`, and `/metrics` return HTTP 200 on the bound
    address;
  - `csv`: the current-day CSV exists and grows over `-CsvGrowthSeconds`
    (default 5; `0` skips growth and reports `Info`);
  - `log-tooling`: the installed log-management scripts and configuration are
    present at the declared runtime.
- Output is the record stream, or a single `sq.lhm.live-state` version-1 JSON
  document with `-Json`. Any `Fail` ends the run with a throw naming the failed
  checks, so scheduled or scripted callers get a non-zero exit.
- Strictly read-only: no file, task, process, service, registry, or manifest is
  created or changed. Reading is the only side effect.
- Works under Windows PowerShell 5.1 and PowerShell 7.

## Non-goals

- No promotion, rollback, staging, task registration, or manifest refresh; the
  tool grants no deployment authority and replaces no approval gate in
  `OPERATIONS.md`.
- No comparison of installed script content against the repository (presence
  only); content reconciliation is a separate identity-verified operation.
- No remote transport; run it on the target host.
- Not an `ops/deploy` surface and not a relaxation of the SND-DESK-only
  deployment boundary.

## Acceptance

- [x] Against a synthetic fixture stack, the verifier reports per-check records,
  fails the run, and exits non-zero when live state is absent.
- [x] Manifest, machine-mismatch, missing-process, hash-mismatch, missing-task,
  missing-listener, and missing-CSV conditions each surface as distinct `Fail`
  records.
- [x] The fixture run proves read-only behavior: no fixture file is created,
  changed, or touched by the verifier.
- [x] `-Json` emits parseable `sq.lhm.live-state` v1 with the same records.
- [x] The self-test passes under Windows PowerShell 5.1 and PowerShell 7 and is
  wired as the `live-verification` CI gate.

## Verification

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\live-verification\Test-LhmLiveStateSystem.ps1
pwsh -NoProfile -File ops\live-verification\Test-LhmLiveStateSystem.ps1
```

Live usage on SND-HOST (read-only; safe at any time):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\live-verification\Test-LhmLiveState.ps1 `
  -StackRoot 'E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack'
```

## Verification log

- 2026-08-05: implemented with fixture self-test passing under both engines;
  first live SND-HOST run recorded separately once executed.

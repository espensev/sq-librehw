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
  - `manifests`: all three manifests exist and parse; their schema/version and
    `machineId` contracts agree; `layout.json`'s operations root matches the
    explicit `-StackRoot`; and the live channel's deployment root matches the
    layout's live root; before any task lookup, the consumer manifest must
    contain exactly one binding for the declared live entry point and exactly
    one for the declared log-management invocation script;
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
  - `task:<name>`: every `scheduledTask` consumer binding exists with exactly
    one action, the target is an exact executable path or exact PowerShell
    `-File` path, and the working directory is exactly the target's parent;
    a configured `PowerShellExecutable` is an exact engine-path contract, while
    legacy configuration permits only the machine Windows PowerShell path or
    the verifier process's own full PowerShell engine path, never another
    caller-`PATH` discovery; the log-management task must also carry the exact sibling
    `log-management.json` `-ConfigPath`; the entry-point task must be `Running`,
    while other tasks must not be disabled and must show result `0`/`267009`
    within 26 hours;
  - `listener-config`: the executable-adjacent configuration declares the
    listener; wildcard binds (`0.0.0.0`, `::`, `+`, `*`) are verified by
    confirming a listener on the configured port and probed via loopback;
  - `endpoints`: `/`, `/data.json`, and `/metrics` return HTTP 200 on the bound
    address;
  - `csv`: the current-day CSV exists and grows over `-CsvGrowthSeconds`
    (default 5; `0` skips growth and reports `Info`);
  - `log-tooling`: the installed log-management scripts and configuration are
    present at the declared runtime; the configuration has the supported
    schema/version, includes the declared live root in `SourceDirectories`,
    names the declared archive root and computer, and has a retention period
    from 1 through 36500 days; an additive `PowerShellExecutable`, when
    present, must be a full path and is the exact script-task engine contract.
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
- No comparison of installed script content against the repository; script
  presence and installed-configuration contracts are checked, while content
  reconciliation remains a separate identity-verified operation.
- No remote transport; run it on the target host.
- Not an `ops/deploy` surface and not a relaxation of the SND-DESK-only
  deployment boundary.

## Gate 4 coverage boundary

The verifier automates repeatable observations but does not turn the runbook's
transactional evidence into an approval-free gate:

| `OPERATIONS.md` Gate 4 obligation | Automated coverage | Evidence that remains manual |
|---|---|---|
| 1. One exact-path process | Complete. | None for this observation. |
| 2. Root task Running with the exact action and working directory | Complete against the declared consumer binding. | Gate 3 task XML, principal, triggers, and restart-setting capture remains manual. |
| 3. Running version and SHA-256 match the selected package | Partial: SHA-256 is checked against `channels\live.json`, or an explicitly supplied `-ExpectedSha256`. | Compare product version and provenance with the explicitly selected immutable candidate/package. |
| 4. `/`, `/data.json`, and `/metrics` return HTTP 200 | Complete. | None for this observation. |
| 5. Every pre-cutover setting remains present and changed values are explained | Not covered. | Capture and compare the complete pre/post settings key/value set. |
| 6. The same current-day CSV remains and grows after restart | Partial: current-day existence and growth are checked. | Prove that the file is the same pre-cutover CSV. |
| 7. Log task and configuration still use the declared live/archive roots | Complete for the exact task target/config arguments and parsed configuration roots. | Installed-script byte identity remains a separate reconciliation check. |

Candidate acceptance, promotion or rollback authority, package inventory,
settings-preservation evidence, immutable history/rollback packets, and
manifest refresh remain outside this verifier.

## Acceptance

- [x] Against a synthetic fixture stack, the verifier reports per-check records,
  fails the run, and exits non-zero when live state is absent.
- [x] Manifest, machine-mismatch, missing-process, hash-mismatch, missing-task,
  missing-listener, and missing-CSV conditions each surface as distinct `Fail`
  records.
- [x] Unsupported or cross-inconsistent manifests, non-exact task actions,
  wrong task working directories/arguments, and invalid log-management
  configuration contracts fail independently in adversarial fixtures.
- [x] Drifted, zero, or incomplete expected task bindings fail before task
  lookup; configured engine paths match exactly; and a discoverable
  same-basename engine injected through caller `PATH` is rejected by the
  legacy fallback.
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

- 2026-08-05: implemented with fixture self-test passing under both engines.
  First live SND-HOST run the same day: 11/11 checks Pass, exit 0 — manifests,
  machine, live root, exact-path process (PID 15988), entry-point SHA-256
  matching `channels\live.json`, both task bindings (entry task Running; log
  task last run 03:45:01 result 0), listener 192.168.2.5:8080, all three
  endpoints 200, current-day CSV growth, and complete installed log tooling.
  JSON report preserved in the stack packet
  `deployments\history\20260805-034905-log-tooling-hardening`.
- 2026-08-05: strengthened the same 11-record report with fail-closed manifest
  contracts, exact single-action task target/working-directory/config-argument
  checks, and parsed installed log-management configuration validation.
  Adversarial fixtures for schema, version, machine/root disagreement, task
  action ambiguity, and every configuration field above passed under PowerShell
  7 and Windows PowerShell 5.1. The 5.1 run used the machine Windows PowerShell
  module path because the launching development shell included PowerShell 7
  module directories, which otherwise hide the built-in `Get-FileHash` cmdlet.
  Final adversarial review also proved that drifted, zero, or incomplete
  expected task bindings fail in the manifest prerequisite before task lookup,
  a configured `PowerShellExecutable` replaces the legacy engine fallback
  exactly, and a fake same-basename engine remains rejected under both engines
  even when a prepended caller `PATH` makes `Get-Command` discover it.
  This was source-fixture evidence only; no new live observation or deployment
  is claimed.

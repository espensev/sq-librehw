# Review - Official upstream integration and remaining work

**Date:** 2026-10-19

**Surface:** fixed-point integration of official `e68a7ef7326fa875210e89e1402d93a45ffe4d3e`
into local `5ff9d2d29f7373343273943d5954ad4971131a42`; existing primary-checkout
changes and the open-work queue reviewed separately.

**Spec:** [integration contract](../features/feature-upstream-sync-2026-10-19.md),
`feature-memory-ui-reliability.md`, `feature-local-release-system.md`,
`feature-http-server-safety.md`, and `docs/campaign-backlog.md`.

**Standards:** `AGENTS.md`, `docs/README.md`, `.codex/skills/project.toml`,
`docs/architecture/refactor-roadmap.md`.

**Verdict:** PASS WITH NOTES for source integration. No remaining confirmed merge
blocker after the fixes below. Existing operational and reliability defects
remain open; this is not clearance to deploy. Campaign cleanup safety,
empty CI gate handling, per-device polling isolation, PawnIO platform gating,
and crash-time history preservation were subsequently repaired and verified below.

## Findings and next work

### Medium priority

| Area | Current evidence | Required next step |
|---|---|---|
| Runtime hardware options | `Hardware/Computer.cs:199` adds GPU groups before the backing flag commits; `UI/MainForm.cs:707` only logs apply failures. | Category-level transactions and deliberate UI/settings rollback, including partial enable/disable and retries. |
| DIMM discovery retry | `Hardware/Memory/MemoryGroup.cs:193` publishes before notifications; its retry catches subscriber failure as discovery failure. | Separate publication from notification failure and prevent duplicate identities. |
| Release health and timeout defaults | Install line 17 and Restore line 11 still default to port 8085; the local release spec records explicit 18085. Install/Restore allow 300 seconds while the complete checkout's launcher allows 120. | Bind health checks to validated configuration and reject unsupported timeout values before mutation. |
| Migration recovery | `Move-LibreHardwareMonitorRuntimeRoot.ps1:647` changes the shim before task retargeting; line 651 sets restoration responsibility only after both. | Independently restore partial binding writes before reusing runtime migration. |
| Authentication transitions | `UI/AuthForm.cs:40` changes `AuthEnabled`; `HttpListenerDispatchService.cs:82` sets the listener scheme only at startup. | Coordinate listener reconfiguration and drain. This is an availability mismatch, not a proven authentication bypass. |

These remaining source findings were not reproduced again during the tooling
safety fixes. Existing specs and the October 8 review record earlier reproductions. The
latter is preserved at
`archive/pre-cleanup-20261019:docs/reviews/review-2026-10-08-improvement-continuation.md`
and can be read with `git show` using that revision/path.
The main-tree corruption discovered here was separate: seven recent commits
and their dependent objects were missing. Exact-hash recovery restored all
49 missing objects (7 commits, 26 trees, 16 blobs), preserving original ancestry.
`git fsck --full` and pack checks pass. The cause of object loss remains unknown;
no replacement history, reset, or force operation was used.

### Checkout cleanup completed on 2026-10-19

The subsequent user-authorized cleanup restored all 12 missing tracked files
from `master`, including the icons, AuthForm resource, launcher, campaign
imports, licenses and Avalonia spec. The original dirty tracked state and both
historical review drafts are preserved in `0df7941f` on
`archive/pre-cleanup-20261019`. That archive intentionally records the former
deletions and must not be merged into `master`.

Generated `.audit` receipts remain unchanged in place and are now explicitly
ignored as local state. The documentation index points to this current review.
At cleanup time the PawnIO implementation remained in its labeled preservation
checkpoint. Its subsequent acceptance is recorded below. Cleanup alone closed
no implementation or deployment gate.

### Tooling safety fixes completed on 2026-10-19

- `scripts/task_runtime/merge.py` now refuses dirty destination trees without
  stashing, requires exact fresh Git worktree identity and merged ancestry for
  cleanup, preserves ignored/untracked/staged changes, and uses non-forced Git
  removal. Unregistered paths are retained. Copying files does not prove that
  the source branch was merged, so source branches remain available for recovery.
- `scripts/task_runtime/orchestration.py` routes orphan cleanup through those
  same checks and reports actual removal rather than merely a prune request.
- `eng/ci/Invoke-LhmGates.ps1` validates the whole selection before execution;
  gates containing no nonblank allowed command fail with exit 2, including in
  dry-run mode. Valid partial gates continue to execute their allowed fixture.
- Regression tests first reproduced forced cleanup, lost local work, recursive
  orphan removal, and false-success gates. The full campaign suite then passed
  52 tests, including 17 merge/recovery safety tests using disposable Git repos.
  The complete CI suite passed in PowerShell 5.1 and 7: 99 assertions per engine,
  zero failures, and two existing optional YAML checks skipped without PyYAML.

Acceptance and regression commands are maintained in the
[campaign control-plane contract](../architecture/campaign-control-plane.md#merge-and-cleanup-acceptance-2026-10-19)
and [CI runner documentation](../../eng/ci/README.md#exit-codes).
No real campaign cleanup, push, deployment, or runtime operation was performed.

### Device polling isolation completed on 2026-10-19

`UI/UpdateVisitor.cs` now isolates recoverable per-device update failures,
invalidates current built-in readings, and continues children, siblings, later
devices, and CSV logging. Min/max and completed history remain; pending averages
and temperature-rate samples reset so recovery starts fresh. Direct library
callers retain exception behavior, and fatal failures still propagate.

The [reliability contract](../features/feature-memory-ui-reliability.md#device-polling-isolation-follow-up-2026-10-19)
records behavior and regression commands. The deterministic suites passed 513
tests with one existing opt-in skip; both application frameworks and all five
library targets built with zero warnings/errors. Hardware failure acceptance and
deployment remain separate.

### PawnIO platform gate accepted on 2026-10-19

**Review verdict:** PASS for source integration. Reviewed all 11 changed paths
between `8aae1fbc` and `8f4a143c`, using
[the PawnIO specification](../features/feature-pawnio-platform-gating.md),
`AGENTS.md`, the CI runner contract, and the current module callers.
Both guards precede the unsupported operation and retain Windows lookup order.
The original missing portable-process evidence is now supplied; the spec's
inaccurate claim that unloaded `ExecuteHr` clears buffers was corrected to the
existing zero-return-size/unchanged-buffer behavior. No remaining merge blocker.

The preservation branch was integrated with current `master` without changing
its PawnIO implementation. Local validation passed 515 deterministic tests
(one existing opt-in skip), both Windows application builds, all five library
targets, and 99 CI assertions per PowerShell engine (two optional YAML skips).
[Hosted run 39286347591](https://github.com/espensev/sq-librehw/actions/runs/39286347591)
at `8f4a143c` passed the portable gate on real Ubuntu and Windows, including
real Unix type initialization and AMD/Intel/SMU wrapper construction.

Hardware driver I/O and deployment remain outside this verification.

### Crash-time history preservation completed on 2026-10-19

`Hardware/Sensor.cs` now claims a validated, normalized bounded checkpoint through
the existing settings store. Two dirty autosaves preserve it in both primary
and backup files, and abrupt reopen recovers it without a hardware-close event.
Rejected and unclaimed histories still disappear; explicit clear/disable and
window narrowing remove or trim the checkpoint. Clean hardware close refreshes
it with current-session samples.

The [history preservation contract](../features/feature-memory-ui-reliability.md#crash-time-history-preservation-follow-up-2026-10-19)
records the acceptance and verification. Three real-file cases first reproduced
the loss; the repaired focused suites passed 49 tests with one existing opt-in
skip. Independent full verification passed 527 deterministic tests with that
same skip, both Windows application builds, and all five library targets with
zero warnings/errors. The source change does not add periodic sample saving.
Independent review accepted all six changed files and reran seven recovery,
clear/disable, and window-change cases successfully.
Live crash acceptance and deployment remain separate. Runtime hardware-option
transactions are the next source item in the remaining queue.

### Separate acceptance and policy gates

- Plan-001 attended smoke and Plan-012 criterion 10 remain open in
  `docs/campaign-backlog.md:30`; Plan-014 still requires its named authorization.
- Operator layout/text-scaling checks, HTTP listener/firewall acceptance and
  physical hardware checks remain distinct from source tests.
- Failed Reset reconstruction and reader-origin/CORS policy remain decisions;
  transactional Open and partial-reset cleanup already exist.
- Current reliability/release specs record the October 8 Corsair promotion of
  `957923fc`. The old handoff's claim that the fix is undeployed is stale. This
  turn did not reverify or alter the installed runtime.

## Integration decisions

The shared upstream baseline was `d6cb2604`; the captured official tip adds
50 commits across 53 paths. The merge retains complete official ancestry while
adapting these contracts:

- Keep Data/SmallData units, ordinals and identifiers; defer upstream's
  `677a3a56` byte migration. DiskInfoToolkit's own host-counter conversion is
  adapted using its pinned old/new profile rules; UInt64-byte overflow remains
  unavailable under the new dependency.
- Keep central package management, fork CI workflow selection, storage lifecycle
  coordination, shared NVML leases, Corsair failure handling and graph lanes.
- Append new motherboard enum entries. Put new AMD sensors outside the legacy
  sequential identifier allocation. Correct the upstream flow-register address
  while retaining the documented litres/hour conversion.
- Resolve two newly imported failure paths before landing: AMD PM-table loss
  now clears stale readings without replacing a fresh MSR fallback, and
  NCT6701D invalid/remapped sources clear previous temperatures.
- New NVIDIA polling reacquires identity under the lifetime lock. PawnIO
  failures clear per-channel readings, preserve current NVAPI fallback, and
  feed the hotspot-rate sensor once from the final fresh value.
- Accept explicit loopback, USB exclusion, About-link shell execution,
  collapsed-plot input protection, gadget clamping, battery recovery, fan
  restoration, CPU support and motherboard mappings.

## Verification

All commands ran in the complete `wt/upstream-sync-20261019` worktree:

| Command | Result |
|---|---|
| `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.slnf -c Release -p:Platform=x64 --no-restore` | 505 passed, one intentional opt-in skip; Library 170, Application 222, Contracts 113 |
| `dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore` | Zero warnings/errors |
| Same application build with `-f net472` | Zero warnings/errors |
| `dotnet build LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj -c Release -p:Platform=x64 --no-restore` | All five framework targets and NuGet pack pass, zero warnings/errors |
| `node webtests/selftest.node.js` | 339/339 |
| `node --test webtests/console.tests.js webtests/workspace.tests.js` | 18/18 |
| `powershell.exe -NoProfile -File ops/log-management/Test-LhmLogManagement.ps1` | PASS |
| `powershell.exe -NoProfile -File ops/candidate/Test-LhmReleaseSystem.ps1` | PASS, 191 fixture assertions; no live candidate or deployment |
| Whitespace/conflict/package checks and independent integration review | PASS |

Full logs, exact-object recovery receipt and final ancestry/preservation checks
are in the `librehw-upstream-sync-20261019` child of the system temporary
directory (`[IO.Path]::GetTempPath()`). Receipt files are `exact-object-recovery.json`,
`recovery-fsck.txt`, `tests-full.log`, framework build logs and `closeout.json`.
These are execution evidence, not runtime authority.

Cleanup rebuilt both application frameworks from the restored primary checkout:
zero warnings/errors for `net10.0-windows` and `net472`. Preservation checks
compare checkpoint blobs, all 12 restored tracked files, the PawnIO source/test
bytes and all 12 local audit files. The `librehw-cleanup-20261019` child of the
system temporary directory contains those receipts and build logs;
`closeout.json` records the final status of all six worktrees.

## Coverage

Deep review covered the changed NVIDIA/NVML/PawnIO C# paths, storage facade and
lifecycle/API adaptations, storage counter adapter, Data/SmallData consumers,
AMD identifier/freshness adaptations, model enumeration, EC flow mapping,
NCT6701D and retained NCT6687DR state handling, native UI conflict resolutions,
HTTP loopback, package/project changes, battery recovery and fan-control changes.
Each added/changed regression file was read and executed.

The remaining AMD register definitions and motherboard mapping tables
(`Identification`, `SuperIOHardware`, `RyzenSMU`) were sampled. Upstream
`Nvidia.bin`, `RyzenSMU.bin`, `SmbusI801.bin` and `SmbusNCT6793.bin` were checked
for inclusion/version and compiled as resources; their machine instructions and
physical behavior were not independently audited. Unchanged modules and the
remaining historical sweep candidates were excluded. No SND-HOST, live process,
hardware fault, interactive desktop, or real Linux acceptance was claimed.

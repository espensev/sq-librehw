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
remain open; this is not clearance to deploy or run campaign cleanup.

## Findings and next work

### High priority

1. **[standards/regression] Primary checkout remains incomplete.** Its 12
   pre-existing tracked deletions include the application icon, AuthForm resource,
   canonical launcher, campaign imports and license files. The application project
   still requires its icon at `LibreHardwareMonitor.Windows.Forms.csproj:11`;
   installer `ops/deploy/snd-desk/Install-LibreHardwareMonitorRelease.ps1:122`
   requires the missing launcher. Deletion ownership is unresolved. This turn
   preserves those deletions and the separate `docs/README.md` edit and untracked
   audit files. Resolve ownership before using the primary tree as a build or
   campaign baseline; the complete integration worktree supplies verified source.

2. **[regression/standards] Campaign cleanup can discard unrelated work.**
   `scripts/task_runtime/merge.py:56` lets recorded worktree parents authorize
   themselves. Lines 173, 183 and 190 force-remove worktrees, recursively delete
   fallback paths and force-delete branches. Line 236 stashes without a matching
   restore path. Repair path authority, dirty-work preservation, ancestry checks
   and recovery before using campaign merge/cleanup. These paths were inspected,
   never executed.

3. **[regression] Device exceptions still starve subsequent devices and CSV.**
   `UI/UpdateVisitor.cs:20` propagates `hardware.Update()` failures;
   `UI/MainForm.cs:829` then exits before logging at line 832. Later polls still
   run, so this is repeated starvation, not a stopped timer. Specify per-device
   failure isolation, unavailable readings, healthy-device continuation and
   recovery before implementing the broader polling change.

### Medium priority

| Area | Current evidence | Required next step |
|---|---|---|
| PawnIO platform gate (F-013) | `LibreHardwareMonitorLib/PawnIo/PawnIo.cs:25` enters Windows registry code; line 67 opens the Windows device. Existing `fix/f-013-pawnio-os-gate` remains at `fd6f106a` with three uncommitted patch/test/spec files. | Finish the preserved lane, refresh integration and test a real portable process; injected Unix flags alone do not establish Linux acceptance. |
| False-success CI gates | `eng/ci/Invoke-LhmGates.ps1:465` skips disallowed keys; line 531 passes the result without requiring a step. | Reject selected gates with no executable allowed command. Current valid configuration does not repair malformed-config behavior. |
| Crash-time history loss | `Hardware/Sensor.cs:473` removes loaded history; `Utilities/PersistentSettings.cs:313` excludes unclaimed histories; dirty autosaves can replace both saved copies. | Preserve and claim validated bounded histories; cover two dirty saves and crash/reopen. |
| Runtime hardware options | `Hardware/Computer.cs:199` adds GPU groups before the backing flag commits; `UI/MainForm.cs:707` only logs apply failures. | Category-level transactions and deliberate UI/settings rollback, including partial enable/disable and retries. |
| DIMM discovery retry | `Hardware/Memory/MemoryGroup.cs:193` publishes before notifications; its retry catches subscriber failure as discovery failure. | Separate publication from notification failure and prevent duplicate identities. |
| Release health and timeout defaults | Install line 17 and Restore line 11 still default to port 8085; the local release spec records explicit 18085. Install/Restore allow 300 seconds while the complete checkout's launcher allows 120. | Bind health checks to validated configuration and reject unsupported timeout values before mutation. |
| Migration recovery | `Move-LibreHardwareMonitorRuntimeRoot.ps1:647` changes the shim before task retargeting; line 651 sets restoration responsibility only after both. | Independently restore partial binding writes before reusing runtime migration. |
| Authentication transitions | `UI/AuthForm.cs:40` changes `AuthEnabled`; `HttpListenerDispatchService.cs:82` sets the listener scheme only at startup. | Coordinate listener reconfiguration and drain. This is an availability mismatch, not a proven authentication bypass. |

These are current source findings, not newly repeated fault reproductions this
turn. Existing Specs and the October 8 local review record earlier reproductions.
The main-tree corruption discovered here was separate: seven recent commits
and their dependent objects were missing. Exact-hash recovery restored all
49 missing objects (7 commits, 26 trees, 16 blobs), preserving original ancestry.
`git fsck --full` and pack checks pass. The cause of object loss remains unknown;
no replacement history, reset, or force operation was used.

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

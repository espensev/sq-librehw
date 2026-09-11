# Feature Spec: Local Release and Runtime Separation

**Machine scope:** production/live paths are SND-DESK only. Deployment and
launcher actions fail closed to `snd-desk`; the non-live fixture is intentionally
peer-safe under isolated temporary roots. Do not treat these paths, tasks,
users, or runtime state as SND-HOST instructions.

**Status:** the graph-lane startup zoom repair is installed on SND-DESK.
Launcher convergence and runtime/rollback hashes were rechecked on
2026-09-11; Validate returned PASS without drift. Technical graph restart
verification passed; operator layout sign-off remains separate.
Current Data/Bin authority is persisted `SEV_LOCAL_DATA` / `SEV_LOCAL_BIN`
(User, then Machine, then Process), with recursive expansion of `%...%`
templates. The app-owned `librehw` launcher is the sole activation authority.
Completed migrations and their dated evidence are retained below.
**Updated:** 2026-09-11

## Problem

Before this release, Libre Hardware Monitor was launched from disposable
repository build output:

- `E:\SQ_HQ\u-programs\bin\librehw.cmd` delegates to
  `E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1`;
- that script targets
  `bin\Debug\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe`;
- both live scheduled tasks target the corresponding nested Release path.

The build tree was also mutable runtime storage. Settings and backups followed
the executable path, while CSV logs used the executable base directory. The
Release directory accumulated 3,361 CSV files totalling 9,058,052,916 bytes.
Normal builds, live state, startup ownership, and deployment were therefore
coupled.

## Goals

- Keep `librehw.cmd` as the permanent public command under `%SEV_LOCAL_BIN%`.
- Run one fixed, shallow installed EXE rather than a Debug/Release build output.
- Publish the local net10 x64 app as one framework-dependent EXE.
- Keep config, backups, and CSV logs under the dedicated machine-local
  `%SEV_LOCAL_DATA%\LibreHardwareMonitor` root.
- Provide manifest-backed promotion, exactly one rollback slot, and bounded
  retention.
- Converge the launcher, no-UAC task, logon start, shortcuts, and process checks
  on the same installed path.
- Keep source builds and both target-framework verification independent of the
  live runtime.

## Non-goals

- MSI/MSIX, system-wide installation, automatic internet updates, or a package
  feed.
- Deploying net472 locally; it remains a compatibility build gate.
- Retaining an unbounded release/package history.
- Deleting or archiving the existing 9 GB log store during release cutover. A
  later, separately accepted repository-output cleanup preserved it outside the
  repository; the bounded 2026-08-15 relocation keeps that payload intact.
- Force-stopping an unknown or path-mismatched process.
- Changing the existing managed tray-toggle/foreground restoration behavior.

## Baseline evidence before implementation

- At discovery,
  `LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj`
  declared `WinExe`, targeted `net472;net10.0-windows`, and forced normal output
  under `..\bin\$(Configuration)\`.
- `LibreHardwareMonitor.Windows.Forms/Program.cs` required four managed
  DLLs to exist physically beside the EXE.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` loaded/saved settings
  beside the EXE and used the process working directory for PawnIO extraction.
- `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs` used the EXE base
  directory for CSV logs.
- `StartupManager` compared and registered the exact executable path, including
  a scheduler-root task.
- CI built and uploaded raw `bin/Release` directories.
- The Debug and Release config files materially differed: 7,051 bytes versus
  30,554 bytes with different SHA-256 hashes. First migration could not infer
  authority from name or modification time.
- Live startup ownership was split:
  - `\LibreHardwareMonitor` was the scheduler-root logon task and most recently
    returned `0xC0000005`;
  - `\SevGrp\AdminTask\LibreHW-No-UAC` was the managed on-demand elevated task
    and most recently returned `0x40010004`;
  - both named the nested Release executable.
- The required .NET 10 Windows Desktop runtime was present on `snd-desk`
  (`Microsoft.WindowsDesktop.App 10.0.10`).

## Selected release shape

### Stable runtime

```text
E:\Monitoring\LibreHW\
├─ Runtime\
│  ├─ LibreHardwareMonitor.Windows.Forms.exe
│  ├─ release.json
│  ├─ librehw.runtime.json
│  └─ rollback\
│     ├─ LibreHardwareMonitor.Windows.Forms.exe
│     └─ release.json
└─ Scripts\
   └─ Start-LibreHardwareMonitor.ps1
```

The fixed process path is:

```text
E:\Monitoring\LibreHW\Runtime\LibreHardwareMonitor.Windows.Forms.exe
```

There are no version-named live directories and no package cache. Promotion
retains only the current and immediately previous verified payload. The
`rollback` directory is empty after the first install and holds one verified
EXE/manifest pair after a later successful promotion.

The runtime remains shallow and release-owned, while the sibling `Scripts`
directory holds the source-controlled launcher outside the closed runtime
inventory. The earlier data-root relocation did not move, copy, or republish
the application payload; the later guarded runtime-root migration does.

### Mutable data

```text
%SEV_LOCAL_DATA%\LibreHardwareMonitor\
├─ LibreHardwareMonitor.Windows.Forms.config
├─ LibreHardwareMonitor.Windows.Forms.config.backup
└─ logs\
   └─ LibreHardwareMonitorLog-*.csv
```

Deploy scripts resolve that root from persisted `SEV_LOCAL_DATA` at User, then
Machine, then Process scope. They must not prefer inherited Process `sqdata` or
`sqbin`: a long-lived agent can keep the pre-cutover values after User/Machine
already name SevLocal. The installed runtime JSON still stores an expanded
absolute `dataRoot` because the app requires a filesystem path. The app
resolves that runtime configuration first, then an explicit Libre Hardware
Monitor data-root override, then falls back to executable-adjacent storage.
Ambient `%sqdata%` remains not application authority because other hosts and
shells may bind it to unrelated state.

### Public launcher

`%SEV_LOCAL_BIN%\librehw.cmd` remains the public command. Its canonical source
invokes the installed, receipted `%SEV_LOCAL_BIN%\runw\runw.exe` with
`/wait /quiet /cwd:-`,
starts the app-owned
`Start-LibreHardwareMonitor.ps1` through Windows PowerShell 5.1, forwards all
arguments, and returns the helper's exit code. The host is pinned to
`%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`; ambient command
search is not launcher authority. It does not use `start`, so a
caller can observe activation failure instead of receiving an asynchronous
success. The PowerShell helper owns the bounded launch mutex, stable
EXE/working-directory checks, managed-task start, and existing-window
tray-toggle/foreground restoration.

No SoleX target or command participates in LibreHW launch or activation.
`Retire-LibreHardwareMonitorSoleXIntegration.ps1` owns the completed transition,
its recovery packet, and idempotent Plan/Apply/Validate proof.

`Sync-LibreHardwareMonitorLauncher.ps1` is the continuing convergence owner.
`Plan` and `Validate` report source, deployed-artifact, receipt, and managed-task
drift without mutation. `Apply` first requires the installed known-folder v2
identity for `snd-desk`, validates but never recreates the existing managed
task, validates the installed central RunW bytes against
`%SEV_LOCAL_DATA%\RunW\install-receipt-v2-1.3.1-64472d3-help-docs.json`, and transactionally replaces only the
app relay and public CMD. RunW is an authority dependency, not an app-vendored
deployment or rollback target. The convergence tool retains a typed v2 receipt
and exact two-file rollback packet under
`%SEV_LOCAL_DATA%\LibreHardwareMonitor\launcher-convergence`.
The newest five packets are retained, while the packet referenced by the
active receipt is protected during cleanup. Non-live fixture mode accepts only
explicit paths below the OS temporary root.

### SevLocal env-var current roots — 2026-09-01

After the machine SevLocal cutover, current Data/Bin production paths are no
longer drive-letter constants. Deploy scripts resolve `SEV_LOCAL_DATA` and
`SEV_LOCAL_BIN` from persisted User, then Machine, then Process scope, and
join `LibreHardwareMonitor` / `librehw.cmd` / `runw\runw.exe`. Inherited
Process `sqdata` / `sqbin` are not authority because a long-lived agent can
keep pre-cutover values. Canonical `librehw.cmd` stores
`%SEV_LOCAL_BIN%\runw\runw.exe`. The app still requires an expanded absolute
`dataRoot` in `librehw.runtime.json`.

Since the 2026-09-01 DevHome storage-role work, the persisted values are
`REG_EXPAND_SZ` templates chained through other persisted variables
(`SEV_LOCAL_DATA = %SEV_LOCAL_ROOT%\Data`,
`SEV_LOCAL_ROOT = %MACHINE_TOOLS_ROOT%SevLocal`), and
`[Environment]::GetEnvironmentVariable` returns User/Machine values
unexpanded. `Get-LhmPersistedEnvironmentValue` and the canonical launcher
therefore expand persisted `%...%` references recursively against the same
User, then Machine, then Process order. Token values substitute verbatim so a
trailing separator in a drive root such as `E:\` survives; unresolvable or
cyclic references fail closed. `Get-LhmRequiredEnvironmentRoot` additionally
requires the resolved root to be an absolute path to an existing directory.

A 2026-09-01 identity-gated pointer
repair retargeted the live runtime JSON and installed launcher; recovery
copies are under
`%SEV_LOCAL_DATA%\LibreHardwareMonitor\release-recovery\sevlocal-env-pointer-20260901`.
The same day's RunW root relocation wrote
`%SEV_LOCAL_DATA%\RunW\install-receipt-v2-1.3.1-b5cda6d-relocated.json`
(`Operation=Relocated`) without changing launcher bytes or alias targets.
Launcher convergence accepts Applied or Relocated RunW v2 receipts. The same
session's identity-verified Apply wrote the canonical public shim
(`%SEV_LOCAL_BIN%\runw\runw.exe`) and a current
`sq.librehw.launcher-convergence.v2` receipt. Validate then returned PASS.

### Data-root relocation compatibility island — 2026-08-15

The pre-activation handoff keeps the install and public-launch chain fixed while
changing only authoritative mutable-data selection:

```text
E:\SQ_HQ\u-programs\bin\librehw.cmd
  -> E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1
  -> \SevGrp\AdminTask\LibreHW-No-UAC
  -> E:\SQ_HQ\Monitoring\LibreHW\LibreHardwareMonitor.Windows.Forms.exe

Authoritative data -> E:\Data\LibreHardwareMonitor
```

The mutable payload has already been moved normally to the new data root and
the previous `E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor` root is absent.
The live runtime descriptor now selects `E:\Data\LibreHardwareMonitor`; the
existing managed task is enabled and running from the retained stable install
root. The public shim and pre-stable recovery packet remain byte-identical.

`Relocate-LibreHardwareMonitorDataRoot.ps1` is the single production transition
entry point. It requires verified `snd-desk` identity, the exact three roots and
compatibility launcher/shim paths, normal non-reparse boundaries, an existing
installed release, the exact managed-task contract, and the existing
manifest-only pre-stable packet. First convergence requires the task disabled;
validated recovery and already-converged states are resumable. Before changing
anything it atomically
persists a bounded packet under
`E:\Data\LibreHardwareMonitor\release-recovery\data-root-relocation` containing
only the old runtime config, launcher, task definition, and recovery manifest.
It never copies application or data payloads and never rewrites or removes the
pre-stable packet.

Runtime config and canonical launcher deployment use same-directory temporary
files plus rename/readback. Only after config, launcher, shim, task, and recovery
readback does the script enable and start the existing task. Acceptance then
requires one exact installed process, the new data-root descriptor, and HTTP
health. A pre-activation failure leaves the task disabled and the recovery
packet intact; a rerun resumes without rewriting that packet. The public shim
hash must remain unchanged throughout.

### Runtime-root retirement — 2026-08-16

`Move-LibreHardwareMonitorRuntimeRoot.ps1` is the only production entry point
for retiring the compatibility runtime and launcher roots. Production paths are
fixed to:

```text
E:\SQ_HQ\Monitoring\LibreHW                    old runtime
E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1
                                                   old launcher
E:\Monitoring\LibreHW\Runtime                  new runtime
E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1
                                                   new launcher
E:\Bin\librehw.cmd                              public command
```

The operation requires exact installed DevMesh v2 identity, the accepted
legacy runtime/launcher/shim hashes, the closed runtime inventory, runtime data
descriptor, and managed-task contract. It snapshots the old launcher, shim,
and task plus a bounded manifest under
`E:\Data\LibreHardwareMonitor\release-recovery\runtime-root-relocation`, copies
and verifies the runtime in a same-parent staging directory, stops only the
exact old process, rebinds the launcher/shim/task, and requires one exact new
process plus HTTP health. Pre-activation failure restores the task and shim,
removes the new target, and restarts the old runtime. Only accepted activation
permits deletion of the two old leaves and their now-empty compatibility
parents.

The non-live suite covers a complete move and an injected post-binding failure
with byte-for-byte task/shim rollback. `Plan` is report-only, `Apply` is the
guarded transition, and `Validate` proves the final path/task/process contract.
That one-time historical migration generator remains evidence of the then-
accepted sibling app-vendored HideLaunch transition and still fails closed on
its own authority contract. It is not current public-shim generator authority
and cannot regenerate the central-RunW shim; continuing convergence belongs to
`Sync-LibreHardwareMonitorLauncher.ps1`.

## Package decision

The selected local package is:

- target framework: `net10.0-windows`;
- runtime: `win-x64`;
- framework-dependent;
- single-file;
- not trimmed;
- native libraries included for self-extraction;
- no legacy binding-redirect sidecar for net10;
- no XML documentation sidecar;
- no PDB in the installed payload.

Isolated probes produced:

| Package | Files | Bytes |
|---|---:|---:|
| Framework-dependent multi-file | 33 | 11,219,682 |
| Framework-dependent single-file, default | 5 | 11,268,827 |
| Framework-dependent single-file, selected settings | 1 | 10,787,318 |
| Self-contained multi-file | 487 | 189,842,439 |
| Self-contained single-file, selected settings | 1 | 183,097,837 |

The one-file probe used:

```powershell
dotnet publish LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj `
  -c Release -f net10.0-windows -r win-x64 -p:Platform=x64 `
  --self-contained false -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:AutoGenerateBindingRedirects=false `
  -p:GenerateDocumentationFile=false `
  -p:PublishTrimmed=false `
  -p:DebugType=None -p:DebugSymbols=false `
  -p:OutputPath=<isolated-build>\ `
  --artifacts-path <isolated-artifacts> `
  -o <candidate>
```

An explicit `OutputPath` is required because the project overrides normal
output into the repository's shared `bin` tree.

## Source behavior

### Single-file startup

- Retain the user-friendly physical companion-file check for net472.
- Do not require physical managed DLLs for modern .NET single-file publish;
  assembly loading is the authority.
- Keep trimming disabled until a separately evidenced trimming contract exists.

### Data root

- Resolve and create the selected data and log directories before loading
  settings or starting logging.
- Preserve the existing settings filename and atomic backup/write semantics.
- A missing explicit/machine data root falls back to executable-adjacent paths.
- Log-management tooling receives the selected `logs` directory explicitly.
- PawnIO temporary extraction and relative gadget-image behavior must no longer
  depend on an arbitrary shell working directory.

### Startup and single-instance ownership

- `\SevGrp\AdminTask\LibreHW-No-UAC` is the sole managed elevated start owner on
  `snd-desk`; it supports the intended logon and on-demand starts.
- The task action and working directory name the stable runtime.
- A non-zero process exit receives at most three restart attempts at one-minute
  intervals. This restores monitoring after a transient native-provider crash
  without creating an unbounded restart loop.
- The app must not recreate a scheduler-root task under this managed install.
- The duplicate root `\LibreHardwareMonitor` task is retired only after the
  managed task and logon behavior pass.
- Release acceptance proves one PID and the exact installed process path.

## Build and manifest contract

The source-controlled publish entry point:

1. refuses a dirty source release by default;
2. runs the repository's .NET tests and both x64 Release target builds;
3. publishes into an isolated temporary build/artifact root, never the live
   directory;
4. verifies that the candidate contains exactly one EXE;
5. reads the built file/product version and commit;
6. calculates SHA-256;
7. emits a bounded release manifest with schema, release ID, commit, version,
   framework, runtime, self-contained flag, timestamp, and hash.

The manifest never authorizes deleting files outside the fixed current and
rollback payload slots.

### Stage one local test release

Review and commit the intended source first. Invoke the publisher once with a
new external candidate directory, for example:

```powershell
.\ops\deploy\snd-desk\Publish-LibreHardwareMonitor.ps1 `
  -CandidateDirectory 'E:\Monitoring\LibreHW\Candidates\local-test-<commit>'
```

The publisher owns the three test suites, both compatibility build gates, and
the single-file publish. Do not duplicate those gates with preliminary builds.
Tool discovery selects the first `git` and `dotnet` application on PATH, so
duplicate application paths do not become an invalid command array.
It retains one candidate EXE plus `release.json` and removes its temporary
build outputs. One release invocation still performs those required internal
compilations; no skip-build or skip-verification switch exists.

Validate the manifest/hash and exact two-file candidate shape with
`Read-LhmReleasePayload -RequireCandidateShape` from
`LhmLocalRelease.Common.ps1`. The manifest commit must equal the clean source
commit used for publication. Installer preflight can then run without promotion:

```powershell
.\ops\deploy\snd-desk\Install-LibreHardwareMonitorRelease.ps1 `
  -CandidateDirectory 'E:\Monitoring\LibreHW\Candidates\local-test-<commit>' `
  -AllowStopExactProcess -WhatIf
```

`-WhatIf` leaves the running application and installed payloads intact.
Launch through `librehw` only after a separately authorized guarded promotion;
do not launch a candidate directly into the active hardware/settings session.
The runtime and its one rollback slot remain separate from staged candidates.

Verification on 2026-09-11: independent review accepted the staging commands
and scalar tool-discovery fix. PowerShell 7.6.6 and Windows PowerShell 5.1 both
parsed the publisher without errors, selected one Git application from two
PATH matches, and successfully invoked the selected Git and .NET SDK. The
guarded repository cleanup removed 1,643 generated files (1,427,399,207 bytes)
from 16 output roots; runtime and rollback payload hashes remained valid.

## Promotion and rollback contract

- Before a machine mutation, run
  `Get-VerifiedMachineIdentity.ps1` and require `VERIFIED` for `snd-desk`.
- Preflight the .NET 10 Windows Desktop runtime, candidate manifest/hash,
  install/data roots, task namespace, and exact process identity.
- First migration requires an explicit Debug-or-Release config source. Copy it
  to a temporary file under the data root, verify length/hash, then rename it
  into place. Never infer authority from modification time.
- Stage the candidate on the same volume as the install root.
- Persist and flush a bounded transaction journal before replacing current or
  rollback payloads. An interrupted operation repairs from that journal before
  accepting another promotion.
- Keep launcher/task snapshots durable in the transaction journal before the
  first launcher/task mutation. On first install, after activation and health
  acceptance, copy those snapshots into a separately bounded, strictly
  validated pre-stable recovery packet before committing/removing the
  transaction journal. A pre-packet interruption repairs from the journal.
  Recovery manifests never authorize paths, task XML, launchers, or shortcuts
  outside their source-pinned contracts.
- By default, refuse activation while Libre Hardware Monitor is running.
  Any active-stop mode targets only the confirmed exact process.
- Copy the journaled current EXE/manifest into `rollback`, then copy and verify
  the staged candidate/manifest in the fixed current slots. The durable journal
  repairs any interruption across the multi-file replacement.
- Start through the managed task and verify health.
- On failed activation, stop only the exact failed candidate, restore the
  rollback pair, restart, and verify rollback health.
- A later successful promotion replaces the one old rollback pair; no older
  package is retained.
- Old build-directory config/log cleanup was a separate approved operation and
  is recorded in `docs/architecture/repository-build-output-cleanup.md`.

## First-install proof — 2026-07-25

The first install selected the existing Release config explicitly because it
has the dashboard, logging, listener, and start-minimized settings needed by
the health contract. The smaller Debug config does not enable the web server.

- Release:
  `0.9.6_fd4d238-dirty.2026-07-25-fd4d23854fb5-20260725T163201Z`
- Installed EXE SHA-256:
  `597dd221c58684ff3c25ba79233e0e4856521bb2a421451faf83b2702d979cd3`
- Config source SHA-256:
  `68d83c865af307d558d09730ab469ae5fd5205c0352a06c68d25ff49dd841496`
- One exact stable process passed HTTP `data.json` with status 200 and a
  populated `SND-DESK` hardware tree.
- The native window was responding and exposed populated tree/menu/scrollbar
  controls through UI Automation.
- A new CSV appeared under the then-selected `sqdata` log directory.
- The 3,361 existing Release-tree CSV files (9,058,052,916 bytes) remained
  untouched during installation. The later accepted cleanup moved them,
  unchanged and collision-isolated, under
  `E:\Data\LibreHardwareMonitor\historical\pre-stable-repo-build-output-2026-07-25`.
- The public command still resolves to the unchanged `librehw.cmd`; an
  elevated invocation restored the existing stable process without creating a
  duplicate.
- The final cleanup gate caught and fixed a Windows PowerShell 5.1
  scalar-`Count` incompatibility in the delegated launcher's existing-process
  enumeration. The non-live suite now executes `-ValidateScriptOnly` through
  the same Windows PowerShell host used by `librehw.cmd`.
- The pre-stable launcher/task recovery packet validated successfully before
  cleanup. It is now historical audit evidence because its Debug/Release
  payloads were intentionally removed.
- The normal-user `librehw.cmd` foreground check was accepted. Both Start Menu
  shortcuts now target the unchanged public command, the exact duplicate
  scheduler-root task is absent, and the managed task is the sole startup
  owner.
- Both bounded recovery packets validated after finalization. Their old startup
  restore entry points now fail closed before mutation; installed stable
  rollback remains supported. No launcher helper remains after foreground
  restoration.

## Zero-process launcher follow-up — 2026-07-28

An identity-verified `snd-desk` re-smoke found that Windows PowerShell 5.1
StrictMode rejected `$entries.Process` when no Libre Hardware Monitor process
existed. That prevented the public wrapper from reaching the managed task in
the exact absent-process state it is meant to handle.

- The launcher now pipeline-projects process entries, so an empty query returns
  an empty array without a StrictMode member-enumeration failure.
- The release suite now forces an empty `Get-Process` result under Windows
  PowerShell 5.1 and requires `DetectedProcessCount : 0`.
- `-ValidateScriptOnly` inspects but does not enforce live executable ownership,
  so source verification remains non-mutating and can run on a peer whose own
  LibreHardwareMonitor process uses a different approved path. Normal launcher
  execution still fails closed on any path mismatch.
- Source and deployed launcher SHA-256 both are
  `068381618170282fdde593d41ed01d24d983d356d6841bb70780a07e0052be84`.
- Windows PowerShell 5.1 and PowerShell 7 `-ValidateScriptOnly` checks passed.
- Invoking the PATH-resolved `librehw` with no process started exact stable PID
  `46608` through `\SevGrp\AdminTask\LibreHW-No-UAC`; a repeated invocation
  retained that PID and foregrounded its visible window without a helper or
  duplicate.
- The live UI exposed 19 UI Automation descendants, the named `treeView` pane,
  two scrollbars, and the main menu. `data.json` returned HTTP 200.
- The same responding PID remained healthy after a 232-second dwell with no
  new Application 1000/1026 crash event. This is a launcher smoke, not closure
  of separate long-duration native GPU polling stability.

## Cross-host source verification — 2026-07-30

SND-HOST imported this SND-DESK-only workflow without touching production or
live paths. The fixture exercised install, restore, and cleanup behavior only
against isolated temporary roots in non-live mode; it did not publish a release
or invoke production task actions.

- `-ValidateScriptOnly` detected the existing SND-HOST process through its
  inspection-only path and did not reject that peer-approved executable.
- The fixture passed 12/12 failure-injection cases, 16/16 hostile recovery
  manifests, 12/12 hostile reparse cases, its Windows PowerShell 5.1 launcher
  checks, and cleanup compatibility.
- Non-elevated junction fixtures cover the same fail-before-touch reparse guards
  without requiring symbolic-link privilege.
- No SND-DESK path, task, launcher, configuration, or runtime state was
  materialized on SND-HOST.

## Managed-task restoration — 2026-08-03

The app was found stopped with `\SevGrp\AdminTask\LibreHW-No-UAC` entirely
absent (the whole `AdminTask` folder was gone). The last pre-gap CSV write was
2026-08-01 20:29; no register/update/delete events for the task appeared in the
last 400 Task Scheduler registration events, so the removal cause is
undetermined.

- Preflight passed before any mutation: identity `VERIFIED`/`snd-desk`,
  installed payload hash and manifest, runtime config, public shim hash, and
  deployed-vs-repo launcher hash all matched this contract.
- `Register-LhmManagedTask` recreated the task under one attended UAC consent:
  single Exec on the stable EXE/working directory, one `MSFT_TaskLogonTrigger`,
  Interactive/Highest principal, `IgnoreNew`, `StartWhenAvailable`, no hard
  terminate, `PT0S`.
- The unchanged shim/launcher chain then started one process through the task
  (last result `0x41301` running). `/`, `/data.json`, and `/metrics` all
  returned HTTP 200; `/data.json` retained the expected `Sensor` envelope. A
  new `LibreHardwareMonitorLog-2026-08-03.csv` appeared under the then-selected
  `sqdata` logs
  root. A repeated shim invocation kept the same PID with no duplicate.
- The separate `hardware-optimization` health-feed task was also absent from
  Task Scheduler on this date. Its definition lives outside this repository;
  the owning package must decide whether to recreate it against the dedicated
  `E:\Data\LibreHardwareMonitor\logs` directory or retire it. It was not
  recreated here.

## Rebuilt-profile deployment — 2026-08-14

An identity-verified `snd-desk` deployment started with no installed runtime,
launcher, managed task, or prior Libre Hardware Monitor data root. The source
was fast-forwarded from `librehw-host/main`; the local release workflow then
needed two bounded fixes before it could safely bootstrap that clean state.

- The machine verifier authority now uses the enrolled
  `OneDrive\Common\common_development\common_dev` path in both the installer
  and deployed launcher.
- First-install recovery accepts both legacy startup owners being absent and
  writes a manifest-only packet. It still rejects either asymmetric state.
  The non-live release suite covers both the ownerless success and asymmetric
  rejection.
- The final published and installed release is
  `0.9.6_e977e57.2026-08-14-e977e577ea13-20260814T205711Z`; its installed EXE
  SHA-256 is
  `e2ac66b3791dba60d77d297708056d5716e44ec596d63f1b778e950352f0fca8`.
- One exact-path process remained at the stable shallow runtime after repeated
  direct activation. The native window was visible and responsive
  with the named `treeView` pane, five menu items, and two scrollbars.
- `/`, `/data.json`, and `/metrics` returned HTTP 200. The data payload kept
  its `Sensor` envelope, metrics exposed 670 lines, and the new 1-second CSV
  grew from 142,535 to 149,240 bytes during the acceptance sample.
- The public `librehw.cmd` retained its required hash and used the app-owned
  launcher for a tray-hidden process; that path returned exit 0, restored the
  same PID, and created no duplicate.
- The canonical all-target gate passed 9/9 runnable targets. This included 370
  deterministic .NET tests passing with one intentional skip, both shipping
  framework builds, 75/75 Avalonia spike tests, dashboard checks, and the
  SND-DESK local-release fixture.

## Runtime-root retirement acceptance — 2026-08-16

- Source commit `9de2314dbe1a770153f6d254f3c3b01a96d4533f` passed the
  complete non-live local-release suite, including two runtime-root migration
  cases, and the canonical launcher passed `-ValidateScriptOnly` in PowerShell
  7.6.5 and Windows PowerShell 5.1.
- The installed DevMesh v2 verifier returned exactly one `VERIFIED` identity for
  `snd-desk` instance `ca96d510-7d87-4cec-8e1a-bd8fc3866903` before mutation.
  Live `Apply` and an independent `Validate` then returned `PASS`.
- One exact process runs from
  `E:\Monitoring\LibreHW\Runtime\LibreHardwareMonitor.Windows.Forms.exe`; the
  managed task uses that executable and working directory with
  Interactive/Highest, `IgnoreNew`, and `PT0S`. `data.json` returned HTTP 200
  and a new `LibreHardwareMonitorLog-2026-08-16-4.csv` grew under the dedicated
  data root.
- Repeated direct `E:\Bin\librehw.cmd` calls retained one PID and restored the
  visible `Libre Hardware Monitor - Sev IQ` window. Fresh
  PowerShell 7 and 5.1 sessions both resolve `librehw` to `E:\Bin\librehw.cmd`.
- The launcher authority uses the app-owned launcher directory and exact
  migrated executable. Tasks, services, running
  process paths, User/Machine environment, registry startup values, shortcuts,
  and managed live-file text contain no executable binding to `E:\SQ_HQ` or
  `E:\UserProfile`. The controlling Codex process retains one inherited stale
  pre-cutover PATH entry, which is not persistent and is absent from fresh
  shells.
- Both legacy roots are absent. The exact pre-migration launcher, shim, task,
  release ID, and hashes remain in the bounded recovery packet at
  `E:\Data\LibreHardwareMonitor\release-recovery\runtime-root-relocation`.

## Bounded crash-recovery hardening — 2026-08-22

- Libre Hardware Monitor and HWiNFO both terminated during an NVIDIA
  driver/NVML incident. Libre's Application 1026 stack ended in
  `NvmlDeviceGetPowerUsage`, its task returned `0xC0000005`, and the logon-only
  task left monitoring and HTTP down after the crash.
- Identity was reverified as `snd-desk` instance
  `ca96d510-7d87-4cec-8e1a-bd8fc3866903`. The installed executable still
  matched release `0.9.6_e977e57.2026-08-14-e977e577ea13-20260814T205711Z`
  and SHA-256
  `e2ac66b3791dba60d77d297708056d5716e44ec596d63f1b778e950352f0fca8`
  before task mutation.
- The exact managed task was re-registered with three one-minute restart
  attempts, `IgnoreNew`, no hard termination, and the unchanged executable and
  working directory. Its prior XML is retained at
  `E:\Data\LibreHardwareMonitor\release-recovery\managed-task-hardening-2026-08-22`.
- Exact stable PID `56392` then returned HTTP 200. `data.json` reached 148,829
  bytes with the `Sensor` root and `/metrics` reached 604 populated lines. No
  new Libre crash event appeared during the acceptance dwell.
- No firewall rule was added. HTTP.sys currently registers `HTTP://+:8085/`
  and authentication remains disabled, so intentional remote access is still
  gated on a separately selected listener/authentication/firewall boundary.
  The source fail-closed listener change is not present in this installed
  2026-08-14 binary and requires a separately authorized promotion.

## Wait-capable launcher convergence — updated 2026-08-31

- `ops/deploy/snd-desk/librehw.cmd` is now the canonical public-shim source.
  Its normalized expected deployment hash is
  `13a96605268b43dbf1052e837f15557ff655165c1e31ea936a1d30f452dbaee3`;
  the shared release validators and convergence tool use the same bytes. The
  historical runtime-migration generator remains intentionally separate.
- The convergence tool trusts only the installed
  `E:\Bin\runw\runw.exe`. Its RunW v2 authority receipt must self-bind to
  `E:\Data\RunW\install-receipt-v2-1.3.1-b5cda6d.json`, contain exactly one destination row
  for that canonical path, and match the destination hash plus artifact hash
  and length to the installed bytes. It also records the exact clean
  `b5cda6d` source commit, source root, artifact path, and version 1.3.1;
  recorded historical paths need not remain present. Mutable build output and
  source HEAD are not continuing launcher authority.
- Immutable legacy v2 receipts remain compatible only through their exact
  two-field destination row; current four-field rows require strict Boolean
  existence and conditional pre-state hash evidence. Other row shapes block.
- The convergence receipt is `sq.librehw.launcher-convergence.v2`. It records
  the central launcher path/hash and authority-receipt path/hash, while its
  managed targets and rollback manifest contain only the app relay and public
  CMD. A structurally valid v1 app-vendored receipt is recognized only as
  migration input: Plan reports drift and proposes v2, and it is never current.
- Fixture Apply is identity-gated before writes, stages and hashes both owned
  files, retains displaced files, and restores from a fixed in-memory
  plan after injected failures following each owned file and the receipt.
  Receipt validation rejects source, central-authority, managed-task,
  target-set, rollback ancestry, manifest, and backup tampering. Original and
  rollback failures are reported separately, retention is bounded, and both
  the managed-task fixture and central RunW bytes remain unchanged.
- The focused 25-case launcher-convergence suite passed in PowerShell 7 and
  Windows PowerShell 5.1. It uses only isolated OS-temp fixture roots.
- The 2026-08-31 live Plan was non-mutating: central RunW, its receipt, and the
  managed task were current with no blocker; the public shim and convergence
  receipt remained drifted. No LibreHW Apply ran. Identity-verified Apply,
  Validate, and attended absent/running/tray-hidden foreground smokes remain
  the promotion gate. The app relay retains its start-if-closed and tray
  restore/foreground behavior; the CMD does not use generic `/focus`.

## Historical live launcher Plan recheck — 2026-08-27

- The installed verifier returned `VERIFIED` for `snd-desk` instance
  `ca96d510-7d87-4cec-8e1a-bd8fc3866903`. One process still runs the accepted
  `e977e577` executable, and `/`, `/data.json`, and `/metrics` returned HTTP 200.
- The managed task still has the accepted action, working directory, and
  `IgnoreNew`, but its exported XML has no `RestartOnFailure`; the observed
  restart count is zero. The bounded three-attempt/PT1M contract has drifted.
- The then-current v1 `Sync-LibreHardwareMonitorLauncher.ps1 -Mode Plan` was
  non-mutating and returned `DRIFT`. Its blocking issues were the missing
  authority-pinned
  `D:\Development\System\launch-hidden-shim\bin\runw.exe` artifact and
  the managed-task contract mismatch. The runtime HideLaunch file is absent,
  the public shim is still the old asynchronous form, and the convergence
  receipt is not current.
- This evidence predates the v2 central-launcher implementation above. Current
  source no longer consumes the missing mutable build artifact or source HEAD,
  and the observed task drift remains relevant until an authorized recheck.

## Central-RunW v2 read-only Plan — 2026-08-28

- The source `Plan` performed no mutation, confirmed the installed central
  RunW and its authority receipt, reported the public-shim drift, recognized
  the existing exact v1 app-vendored receipt as migration input, and proposed
  `sq.librehw.launcher-convergence.v2`.
- Scheduled-task inspection returned `Access denied` in the current shell, so
  the Plan remained blocked and did not supersede the earlier task-contract
  finding. No Apply, file deployment, task change, or process action ran.

## Acceptance

- [x] `Get-Command librehw` resolves `%SEV_LOCAL_BIN%\librehw.cmd` in fresh
  PowerShell 7 and Windows PowerShell 5.1 sessions.
- [x] Publish emits exactly one framework-dependent x64 EXE and a separate
  bounded manifest.
- [x] The installed process path is the fixed shallow path.
- [x] Debug/Release builds and tests do not write into the installed runtime.
- [x] Config load/save and the generated backup stay under the selected
  data root.
- [x] New CSV logs appear only under the selected `logs` root.
- [x] Launcher promotion/repair and persistent settings/log writes reject
  reparse-point ancestors or leaves before touching an external target.
- [x] An explicit current config migrates with verified length/hash and retains
  listener, dashboard, logging, UI, and sensor settings.
- [x] A rebuilt-profile first install may record both legacy startup owners as
  absent; an asymmetric packet with only the launcher or only the task still
  fails closed.
- [x] `\SevGrp\AdminTask\LibreHW-No-UAC` owns on-demand and intended logon start,
  with the stable action/working directory.
- [x] The Start Menu/Desktop shortcut scan contains no legacy-root binding;
  public command entry points route through `%SEV_LOCAL_BIN%` rather than a
  repository `bin` tree.
- [x] The duplicate scheduler-root task is absent after accepted cutover.
- [x] Direct task, normal-user `librehw.cmd`, and repeated launcher calls
  converge on one process.
- [x] The managed-task source contract uses bounded restart-on-failure: three
  attempts at one-minute intervals, while `IgnoreNew` prevents duplicates.
- [ ] The live managed task matches that restart contract. The 2026-08-27
  read-only recheck observed restart count zero and no `RestartOnFailure` XML.
- [x] Canonical `librehw.cmd` uses the exact installed
  `%SEV_LOCAL_BIN%\runw\runw.exe` with `/wait /quiet /cwd:-`, pins the System32
  Windows PowerShell 5.1 host, forwards arguments, propagates non-zero status,
  and contains no asynchronous `start`, generic `/focus`, ambient host lookup,
  or recursive public-command dispatch.
- [x] Launcher convergence validates the central RunW receipt self path, exact
  destination row, hash, length, and installed bytes without mutable artifact
  or source-HEAD coupling; v1 receipts are migration input only.
- [x] Non-live launcher convergence covers drift reporting, identity-before-
  write, exact two-file deployment, task/central-launcher immutability,
  retained rollback, and restoration after an injected partial deployment in
  PowerShell 7 and Windows PowerShell 5.1.
- [ ] Identity-verified live Apply and Validate have deployed the new launcher
  chain and attended absent/running/tray-hidden smokes have accepted foreground
  behavior and status propagation.
- [x] Existing-window restore shows populated child controls and obtains
  foreground ownership from a normal unelevated shell without a lingering
  helper.
- [x] The actual Windows PowerShell 5.1 host used by `librehw.cmd` passes the
  process-enumeration compatibility check and restores a genuinely tray-hidden
  stable window through the public command.
- [x] Local `data.json` responds with HTTP 200 and valid JSON. Release identity
  is proven separately by the one exact installed process path plus the
  installed EXE hash and file/product version matching `release.json`;
  `data.json` retains its stable API compatibility version.
- [x] A deliberately invalid candidate cannot replace current, and a failed
  post-start check restores the last-known-good release.
- [x] No existing CSV log is deleted as part of release installation.
- [x] The later accepted cleanup preserves all 3,361 historical CSVs and three
  old config files outside the repo, then removes every ignored repo-local
  `bin`/`obj` tree and all 32 non-authoritative EXEs.
- [x] The non-live relocation fixture covers exact-path refusal, old-root
  presence refusal, junction refusal, bounded recovery validation and tamper
  rejection, pre-activation task disablement, resumability, and a byte-stable
  idempotent Windows PowerShell 5.1 rerun.
- [x] Production relocation readback proved the unchanged public shim, canonical
  compatibility launcher, enabled existing task, one exact stable process using
  `E:\Data\LibreHardwareMonitor`, HTTP 200 health, and a fresh CSV write. The
  deployed/canonical launcher SHA-256 was
  `8C71D3167F8DC212E9BF282F086F848CE0C80BC6BFDCFCDA3F15448EE7061096`;
  the public shim (`FE319AAB...0D73`) and immutable pre-stable recovery manifest
  (`3A9366E8...CE04`) were unchanged. The relocation recovery directory contains
  exactly the four bounded config/launcher/task/manifest files.
- [x] Runtime-root migration moved the verified payload and launcher into
  `E:\Monitoring\LibreHW`, rebound the task and launcher metadata, preserved the
  release/data contract, and removed `E:\SQ_HQ` and `E:\UserProfile` only after
  exact-process, HTTP, logging, and launcher acceptance passed.

## Verification

Repository gate:

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Release gate:

- inspect candidate count, version, size, manifest, and SHA-256;
- test data-root selection and executable-adjacent fallback in temporary roots;
- run a single-file launch smoke without touching the live install;
- run installer and rollback integration tests against temporary install/data
  roots;
- run the relocation entry point against temporary roots, including a
  Windows PowerShell 5.1 idempotence pass, recovery tamper, reparse, and injected
  pre-activation failure/resume cases;
- cover an ownerless first install and require its bounded recovery packet to
  contain only the manifest with both legacy owners recorded absent;
- preserve external sentinels across hostile launcher, payload, rollback,
  transaction, settings, log, cleanup, and recovery reparse cases;
- verify `-WhatIf` changes no file, process, task, shortcut, or runtime state.

Operator workflow:

```powershell
# Non-live parser, failure-injection, hostile-input, and rollback checks
.\ops\deploy\snd-desk\Test-LhmLocalRelease.ps1

# Focused launcher convergence checks in the current host
.\ops\deploy\snd-desk\Test-LhmLocalRelease.ps1 -LauncherConvergenceOnly

# Report launcher drift; Apply remains a separately attended live gate
.\ops\deploy\snd-desk\Sync-LibreHardwareMonitorLauncher.ps1 -Mode Plan
.\ops\deploy\snd-desk\Sync-LibreHardwareMonitorLauncher.ps1 -Mode Validate

# After QA: relocate only runtime authority, launcher content, and existing task
.\ops\deploy\snd-desk\Relocate-LibreHardwareMonitorDataRoot.ps1 `
  -DataMoveAlreadyCompleted -Confirm:$false

# Publish an isolated candidate; omit -AllowDirty for normal committed releases
.\ops\deploy\snd-desk\Publish-LibreHardwareMonitor.ps1 `
  -CandidateDirectory E:\path\to\candidate

# Preview, then perform promotion
.\ops\deploy\snd-desk\Install-LibreHardwareMonitorRelease.ps1 `
  -CandidateDirectory E:\path\to\candidate `
  -InitialConfigSource E:\path\to\LibreHardwareMonitor.Windows.Forms.config `
  -WhatIf
.\ops\deploy\snd-desk\Install-LibreHardwareMonitorRelease.ps1 `
  -CandidateDirectory E:\path\to\candidate `
  -InitialConfigSource E:\path\to\LibreHardwareMonitor.Windows.Forms.config `
  -Confirm:$false

# Swap current and the one rollback payload
.\ops\deploy\snd-desk\Restore-LibreHardwareMonitorRelease.ps1 -WhatIf

# After attended UI and normal-user launcher acceptance
.\ops\deploy\snd-desk\Finalize-LibreHardwareMonitorCutover.ps1 `
  -AttendedUiAccepted -NormalUserLauncherAccepted -WhatIf
```

The accepted repository-output cleanup permanently retires first-migration
recovery to the old Debug/Release payloads. The two old startup-recovery
entry points remain only as fail-closed tombstones and throw before any
launcher, task, or shortcut mutation. Use
`Restore-LibreHardwareMonitorRelease.ps1` for bounded installed-release
rollback.

Identity-verified cutover gate:

- prove the exact current process and both task actions before mutation;
- promote the candidate and inspect the installed manifest/hash;
- prove one exact-path process, managed task ownership, settings persistence,
  new log location, populated UI restoration, and HTTP health;
- retain old build/runtime evidence until the new release passes.

Source verification on 2026-09-01 (resolver expansion and Relocated RunW
receipt; not a promotion):

- `Test-LhmLocalRelease.ps1` PASS, including Windows PowerShell 5.1 launcher,
  data-root relocation, and cleanup compatibility.
- `Test-LhmReleaseSystem.ps1` PASS, 144 assertions.
- Deterministic .NET suites: 384 passed, one established live-config test
  skipped, zero failed; both x64 Release target frameworks 0W/0E.

## Launcher alignment — 2026-09-11

The installed RunW 1.3.1 binary now matches the September 4 v2 receipt for
source commit `64472d33d43aeac0493781f1739c8ebe7ae1165b`, SHA-256
`422F4534350A8174712345C972C5618F320A7FDDFFED5BD46368A91EBCDA9E96`,
and length 316928. The prior `b5cda6d` pin rejects those installed bytes.
Convergence adopts this exact receipt and artifact identity while retaining
all destination, schema, clean-source, identity, and hash checks. It does not
update RunW itself or infer authority from RunW's current working tree.

The public shim retains its existing helper path token
`%MACHINE_TOOLS_ROOT%\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1`.
The System32 host, wait/exit behavior, and fixed SND-DESK operation boundaries
remain unchanged. Fixture mode continues to use explicit temporary paths.
The app-owned helper receives the already-implemented recursive persisted
environment-variable resolver so chained Data/Bin values resolve correctly.

Acceptance and verification:

- [x] Focused launcher fixtures pass in PowerShell 7 and Windows PowerShell 5.1;
  the complete local-release fixture passes with the updated pins and shim.
- [x] Plan verifies the current RunW receipt and managed task with no blockers.
- [x] Identity-verified Apply and Validate converge the helper, shim, and receipt;
  installed RunW, application executable, and managed task remain unchanged.
- [x] The public launcher forwards `-ValidateScriptOnly` successfully and normal
  activation retains the exact installed PID with a populated native window.

Verified on controller/target `snd-desk`, installation
`ca96d510-7d87-4cec-8e1a-bd8fc3866903`, at 2026-09-10 23:02 UTC:
Apply and Validate returned `PASS`, no drift or blockers, and all five
current-state checks true. The full fixture passed with 25 launcher cases,
12 injected failures, 16 hostile manifests, and 12 hostile-reparse cases.
Runtime executable, RunW, public shim, and exported managed-task XML hashes
matched the pre-apply baseline. Public validation and activation exited zero
from the elevated controller shell; the existing PID `15072` was retained
and its native window changed from hidden to visible (`MainWindowHandle=66534`).
This does not add an unelevated-shell acceptance claim.

### Graph-lane release promotion — 2026-09-11

- Clean source `96a3e629087200b3fcfacdd7de8b99536228e84f` produced candidate
  `0.9.6_96a3e62.2026-09-11-96a3e6290872-20260910T230940Z` under
  `E:\Monitoring\LibreHW\Candidates\graph-lanes-20260911-2310`.
  The publisher's duplicate Git PATH discovery was avoided by removing the
  redundant Git `bin` entry only in its child process; persisted PATH and
  publisher source were unchanged.
- Candidate gates passed: Library 93, Application 195 plus one established
  skip, Contracts 109; both Release frameworks zero warnings/errors.
  Guarded installer WhatIf passed, followed by verified `snd-desk` promotion
  at 2026-09-10 23:10 UTC. Installed SHA-256:
  `cfeb25307a961b21bb2488822e79fa25170b9dc29db9432cfecf443f3e513771`.
- Independent checks confirmed one exact installed process, all three HTTP
  endpoints returning 200, continuing CSV growth, matching installed and
  rollback hashes, and launcher Validate PASS without drift.
- Rollback retains source `e977e577ea133fa6e9486f40f29b2266dcc22d70`,
  SHA-256 `e2ac66b3791dba60d77d297708056d5716e44ec596d63f1b778e950352f0fca8`.
  Initial graph-lane live tests found a manual-zoom startup regression, repaired
  by the subsequent promotion below. Promotion success is not operator sign-off.

### Startup zoom repair promotion — 2026-09-11

- User-authorized repair source `d65d2ae336820a5cc97541319f971f30bd682dfb`
  produced candidate `0.9.6_d65d2ae.2026-09-11-d65d2ae33682-20260910T234223Z`
  under `E:\Monitoring\LibreHW\Candidates\graph-lanes-zoom-fix-20260911`.
- Candidate gates: 401 passed, one established skip; both Release frameworks
  zero warnings/errors. Identity-verified WhatIf and promotion succeeded on
  `snd-desk` at 2026-09-10 23:43 UTC, using the existing data root and task.
- Installed SHA-256:
  `c7fb15fe9a26494db5cab2c0b9bdd1adcd3279c64df1ba84255a60fafa68bf10`.
  The one rollback slot now retains `96a3e629`, SHA-256
  `cfeb25307a961b21bb2488822e79fa25170b9dc29db9432cfecf443f3e513771`.
- Independent checks passed for installed/rollback hashes, exact process,
  HTTP health, CSV growth, and launcher convergence. Native clean-exit and
  restart checks preserved exact zoom bounds for both voltage lanes, plus
  membership, CPU 3x height, and Auto Range. See `feature-graph-lanes.md` for
  the numeric evidence and remaining operator sign-off boundary.

## Initial config decision

Resolved for the first install: use the then-current
`bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.config`
(30,554 bytes at cutover). Its verified settings enable the dashboard and
logging required by release health. The exact Release config/backup and the
7,051-byte Debug config are now preserved under the historical archive recorded
in `docs/architecture/repository-build-output-cleanup.md`.

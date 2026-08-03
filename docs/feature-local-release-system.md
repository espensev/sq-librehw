# Feature Spec: Local Release and Runtime Separation

**Status:** implemented, installed, attended-finalization verified, and
repository-output cleanup complete
**Updated:** 2026-07-28

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

- Keep `librehw.cmd` as the permanent public command, unchanged.
- Run one fixed, shallow installed EXE rather than a Debug/Release build output.
- Publish the local net10 x64 app as one framework-dependent EXE.
- Keep config, backups, and CSV logs under machine-local `sqdata`.
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
  later, separately accepted repository-output cleanup preserved it under
  machine-local `sqdata`.
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
E:\SQ_HQ\Monitoring\LibreHW\
├─ LibreHardwareMonitor.Windows.Forms.exe
├─ release.json
├─ librehw.runtime.json
└─ rollback\
   ├─ LibreHardwareMonitor.Windows.Forms.exe
   └─ release.json
```

The fixed process path is:

```text
E:\SQ_HQ\Monitoring\LibreHW\LibreHardwareMonitor.Windows.Forms.exe
```

There are no version-named live directories and no package cache. Promotion
retains only the current and immediately previous verified payload. The
`rollback` directory is empty after the first install and holds one verified
EXE/manifest pair after a later successful promotion.

### Mutable data

```text
E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor\
├─ LibreHardwareMonitor.Windows.Forms.config
├─ LibreHardwareMonitor.Windows.Forms.config.backup
└─ logs\
   └─ LibreHardwareMonitorLog-*.csv
```

The app resolves an explicit Libre Hardware Monitor data-root override first,
then `%sqdata%\LibreHardwareMonitor`, then falls back to executable-adjacent
storage for portable/upstream-compatible behavior.

### Public launcher

`E:\SQ_HQ\u-programs\bin\librehw.cmd` remains byte-for-byte unchanged. Its
delegated PowerShell script now targets the stable EXE and working directory.
Existing-process restore still uses the app-owned tray-toggle path. When no
process exists, elevated start routes through the managed no-UAC task.

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
  is recorded in `docs/repository-build-output-cleanup.md`.

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
- A new CSV appeared under the `sqdata` log directory.
- The 3,361 existing Release-tree CSV files (9,058,052,916 bytes) remained
  untouched during installation. The later accepted cleanup moved them,
  unchanged and collision-isolated, under
  `sqdata\LibreHardwareMonitor\historical\pre-stable-repo-build-output-2026-07-25`.
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
  (last result `0x41301` running). `/`, `/data.json`, and `/metrics` returned
  HTTP 200 with the `Sensor` envelope; a new `LibreHardwareMonitorLog-2026-08-03.csv`
  appeared under the `sqdata` logs root. A repeated shim invocation kept the
  same PID with no duplicate.
- The separate `hardware-optimization` health-feed task (see
  `docs/README.md` roadmap) was also absent from Task Scheduler on this date.
  Its definition lives outside this repository and was not recreated here.

## Acceptance

- [x] `Get-Command librehw` resolves
  `E:\SQ_HQ\u-programs\bin\librehw.cmd`, and its bytes are unchanged.
- [x] Publish emits exactly one framework-dependent x64 EXE and a separate
  bounded manifest.
- [x] The installed process path is the fixed shallow path.
- [x] Debug/Release builds and tests do not write into the installed runtime.
- [x] Config load/save and the generated backup stay under the selected
  `sqdata` root.
- [x] New CSV logs appear only under the selected `logs` root.
- [x] Launcher promotion/repair and persistent settings/log writes reject
  reparse-point ancestors or leaves before touching an external target.
- [x] An explicit current config migrates with verified length/hash and retains
  listener, dashboard, logging, UI, and sensor settings.
- [x] `\SevGrp\AdminTask\LibreHW-No-UAC` owns on-demand and intended logon start,
  with the stable action/working directory.
- [x] Both Start Menu shortcuts route through the unchanged
  `E:\SQ_HQ\u-programs\bin\librehw.cmd`; neither target nor working directory
  references a repository `bin` tree.
- [x] The duplicate scheduler-root task is absent after accepted cutover.
- [x] Direct task, normal-user `librehw.cmd`, and repeated launcher calls
  converge on one process.
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

## Verification

Repository gate:

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Release gate:

- inspect candidate count, version, size, manifest, and SHA-256;
- test data-root selection and executable-adjacent fallback in temporary roots;
- run a single-file launch smoke without touching the live install;
- run installer and rollback integration tests against temporary install/data
  roots;
- preserve external sentinels across hostile launcher, payload, rollback,
  transaction, settings, log, cleanup, and recovery reparse cases;
- verify `-WhatIf` changes no file, process, task, shortcut, or runtime state.

Operator workflow:

```powershell
# Non-live parser, failure-injection, hostile-input, and rollback checks
.\scripts\local-release\Test-LhmLocalRelease.ps1

# Publish an isolated candidate; omit -AllowDirty for normal committed releases
.\scripts\local-release\Publish-LibreHardwareMonitor.ps1 `
  -CandidateDirectory E:\path\to\candidate

# Preview, then perform promotion
.\ops\local-release\Install-LibreHardwareMonitorRelease.ps1 `
  -CandidateDirectory E:\path\to\candidate `
  -InitialConfigSource E:\path\to\LibreHardwareMonitor.Windows.Forms.config `
  -WhatIf
.\ops\local-release\Install-LibreHardwareMonitorRelease.ps1 `
  -CandidateDirectory E:\path\to\candidate `
  -InitialConfigSource E:\path\to\LibreHardwareMonitor.Windows.Forms.config `
  -Confirm:$false

# Swap current and the one rollback payload
.\ops\local-release\Restore-LibreHardwareMonitorRelease.ps1 -WhatIf

# After attended UI and normal-user launcher acceptance
.\ops\local-release\Finalize-LibreHardwareMonitorCutover.ps1 `
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

## Initial config decision

Resolved for the first install: use the then-current
`bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.config`
(30,554 bytes at cutover). Its verified settings enable the dashboard and
logging required by release health. The exact Release config/backup and the
7,051-byte Debug config are now preserved under the historical archive recorded
in `docs/repository-build-output-cleanup.md`.

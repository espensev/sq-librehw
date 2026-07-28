# Repository Build-Output Cleanup

**Status:** completed on verified `snd-desk`
**Updated:** 2026-07-28

## Authority boundary

Repository `bin` and `obj` directories are disposable compiler, test, pack, and
verification output. They are ignored by Git and are never a launch, install,
configuration, or logging surface.

The authoritative machine-local locations are:

```text
Stable runtime:
E:\SQ_HQ\Monitoring\LibreHW\LibreHardwareMonitor.Windows.Forms.exe

Mutable data:
E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor\

Public command:
E:\SQ_HQ\u-programs\bin\librehw.cmd
```

The stable release contains one application EXE plus its bounded release and
runtime manifests. Builds and publishes use isolated output roots and never
replace that EXE in place from a repository build directory.

## Accepted cleanup

The pre-clean inventory covered every existing allowlisted repo-local `bin` and
`obj` tree:

| Item | Count |
|---|---:|
| Files | 6,155 |
| Bytes | 9,739,202,283 |
| EXEs | 32 |
| Historical CSV logs | 3,361 |

The 32 EXEs and the other generated files were reproducible output, not installed
payloads. The CSVs were real historical telemetry, so they were not deleted.
They and the three old mutable config files were moved without flattening names
to:

```text
E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor\historical\
  pre-stable-repo-build-output-2026-07-25\
    logs\
    config\Debug\
    config\Release\
```

Archive proof:

- CSV count: 3,361
- CSV bytes: 9,058,052,916
- CSV name/length/UTC-write-time metadata SHA-256:
  `35085a84ac5d2678074a31e918483bbe3a55a6fd1dd4a780254b74ab44acdd1b`
- Two names also existed in the live log directory, which is why the archive is
  isolated instead of merged into `logs`.
- Debug config SHA-256:
  `626c12f35773986632032896efff6a20cee0855b7f4d4a6282854bea034916ce`
- Release config and backup SHA-256:
  `68d83c865af307d558d09730ab469ae5fd5205c0352a06c68d25ff49dd841496`

After preserving those 3,364 files, the cleanup removed 2,791 reproducible files
totalling 681,081,208 bytes, including all 32 repo-output EXEs. No stable runtime
or live `sqdata` file was part of the deletion scope.

The one EXE that remains elsewhere in the repository is the tracked
`LibreHardwareMonitor.Windows.Forms\Resources\PawnIO_setup.exe` application
resource. It is embedded/used by the product and is not a build-output or launch
copy.

## Repeatable cleanup

Future builds may recreate ignored output directories. Clean the complete
allowlist with:

```powershell
.\scripts\local-release\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
.\scripts\local-release\Clear-LhmRepositoryBuildOutputs.ps1 -Confirm:$false
```

The command resolves every target beneath the selected repository root, rejects
reparse points at every intermediate ancestor and below each output root, and
refuses to remove any tree containing Libre Hardware Monitor CSV or config
state. It also rejects filesystem roots and requires the expected solution and
central-build marker files before enumeration. The documented no-argument form
is regression-tested under Windows PowerShell 5.1. This is intentionally
stricter than `dotnet clean`, which does not cover every custom/stale
configuration or runtime-created file.

## Public command chain

The supported operator entry point remains the command name, without requiring
the `.cmd` extension:

```text
Get-Command librehw
  -> E:\SQ_HQ\u-programs\bin\librehw.cmd
  -> E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1
  -> existing process: app-owned tray-toggle/foreground restoration
  -> no process: \SevGrp\AdminTask\LibreHW-No-UAC
  -> E:\SQ_HQ\Monitoring\LibreHW\LibreHardwareMonitor.Windows.Forms.exe
```

Every path converges on one exact stable process. The managed task action and
working directory use the shallow stable directory, and health is checked at
`http://localhost:8085/data.json`.

The final command-chain test also covers the actual Windows PowerShell 5.1 host
started by `librehw.cmd`. A scalar-`Count` incompatibility found during this
cleanup was fixed by explicitly materializing process-query results as an
array, and the non-live release suite now locks that compatibility down.

Because the unchanged CMD shim uses asynchronous `start /min`, its exit code
proves that the helper was created, not that the delegated script ultimately
succeeded. The process, foreground UI, task/path, and HTTP assertions below are
the authoritative end-to-end result.

## Final command-chain proof

The final identity-verified `snd-desk` gate passed on 2026-07-25:

- `Get-Command librehw` resolved the Application
  `E:\SQ_HQ\u-programs\bin\librehw.cmd`.
- The unchanged shim SHA-256 was
  `fe319aabd007a3a639cd618c748d480f7881dd18864db6f9c94bac537bd10d73`.
- The source and deployed delegated launcher SHA-256 both were
  `aca14b36bfcf033b58b747115502856e9c9d72616088338e08e92f77937284cb`.
- With the app genuinely hidden to its tray, invoking `librehw` restored and
  foregrounded the existing stable PID `63544`; it did not create a duplicate
  or leave a launcher helper.
- The responding native window exposed 20 UI Automation descendants, the
  `treeView` pane, two scrollbars, and the main menu.
- `data.json` returned HTTP 200 with machine `SND-DESK` and 51 hardware groups.
- The one process path and manifest hash matched the shallow stable EXE.
- `\SevGrp\AdminTask\LibreHW-No-UAC` had one logon trigger and the exact stable
  action/working directory. The scheduler-root legacy task remained absent.
- Both Start Menu shortcuts targeted the public CMD shim; no task or shortcut
  referenced a repository build tree.
- No repo-local `bin` or `obj` directory remained after all isolated build and
  release integration gates.

### Zero-process launcher re-smoke — 2026-07-28

The July 25 launcher hash above is historical. A later no-process check exposed
a Windows PowerShell 5.1 StrictMode failure in empty-array member enumeration.
The canonical and deployed launchers were changed together and now both hash
to `068381618170282fdde593d41ed01d24d983d356d6841bb70780a07e0052be84`.

The release safety suite now contains a forced zero-process PS5.1 regression
case and passed. From a genuinely absent process, PATH `librehw` started one
stable task-owned process, visible and foregrounded with populated native
controls and HTTP 200. A second PATH invocation retained the same PID with no
launcher helper or duplicate. The process remained responding after a
232-second dwell with no new Application 1000/1026 event; longer native GPU
polling stability remains a separate concern.

## Retired pre-stable recovery

The old recovery packets name the removed Debug and Release payloads. Their
launcher/task definitions remain under `sqdata\release-recovery` as historical
audit evidence, but restoring them would create broken startup entries.
Accordingly, both old startup-recovery entry points now fail closed before any
launcher, task, or shortcut mutation. Installed-release rollback remains
`Restore-LibreHardwareMonitorRelease.ps1` and uses only the bounded stable
`rollback` slot.

## Separate external consumer

The `hardware-optimization` health-feed task is outside this repository and was
already configured with a different, nonexistent legacy root:
`E:\SQ_HQ\Monitoring\sq-librehw\bin\Release\net10.0-windows`. It is not part of
the `librehw` launch chain or this deletion scope; its stale log-root default
requires a separate repair to the canonical `sqdata` log directory.

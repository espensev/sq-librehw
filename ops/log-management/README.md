# LibreHardwareMonitor log management

This package archives completed daily CSV logs into verified one-entry ZIPs and
prunes only recognized, readable archives past retention. It is source code,
not the live task location.

Run the isolated integration test:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\ops\log-management\Test-LhmLogManagement.ps1
```

Preview an archive cycle:

```powershell
.\ops\log-management\Archive-LhmLogs.ps1 `
  -SourceDirectory 'E:\path\to\runtime' `
  -ArchiveRoot 'E:\path\to\LogArchive' `
  -MachineName 'EXPECTED-MACHINE' `
  -WhatIf
```

The installer copies the common/archive/cleanup/invoker scripts plus a bounded
JSON configuration to a stable runtime directory and registers a daily SYSTEM
task. Always verify the explicit target machine first, then preview:

```powershell
.\ops\log-management\Install-LhmLogManagementTask.ps1 `
  -RuntimeDirectory 'E:\stable\lhm-log-management' `
  -SourceDirectory 'E:\runtime-one','E:\runtime-two' `
  -ArchiveRoot 'E:\stable\LogArchive' `
  -MachineName 'EXPECTED-MACHINE' `
  -WhatIf
```

After approval, remove `-WhatIf`, inspect `log-management.json`, run the copied
`Invoke-LhmLogManagement.ps1` manually once, and verify ZIP contents before
trusting the task. This installer deliberately does not disable or delete any
legacy task.

The installed version-1 configuration also records the task name, task path,
daily hour/minute, and PowerShell executable. Preview a source-script reconcile
without allowing installer defaults to redirect the task:

```powershell
.\ops\log-management\Install-LhmLogManagementTask.ps1 `
  -RuntimeDirectory 'E:\stable\lhm-log-management' `
  -ReconcileFromExistingConfig `
  -WhatIf
```

A legacy version-1 configuration without those task fields fails closed. After
inventorying the existing task, migrate it by supplying all missing task values
explicitly (`-TaskName`, `-TaskPath`, `-DailyHour`, `-DailyMinute`, and
`-PowerShellExecutable`) on that reconcile.

See `docs/features/feature-host-log-management.md` for the complete safety and cutover
contract.

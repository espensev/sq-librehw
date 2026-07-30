[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [switch] $AttendedUiAccepted,

    [Parameter(Mandatory)]
    [switch] $NormalUserLauncherAccepted,

    [uri] $HealthUri = 'http://localhost:8085/data.json',

    [ValidateRange(5, 300)]
    [int] $ValidationTimeoutSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')

if (-not $AttendedUiAccepted) {
    throw 'Use -AttendedUiAccepted only after checking the populated restored UI.'
}
if (-not $NormalUserLauncherAccepted) {
    throw 'Use -NormalUserLauncherAccepted only after invoking librehw.cmd from a normal unelevated shell.'
}

$null = Assert-LhmVerifiedMachineIdentity

$installRoot = $script:LhmProductionInstallRoot
$dataRoot = $script:LhmProductionDataRoot
$executablePath = Join-Path $installRoot $script:LhmExecutableName
$runtimeConfigPath = Join-Path $installRoot $script:LhmRuntimeConfigName
$launcherTargetPath = $script:LhmLauncherTargetPath
$publicShimPath = $script:LhmPublicShimPath
$canonicalLauncher = Join-Path $PSScriptRoot 'Start-LibreHardwareMonitor.ps1'
$legacyExecutablePath =
    'E:\SQ_HQ\Monitoring\sq-librehwdev\sq-librehw\bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe'
$legacyWorkingDirectory = Split-Path -Parent $legacyExecutablePath
$shortcutPaths = @(
    'C:\Users\Sev\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LibreHardwareMonitor.Windows.Forms.lnk',
    'C:\Users\Sev\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LibreHW-No-UAC.lnk'
)
$recoveryParent = Join-Path $dataRoot 'release-recovery'
$recoveryRoot = Join-Path $recoveryParent 'legacy-root-task-cutover'

Assert-LhmNoReparsePathAncestry -Path $dataRoot -Label 'LibreHW data root'
if (Test-Path -LiteralPath $recoveryParent) {
    Assert-LhmNoReparsePathAncestry `
        -Path $recoveryParent `
        -Label 'LibreHW recovery parent'
}
Assert-LhmInstalledRootInventory -InstallRoot $installRoot
if ((Get-LhmFileSha256 -Path $publicShimPath) -cne $script:LhmPublicShimSha256) {
    throw 'Public librehw.cmd does not match the accepted unchanged shim hash.'
}
$shimText = Get-Content -LiteralPath $publicShimPath -Raw
if ($shimText -notmatch [regex]::Escape(
    '-File "E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1"')) {
    throw 'Public librehw.cmd does not delegate to the exact canonical launcher target.'
}
if (-not (Test-Path -LiteralPath $launcherTargetPath -PathType Leaf) -or
    (Get-LhmFileSha256 -Path $launcherTargetPath) -cne
        (Get-LhmFileSha256 -Path $canonicalLauncher)) {
    throw 'The delegated launcher is not the canonical source-controlled launcher.'
}

$null = Get-LhmInstalledPayloadState -InstallRoot $installRoot
$null = Read-LhmRuntimeConfig `
    -Path $runtimeConfigPath `
    -ExpectedDataRoot $dataRoot `
    -ExpectedManagedTaskPath $script:LhmManagedTaskPath

$taskParts = Split-LhmManagedTaskPath -ManagedStartupTaskPath $script:LhmManagedTaskPath
$managedTask = Get-ScheduledTask `
    -TaskPath $taskParts.TaskPath `
    -TaskName $taskParts.TaskName `
    -ErrorAction Stop
$managedActions = @($managedTask.Actions)
$managedTriggers = @($managedTask.Triggers)
if ($managedActions.Count -ne 1 -or
    -not (Test-LhmPathEqual -Left ([string]$managedActions[0].Execute) -Right $executablePath) -or
    -not (Test-LhmPathEqual -Left ([string]$managedActions[0].WorkingDirectory) -Right $installRoot) -or
    [string]$managedTask.Principal.UserId -notin @('Sev', 'SND-DESK\Sev') -or
    [string]$managedTask.Principal.RunLevel -cne 'Highest' -or
    [string]$managedTask.Principal.LogonType -cne 'Interactive' -or
    [string]$managedTask.Settings.MultipleInstances -cne 'IgnoreNew' -or
    -not [bool]$managedTask.Settings.StartWhenAvailable -or
    [bool]$managedTask.Settings.AllowHardTerminate -or
    $managedTriggers.Count -ne 1 -or
    [string]$managedTriggers[0].CimClass.CimClassName -cne 'MSFT_TaskLogonTrigger') {
    throw "Managed task '$($script:LhmManagedTaskPath)' does not satisfy the release contract."
}

$processEntries = @(Get-LhmProcesses -ExpectedExecutablePath $executablePath)
if ($processEntries.Count -ne 1 -or
    -not $processEntries[0].Path -or
    -not $processEntries[0].IsExact) {
    throw 'Finalization requires exactly one path-verified stable Libre Hardware Monitor process.'
}
Assert-LhmHttpHealth -HealthUri $HealthUri -TimeoutSeconds $ValidationTimeoutSeconds

$legacyTask = Get-ScheduledTask `
    -TaskPath '\' `
    -TaskName 'LibreHardwareMonitor' `
    -ErrorAction SilentlyContinue
if ($null -eq $legacyTask) {
    if (Test-Path -LiteralPath $recoveryRoot -PathType Container) {
        throw 'The legacy root task is already absent and durable recovery evidence exists; no finalization action remains.'
    }
    throw 'The exact legacy root task is absent, but no durable cutover recovery evidence exists.'
}

$legacyActions = @($legacyTask.Actions)
$legacyTriggers = @($legacyTask.Triggers)
if ($legacyActions.Count -ne 1 -or
    -not (Test-LhmPathEqual -Left ([string]$legacyActions[0].Execute) -Right $legacyExecutablePath) -or
    -not (Test-LhmPathEqual -Left ([string]$legacyActions[0].WorkingDirectory) -Right $legacyWorkingDirectory) -or
    [string]$legacyTask.Principal.UserId -cne 'Sev' -or
    [string]$legacyTask.Principal.RunLevel -cne 'Highest' -or
    [string]$legacyTask.Principal.LogonType -cne 'Interactive' -or
    $legacyTriggers.Count -ne 1 -or
    [string]$legacyTriggers[0].CimClass.CimClassName -cne 'MSFT_TaskLogonTrigger') {
    throw 'The scheduler-root task named LibreHardwareMonitor is not the exact discovered legacy duplicate; refusing deletion.'
}

$legacyTaskXml = Export-ScheduledTask -TaskPath '\' -TaskName 'LibreHardwareMonitor'
if ([System.Text.Encoding]::Unicode.GetByteCount($legacyTaskXml) -gt 1048576) {
    throw 'Legacy Scheduled Task XML is unexpectedly large.'
}
$reuseRecovery = Test-Path -LiteralPath $recoveryRoot -PathType Container
if ($reuseRecovery) {
    $null = Read-LhmLegacyRecoveryPacket `
        -RecoveryRoot $recoveryRoot `
        -ExpectedPublicShimPath $publicShimPath `
        -ExpectedPublicShimSha256 $script:LhmPublicShimSha256
}
else {
    for ($index = 0; $index -lt $shortcutPaths.Count; $index++) {
        Assert-LhmLegacyShortcutContract -Path $shortcutPaths[$index] -Index $index
    }
}

if (-not $PSCmdlet.ShouldProcess(
    'Start Menu links and exact legacy \LibreHardwareMonitor task',
    'Persist bounded recovery, route both links through unchanged librehw.cmd, revalidate, and retire the exact legacy task')) {
    return
}

$preparingRoot =
    Join-Path $recoveryParent ".legacy-root-task-cutover-$([guid]::NewGuid().ToString('N'))"
$null = New-LhmSafeRecoveryPreparationDirectory `
    -DataRoot $dataRoot `
    -RecoveryParent $recoveryParent `
    -PreparationPath $preparingRoot `
    -RequiredLeafPrefix '.legacy-root-task-cutover-'
$shortcutRecords = @()
$legacyRemoved = $false

try {
    if (-not $reuseRecovery) {
        $taskXmlPath = Join-Path $preparingRoot 'legacy-task.xml'
        $legacyTaskXml | Set-Content -LiteralPath $taskXmlPath -Encoding Unicode
        for ($index = 0; $index -lt $shortcutPaths.Count; $index++) {
            $shortcutPath = $shortcutPaths[$index]
            $existed = Test-Path -LiteralPath $shortcutPath -PathType Leaf
            $backupName = "$index.lnk"
            $hash = $null
            if ($existed) {
                Copy-Item `
                    -LiteralPath $shortcutPath `
                    -Destination (Join-Path $preparingRoot $backupName)
                $hash = Get-LhmFileSha256 -Path $shortcutPath
            }
            $shortcutRecords += [ordered]@{
                path = $shortcutPath
                existed = $existed
                backup = if ($existed) { $backupName } else { $null }
                sha256 = $hash
            }
        }

        [ordered]@{
            schema = 'sq.librehw.legacy-cutover-recovery.v1'
            createdAt = [DateTimeOffset]::UtcNow.ToString('o')
            legacyTaskPath = '\LibreHardwareMonitor'
            legacyTaskXml = 'legacy-task.xml'
            legacyTaskXmlSha256 = Get-LhmFileSha256 -Path $taskXmlPath
            publicShimPath = $publicShimPath
            publicShimSha256 = $script:LhmPublicShimSha256
            shortcuts = $shortcutRecords
        } | ConvertTo-Json -Depth 6 | Set-Content `
            -LiteralPath (Join-Path $preparingRoot 'recovery.json') `
            -Encoding UTF8
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $preparingRoot `
            -ExpectedPublicShimPath $publicShimPath `
            -ExpectedPublicShimSha256 $script:LhmPublicShimSha256
        Move-Item -LiteralPath $preparingRoot -Destination $recoveryRoot
    }
    $null = Read-LhmLegacyRecoveryPacket `
        -RecoveryRoot $recoveryRoot `
        -ExpectedPublicShimPath $publicShimPath `
        -ExpectedPublicShimSha256 $script:LhmPublicShimSha256

    foreach ($shortcutPath in $shortcutPaths) {
        $shell = New-Object -ComObject WScript.Shell
        try {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $publicShimPath
            $shortcut.Arguments = ''
            $shortcut.WorkingDirectory = Split-Path -Parent $publicShimPath
            $shortcut.IconLocation = "$executablePath,0"
            $shortcut.Save()
        }
        finally {
            if ($null -ne $shell) {
                [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
            }
        }
    }

    foreach ($shortcutPath in $shortcutPaths) {
        $shell = New-Object -ComObject WScript.Shell
        try {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            if (-not (Test-LhmPathEqual -Left $shortcut.TargetPath -Right $publicShimPath)) {
                throw "Start Menu shortcut did not converge on librehw.cmd: '$shortcutPath'."
            }
        }
        finally {
            if ($null -ne $shell) {
                [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
            }
        }
    }

    & $launcherTargetPath -StartupTimeoutSeconds $ValidationTimeoutSeconds
    Assert-LhmHttpHealth -HealthUri $HealthUri -TimeoutSeconds $ValidationTimeoutSeconds
    $processEntries = @(Get-LhmProcesses -ExpectedExecutablePath $executablePath)
    if ($processEntries.Count -ne 1 -or -not $processEntries[0].IsExact) {
        throw 'Launcher validation did not converge on exactly one stable process.'
    }

    Unregister-ScheduledTask `
        -TaskPath '\' `
        -TaskName 'LibreHardwareMonitor' `
        -Confirm:$false
    $legacyRemoved = $true
    if ($null -ne (Get-ScheduledTask `
        -TaskPath '\' `
        -TaskName 'LibreHardwareMonitor' `
        -ErrorAction SilentlyContinue)) {
        throw 'Legacy scheduler-root task still exists after retirement.'
    }

    [pscustomobject]@{
        StableExecutable = $executablePath
        ManagedTask = $script:LhmManagedTaskPath
        LegacyRootTaskRetired = $true
        RecoveryDirectory = $recoveryRoot
        PublicShim = $publicShimPath
        PublicShimSha256 = $script:LhmPublicShimSha256
        StartMenuLinks = $shortcutPaths
    }
}
catch {
    if (Test-Path -LiteralPath $recoveryRoot -PathType Container) {
        $recoveryPacket = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $recoveryRoot `
            -ExpectedPublicShimPath $publicShimPath `
            -ExpectedPublicShimSha256 $script:LhmPublicShimSha256
        for ($index = 0; $index -lt 2; $index++) {
            $record = $recoveryPacket.ShortcutRecords[$index]
            $shortcutPath = $recoveryPacket.ShortcutPaths[$index]
            if ([bool]$record.existed) {
                Copy-Item `
                    -LiteralPath $recoveryPacket.ShortcutBackupPaths[$index] `
                    -Destination $shortcutPath `
                    -Force
            }
            elseif (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
                Remove-Item -LiteralPath $shortcutPath -Force
            }
        }
        if ($legacyRemoved) {
            Register-ScheduledTask `
                -TaskPath '\' `
                -TaskName 'LibreHardwareMonitor' `
                -Xml $recoveryPacket.TaskXmlText `
                -Force | Out-Null
        }
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $preparingRoot -PathType Container) {
        Remove-LhmOwnedDirectory `
            -Path $preparingRoot `
            -ExpectedParent $recoveryParent `
            -RequiredLeafPrefix '.legacy-root-task-cutover-'
    }
}

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High', DefaultParameterSetName = 'Explicit')]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$RuntimeDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Explicit')][ValidateNotNullOrEmpty()][string[]]$SourceDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Explicit')][ValidateNotNullOrEmpty()][string]$ArchiveRoot,
    [Parameter(Mandatory, ParameterSetName = 'Reconcile')][switch]$ReconcileFromExistingConfig,
    [ValidateNotNullOrEmpty()][string]$MachineName = $env:COMPUTERNAME,
    [ValidateRange(1, 36500)][int]$RetentionDays = 365,
    [ValidateNotNullOrEmpty()][string]$TaskName = 'SQ LibreHardwareMonitor Log Management',
    [ValidateNotNullOrEmpty()][string]$TaskPath = '\',
    [ValidateRange(0, 23)][int]$DailyHour = 3,
    [ValidateRange(0, 59)][int]$DailyMinute = 45,
    [ValidateNotNullOrEmpty()][string]$PowerShellExecutable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLogManagement.Common.ps1')

if ($PSCmdlet.ParameterSetName -eq 'Reconcile') {
    $existingConfigPath = Join-Path (Resolve-LhmFullPath -Path $RuntimeDirectory) 'log-management.json'
    if (-not [System.IO.File]::Exists($existingConfigPath)) {
        throw "Reconcile requires an existing configuration: $existingConfigPath"
    }

    $existingConfig = Get-Content -LiteralPath $existingConfigPath -Raw | ConvertFrom-Json
    $existingSchemaInfo = $existingConfig.PSObject.Properties['Schema']
    $existingVersionInfo = $existingConfig.PSObject.Properties['Version']
    if ($null -eq $existingSchemaInfo -or $existingSchemaInfo.Value -ne 'sq.lhm-log-management' -or $null -eq $existingVersionInfo -or [int]$existingVersionInfo.Value -ne 1) {
        throw 'Existing log-management configuration has an unsupported schema or version.'
    }

    foreach ($property in @('SourceDirectories', 'ArchiveRoot', 'MachineName', 'RetentionDays')) {
        $propertyInfo = $existingConfig.PSObject.Properties[$property]
        if ($null -eq $propertyInfo -or $null -eq $propertyInfo.Value) {
            throw "Existing log-management configuration is missing '$property'."
        }
    }

    $SourceDirectory = @($existingConfig.SourceDirectories)
    $ArchiveRoot = [string]$existingConfig.ArchiveRoot
    if (-not $PSBoundParameters.ContainsKey('MachineName')) {
        $MachineName = [string]$existingConfig.MachineName
    }
    if (-not $PSBoundParameters.ContainsKey('RetentionDays')) {
        $RetentionDays = [int]$existingConfig.RetentionDays
    }

    foreach ($property in @('TaskName', 'TaskPath', 'DailyHour', 'DailyMinute', 'PowerShellExecutable')) {
        $propertyInfo = $existingConfig.PSObject.Properties[$property]
        if (($null -eq $propertyInfo -or $null -eq $propertyInfo.Value) -and -not $PSBoundParameters.ContainsKey($property)) {
            throw "Existing log-management configuration is missing '$property'; supply it explicitly to migrate this legacy configuration."
        }
    }

    if (-not $PSBoundParameters.ContainsKey('TaskName')) {
        $TaskName = [string]$existingConfig.TaskName
    }
    if (-not $PSBoundParameters.ContainsKey('TaskPath')) {
        $TaskPath = [string]$existingConfig.TaskPath
    }
    if (-not $PSBoundParameters.ContainsKey('DailyHour')) {
        $DailyHour = [int]$existingConfig.DailyHour
    }
    if (-not $PSBoundParameters.ContainsKey('DailyMinute')) {
        $DailyMinute = [int]$existingConfig.DailyMinute
    }
    if (-not $PSBoundParameters.ContainsKey('PowerShellExecutable')) {
        $PowerShellExecutable = [string]$existingConfig.PowerShellExecutable
    }
}

$sourceDirectoryValues = @($SourceDirectory)
if ($sourceDirectoryValues.Count -eq 0) {
    throw 'SourceDirectories must contain at least one path.'
}
if (@($sourceDirectoryValues | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) {
    throw 'SourceDirectories must not contain blank paths.'
}
if ([string]::IsNullOrWhiteSpace($ArchiveRoot)) {
    throw 'ArchiveRoot must not be empty.'
}
if ([string]::IsNullOrWhiteSpace($MachineName)) {
    throw 'MachineName must not be empty.'
}
if ($RetentionDays -lt 1 -or $RetentionDays -gt 36500) {
    throw 'RetentionDays must be between 1 and 36500.'
}
if ([string]::IsNullOrWhiteSpace($TaskName)) {
    throw 'TaskName must not be empty.'
}
if ($TaskName -match '[\\/]') {
    throw 'TaskName must not contain path separators.'
}
if ([string]::IsNullOrWhiteSpace($TaskPath) -or $TaskPath -notmatch '^\\(?:[^\\/]+\\)*$') {
    throw "TaskPath must be '\' or a scheduled-task folder path that begins and ends with '\'."
}
if ($DailyHour -lt 0 -or $DailyHour -gt 23) {
    throw 'DailyHour must be between 0 and 23.'
}
if ($DailyMinute -lt 0 -or $DailyMinute -gt 59) {
    throw 'DailyMinute must be between 0 and 59.'
}
if ($PSCmdlet.ParameterSetName -eq 'Reconcile' -and [string]::IsNullOrWhiteSpace($PowerShellExecutable)) {
    throw 'PowerShellExecutable must not be empty during reconcile.'
}

$runtimePath = Resolve-LhmFullPath -Path $RuntimeDirectory
$archiveRootPath = Resolve-LhmFullPath -Path $ArchiveRoot
$safeMachineName = Get-LhmSafeMachineName -MachineName $MachineName
$sourcePaths = @($SourceDirectory | ForEach-Object { Resolve-LhmFullPath -Path $_ })
$packageFiles = @(
    'LhmLogManagement.Common.ps1',
    'Archive-LhmLogs.ps1',
    'Clean-LhmLogArchives.ps1',
    'Invoke-LhmLogManagement.ps1'
)

foreach ($packageFile in $packageFiles) {
    $packagePath = Join-Path $PSScriptRoot $packageFile
    if (-not [System.IO.File]::Exists($packagePath)) {
        throw "Required package file is missing: $packagePath"
    }
}

if ([string]::IsNullOrWhiteSpace($PowerShellExecutable)) {
    $PowerShellExecutable = (Get-Command powershell.exe -ErrorAction Stop).Source
}
else {
    $PowerShellExecutable = Resolve-LhmFullPath -Path $PowerShellExecutable
}
if (-not [System.IO.File]::Exists($PowerShellExecutable)) {
    throw "PowerShell executable does not exist: $PowerShellExecutable"
}

$targetDescription = "$TaskPath$TaskName -> $runtimePath"
if (-not $PSCmdlet.ShouldProcess($targetDescription, 'Install scripts/configuration and register daily SYSTEM task')) {
    New-LhmLogResult -Action 'Install' -Status 'Planned' -Destination $runtimePath -Message "Would register $TaskPath$TaskName at $($DailyHour.ToString('00')):$($DailyMinute.ToString('00'))."
    return
}

$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Administrator rights are required to install the scheduled task.'
}

foreach ($sourcePath in $sourcePaths) {
    if (-not [System.IO.Directory]::Exists($sourcePath)) {
        throw "Configured source directory does not exist: $sourcePath"
    }
}

[System.IO.Directory]::CreateDirectory($runtimePath) | Out-Null
foreach ($packageFile in $packageFiles) {
    $source = Join-Path $PSScriptRoot $packageFile
    $destination = Join-Path $runtimePath $packageFile
    $temporary = $destination + '.tmp-' + [guid]::NewGuid().ToString('N')
    [System.IO.File]::Copy($source, $temporary, $false)
    Move-Item -LiteralPath $temporary -Destination $destination -Force
}

$configPath = Join-Path $runtimePath 'log-management.json'
$configTemporary = $configPath + '.tmp-' + [guid]::NewGuid().ToString('N')
$config = [ordered]@{
    Schema = 'sq.lhm-log-management'
    Version = 1
    SourceDirectories = $sourcePaths
    ArchiveRoot = $archiveRootPath
    MachineName = $safeMachineName
    RetentionDays = $RetentionDays
    TaskName = $TaskName
    TaskPath = $TaskPath
    DailyHour = $DailyHour
    DailyMinute = $DailyMinute
    PowerShellExecutable = $PowerShellExecutable
}
[System.IO.File]::WriteAllText($configTemporary,
                               ($config | ConvertTo-Json -Depth 4),
                               [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $configTemporary -Destination $configPath -Force

$invokePath = Join-Path $runtimePath 'Invoke-LhmLogManagement.ps1'
$arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$invokePath`" -ConfigPath `"$configPath`""
$action = New-ScheduledTaskAction -Execute $PowerShellExecutable -Argument $arguments -WorkingDirectory $runtimePath
$triggerAt = [datetime]::Today.AddHours($DailyHour).AddMinutes($DailyMinute)
$trigger = New-ScheduledTaskTrigger -Daily -At $triggerAt
$taskPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Description 'Archives verified completed LibreHardwareMonitor logs and prunes verified expired archives.' -Force | Out-Null

New-LhmLogResult -Action 'Install' -Status 'Installed' -Destination $runtimePath -Message "Registered $TaskPath$TaskName at $($DailyHour.ToString('00')):$($DailyMinute.ToString('00'))."

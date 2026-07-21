[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$RuntimeDirectory,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$SourceDirectory,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ArchiveRoot,
    [ValidateNotNullOrEmpty()][string]$MachineName = $env:COMPUTERNAME,
    [ValidateRange(1, 36500)][int]$RetentionDays = 365,
    [ValidateNotNullOrEmpty()][string]$TaskName = 'SQ LibreHardwareMonitor Log Management',
    [ValidateNotNullOrEmpty()][string]$TaskPath = '\',
    [ValidateRange(0, 23)][int]$DailyHour = 3,
    [ValidateRange(0, 59)][int]$DailyMinute = 45,
    [string]$PowerShellExecutable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLogManagement.Common.ps1')

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

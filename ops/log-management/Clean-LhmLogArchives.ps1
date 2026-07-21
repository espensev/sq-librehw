[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ArchiveRoot,
    [ValidateNotNullOrEmpty()][string]$MachineName = $env:COMPUTERNAME,
    [ValidateRange(1, 36500)][int]$RetentionDays = 365,
    [datetime]$Now = (Get-Date)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLogManagement.Common.ps1')

$archiveRootPath = Resolve-LhmFullPath -Path $ArchiveRoot
$safeMachineName = Get-LhmSafeMachineName -MachineName $MachineName
$machineRoot = Resolve-LhmChildPath -Parent $archiveRootPath -Child $safeMachineName
if (-not [System.IO.Directory]::Exists($machineRoot)) {
    New-LhmLogResult -Action 'Retention' -Status 'Skipped' -Source $machineRoot -Message 'Machine archive root does not exist.'
    return
}

$cutoffUtc = $Now.ToUniversalTime().AddDays(-$RetentionDays)
$archives = Get-ChildItem -LiteralPath $machineRoot -Filter '*.zip' -File -Recurse
foreach ($archive in $archives) {
    $relative = $archive.FullName.Substring($machineRoot.Length) -replace '^[\\/]+', ''
    $parts = $relative -split '[\\/]'
    $recognizedLayout = $parts.Count -eq 3 -and
                        $parts[0] -match '^\d{4}$' -and
                        $parts[1] -match '^\d{2}-[A-Za-z]{3}$' -and
                        $parts[2] -match '^LibreHardwareMonitorLog-\d{4}-\d{2}-\d{2}(?:-[A-Za-z0-9._-]+)?\.zip$'
    if (-not $recognizedLayout) {
        New-LhmLogResult -Action 'Retention' -Status 'Retained' -Source $archive.FullName -Message 'Archive path is not recognized.'
        continue
    }

    if ($archive.LastWriteTimeUtc -ge $cutoffUtc) {
        continue
    }

    $check = Test-LhmZipArchive -Path $archive.FullName
    $entryRecognized = $check.Valid -and
                       $check.EntryName -match '^LibreHardwareMonitorLog-\d{4}-\d{2}-\d{2}(?:-[A-Za-z0-9._-]+)?\.csv$'
    if (-not $entryRecognized) {
        New-LhmLogResult -Action 'Retention' -Status 'Retained' -Source $archive.FullName -Message ('Expired archive failed validation: ' + $check.Reason)
        continue
    }

    if ($PSCmdlet.ShouldProcess($archive.FullName, "Remove verified archive older than $RetentionDays days")) {
        Remove-Item -LiteralPath $archive.FullName -Force
        New-LhmLogResult -Action 'Retention' -Status 'Removed' -Source $archive.FullName -Message 'Verified expired archive removed.'
    }
    else {
        New-LhmLogResult -Action 'Retention' -Status 'Planned' -Source $archive.FullName -Message 'Would remove verified expired archive.'
    }
}

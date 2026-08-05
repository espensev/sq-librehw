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

$temporaryCutoffUtc = $Now.ToUniversalTime().AddDays(-1)
$temporaries = Get-ChildItem -LiteralPath $machineRoot -Filter '*.zip.tmp-*' -File -Recurse
foreach ($temporary in $temporaries) {
    if ($temporary.LastWriteTimeUtc -ge $temporaryCutoffUtc) {
        continue
    }

    if ($temporary.Name -notmatch '\.zip\.tmp-[0-9a-f]{32}$') {
        New-LhmLogResult -Action 'Retention' -Status 'Retained' -Source $temporary.FullName -Message 'Unrecognized temporary file retained.'
        continue
    }

    if ($PSCmdlet.ShouldProcess($temporary.FullName, 'Remove orphaned temporary archive older than one day')) {
        Remove-Item -LiteralPath $temporary.FullName -Force
        New-LhmLogResult -Action 'Retention' -Status 'TempRemoved' -Source $temporary.FullName -Message 'Orphaned temporary archive removed.'
    }
    else {
        New-LhmLogResult -Action 'Retention' -Status 'Planned' -Source $temporary.FullName -Message 'Would remove orphaned temporary archive.'
    }
}

$cutoffDate = $Now.Date.AddDays(-$RetentionDays)
$archivePattern = '^LibreHardwareMonitorLog-(?<date>\d{4}-\d{2}-\d{2})(?:-[A-Za-z0-9._-]+)?\.zip$'
$archives = Get-ChildItem -LiteralPath $machineRoot -Filter '*.zip' -File -Recurse
foreach ($archive in $archives) {
    $relative = $archive.FullName.Substring($machineRoot.Length) -replace '^[\\/]+', ''
    $parts = $relative -split '[\\/]'
    $recognizedLayout = $parts.Count -eq 3 -and
                        $parts[0] -match '^\d{4}$' -and
                        $parts[1] -match '^\d{2}-[A-Za-z]{3}$' -and
                        $parts[2] -match $archivePattern
    $logDate = [datetime]::MinValue
    $recognizedDate = $recognizedLayout -and
                      [datetime]::TryParseExact($Matches['date'],
                                                'yyyy-MM-dd',
                                                [System.Globalization.CultureInfo]::InvariantCulture,
                                                [System.Globalization.DateTimeStyles]::None,
                                                [ref]$logDate)
    if (-not $recognizedDate) {
        New-LhmLogResult -Action 'Retention' -Status 'Retained' -Source $archive.FullName -Message 'Archive path is not recognized.'
        continue
    }

    if ($logDate.Date -ge $cutoffDate) {
        continue
    }

    $check = Test-LhmZipArchive -Path $archive.FullName
    $entryRecognized = $check.Valid -and
                       $check.EntryName -match '^LibreHardwareMonitorLog-\d{4}-\d{2}-\d{2}(?:-[A-Za-z0-9._-]+)?\.csv$'
    if (-not $entryRecognized) {
        $reason = $check.Reason
        if ($check.Valid) {
            $reason = 'entry-name-unrecognized: ' + $check.EntryName
        }
        New-LhmLogResult -Action 'Retention' -Status 'RetainedInvalid' -Source $archive.FullName -Message ('Expired archive failed validation: ' + $reason)
        continue
    }

    if ($PSCmdlet.ShouldProcess($archive.FullName, "Remove verified archive with log date older than $RetentionDays days")) {
        Remove-Item -LiteralPath $archive.FullName -Force
        New-LhmLogResult -Action 'Retention' -Status 'Removed' -Source $archive.FullName -Message 'Verified expired archive removed.'
    }
    else {
        New-LhmLogResult -Action 'Retention' -Status 'Planned' -Source $archive.FullName -Message 'Would remove verified expired archive.'
    }
}

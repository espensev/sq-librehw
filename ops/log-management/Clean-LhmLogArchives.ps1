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
    if ($parts.Count -ne 3) {
        New-LhmLogResult -Action 'Retention' -Status 'Retained' -Source $archive.FullName -Message 'Archive path is not recognized.'
        continue
    }

    $recognizedName = $parts[2] -match $archivePattern
    if (-not $recognizedName) {
        New-LhmLogResult -Action 'Retention' -Status 'Retained' -Source $archive.FullName -Message 'Archive path is not recognized.'
        continue
    }

    $archiveDateText = [string]$Matches['date']
    $logDate = [datetime]::MinValue
    $recognizedDate = [datetime]::TryParseExact($archiveDateText,
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

    $expectedYear = $logDate.ToString('yyyy', [System.Globalization.CultureInfo]::InvariantCulture)
    $expectedMonth = $logDate.ToString('MM-MMM', [System.Globalization.CultureInfo]::InvariantCulture)
    if ($parts[0] -cne $expectedYear -or $parts[1] -cne $expectedMonth) {
        New-LhmLogResult -Action 'Retention' -Status 'RetainedInvalid' -Source $archive.FullName -Message ("Expired archive failed validation: archive-layout-mismatch; expected '$expectedYear\$expectedMonth'.")
        continue
    }

    $check = Test-LhmZipArchive -Path $archive.FullName
    $archiveBase = [System.IO.Path]::GetFileNameWithoutExtension($archive.Name)
    $expectedEntryName = $archiveBase + '.csv'
    $expectedConflictHash = $null
    $conflictMatch = [regex]::Match($archiveBase,
                                    '^(?<entryBase>.+)-conflict-(?<hash>[0-9a-f]{8})$',
                                    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($conflictMatch.Success) {
        $expectedEntryName = $conflictMatch.Groups['entryBase'].Value + '.csv'
        $expectedConflictHash = $conflictMatch.Groups['hash'].Value
    }

    $entryRecognized = $check.Valid -and $check.EntryName -ceq $expectedEntryName
    if ($entryRecognized -and $null -ne $expectedConflictHash) {
        $entryRecognized = $null -ne $check.Hash -and
                           $check.Hash.Length -ge 8 -and
                           $check.Hash.Substring(0, 8) -ceq $expectedConflictHash
    }
    if (-not $entryRecognized) {
        $reason = $check.Reason
        if ($check.Valid) {
            if ($check.EntryName -cne $expectedEntryName) {
                $reason = "entry-name-mismatch: expected '$expectedEntryName'; actual '$($check.EntryName)'"
            }
            else {
                $actualConflictHash = '<unavailable>'
                if ($null -ne $check.Hash -and $check.Hash.Length -ge 8) {
                    $actualConflictHash = $check.Hash.Substring(0, 8)
                }
                $reason = "conflict-hash-mismatch: expected '$expectedConflictHash'; actual '$actualConflictHash'"
            }
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

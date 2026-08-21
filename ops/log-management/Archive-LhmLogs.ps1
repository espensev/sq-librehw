[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$SourceDirectory,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ArchiveRoot,
    [ValidateNotNullOrEmpty()][string]$MachineName = $env:COMPUTERNAME,
    [datetime]$Now = (Get-Date)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLogManagement.Common.ps1')

$archiveRootPath = Resolve-LhmFullPath -Path $ArchiveRoot
$safeMachineName = Get-LhmSafeMachineName -MachineName $MachineName
$machineRoot = Resolve-LhmChildPath -Parent $archiveRootPath -Child $safeMachineName
$completedBefore = $Now.Date
$namePattern = '^LibreHardwareMonitorLog-(?<date>\d{4}-\d{2}-\d{2})(?:-[A-Za-z0-9._-]+)?\.csv$'
$pendingPattern = '^(?<original>LibreHardwareMonitorLog-\d{4}-\d{2}-\d{2}(?:-[A-Za-z0-9._-]+)?\.csv)\.pending-delete-[0-9a-f]{32}$'

foreach ($sourceCandidate in $SourceDirectory) {
    $sourcePath = Resolve-LhmFullPath -Path $sourceCandidate
    if (-not [System.IO.Directory]::Exists($sourcePath)) {
        New-LhmLogResult -Action 'Scan' -Status 'Skipped' -Source $sourcePath -Message 'Source directory does not exist.'
        continue
    }

    $orphans = Get-ChildItem -LiteralPath $sourcePath -Filter 'LibreHardwareMonitorLog-*.csv.pending-delete-*' -File
    foreach ($orphan in $orphans) {
        if ($orphan.Name -notmatch $pendingPattern) {
            continue
        }

        $restored = Join-Path $sourcePath $Matches['original']
        try {
            if ([System.IO.File]::Exists($restored)) {
                New-LhmLogResult -Action 'Archive' -Status 'Retained' -Source $orphan.FullName -Destination $restored -Message 'Interrupted removal cannot be restored; original name is occupied.'
                continue
            }

            if ($PSCmdlet.ShouldProcess($orphan.FullName, "Restore interrupted removal to $restored")) {
                Move-Item -LiteralPath $orphan.FullName -Destination $restored
                New-LhmLogResult -Action 'Archive' -Status 'Restored' -Source $orphan.FullName -Destination $restored -Message 'Interrupted removal restored for normal archival.'
            }
            else {
                New-LhmLogResult -Action 'Archive' -Status 'Planned' -Source $orphan.FullName -Destination $restored -Message 'Would restore interrupted removal.'
            }
        }
        catch {
            New-LhmLogResult -Action 'Archive' -Status 'Failed' -Source $orphan.FullName -Destination $restored -Message ('Interrupted removal could not be restored: ' + $_.Exception.Message)
        }
    }

    $files = Get-ChildItem -LiteralPath $sourcePath -Filter 'LibreHardwareMonitorLog-*.csv' -File
    foreach ($file in $files) {
        if ($file.Name -notmatch $namePattern) {
            New-LhmLogResult -Action 'Archive' -Status 'Skipped' -Source $file.FullName -Message 'Filename is not a recognized daily log.'
            continue
        }

        $logDate = [datetime]::MinValue
        $parsed = [datetime]::TryParseExact($Matches['date'],
                                            'yyyy-MM-dd',
                                            [System.Globalization.CultureInfo]::InvariantCulture,
                                            [System.Globalization.DateTimeStyles]::None,
                                            [ref]$logDate)
        if (-not $parsed -or $logDate.Date -ge $completedBefore) {
            New-LhmLogResult -Action 'Archive' -Status 'Skipped' -Source $file.FullName -Message 'Current-day or invalid-date log retained.'
            continue
        }

        if (-not (Test-LhmFileReady -Path $file.FullName)) {
            New-LhmLogResult -Action 'Archive' -Status 'Skipped' -Source $file.FullName -Message 'Log is locked or unreadable.'
            continue
        }

        $temporary = $null
        $destination = $null
        try {
            $month = $logDate.ToString('MM-MMM', [System.Globalization.CultureInfo]::InvariantCulture)
            $destinationDirectory = Join-Path (Join-Path $machineRoot $logDate.ToString('yyyy')) $month
            $destination = Join-Path $destinationDirectory ($file.BaseName + '.zip')
            $sourceLength = $file.Length
            $sourceHash = Get-LhmFileSha256 -Path $file.FullName
            $collisionNote = ''

            if ([System.IO.File]::Exists($destination)) {
                $existing = Test-LhmZipArchive -Path $destination -ExpectedEntryName $file.Name -ExpectedLength $sourceLength -ExpectedHash $sourceHash
                if (-not $existing.Valid) {
                    $collisionNote = " Existing archive mismatched ($($existing.Reason)) and was retained for review."
                    $destination = Join-Path $destinationDirectory ($file.BaseName + '-conflict-' + $sourceHash.Substring(0, 8) + '.zip')
                }
            }

            if ([System.IO.File]::Exists($destination)) {
                $existing = Test-LhmZipArchive -Path $destination -ExpectedEntryName $file.Name -ExpectedLength $sourceLength -ExpectedHash $sourceHash
                if (-not $existing.Valid) {
                    New-LhmLogResult -Action 'Archive' -Status 'Failed' -Source $file.FullName -Destination $destination -Message ('Archive collision: ' + $existing.Reason + $collisionNote)
                    continue
                }

                if (-not $PSCmdlet.ShouldProcess($file.FullName, 'Remove source already present in a verified archive')) {
                    New-LhmLogResult -Action 'Archive' -Status 'Planned' -Source $file.FullName -Destination $destination -Message ('Would remove verified duplicate source.' + $collisionNote)
                    continue
                }

                $removal = Remove-LhmVerifiedSourceFile -Path $file.FullName -ExpectedLength $sourceLength -ExpectedHash $sourceHash
                if ($removal.Removed) {
                    New-LhmLogResult -Action 'Archive' -Status 'Duplicate' -Source $file.FullName -Destination $destination -Message ('Verified duplicate source removed.' + $collisionNote)
                }
                else {
                    New-LhmLogResult -Action 'Archive' -Status 'Failed' -Source $file.FullName -Destination $destination -Message ('Duplicate source retained: ' + $removal.Reason + $collisionNote)
                }
                continue
            }

            if (-not $PSCmdlet.ShouldProcess($file.FullName, "Archive to $destination and remove verified source")) {
                New-LhmLogResult -Action 'Archive' -Status 'Planned' -Source $file.FullName -Destination $destination -Message ('Would create and verify archive.' + $collisionNote)
                continue
            }

            [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
            $temporary = $destination + '.tmp-' + [guid]::NewGuid().ToString('N')
            Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
            Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
            $zip = [System.IO.Compression.ZipFile]::Open($temporary, [System.IO.Compression.ZipArchiveMode]::Create)
            try {
                $entry = $zip.CreateEntry($file.Name, [System.IO.Compression.CompressionLevel]::Optimal)
                $input = [System.IO.File]::Open($file.FullName,
                                                [System.IO.FileMode]::Open,
                                                [System.IO.FileAccess]::Read,
                                                [System.IO.FileShare]::Read)
                try {
                    $output = $entry.Open()
                    try {
                        $input.CopyTo($output)
                    }
                    finally {
                        $output.Dispose()
                    }
                }
                finally {
                    $input.Dispose()
                }
            }
            finally {
                $zip.Dispose()
            }

            $temporaryCheck = Test-LhmZipArchive -Path $temporary -ExpectedEntryName $file.Name -ExpectedLength $sourceLength -ExpectedHash $sourceHash
            if (-not $temporaryCheck.Valid) {
                throw "Temporary archive verification failed: $($temporaryCheck.Reason)"
            }

            Move-Item -LiteralPath $temporary -Destination $destination
            $publishedCheck = Test-LhmZipArchive -Path $destination -ExpectedEntryName $file.Name -ExpectedLength $sourceLength -ExpectedHash $sourceHash
            if (-not $publishedCheck.Valid) {
                if ($publishedCheck.Reason -notlike 'unreadable:*') {
                    Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
                }
                New-LhmLogResult -Action 'Archive' -Status 'Failed' -Source $file.FullName -Destination $destination -Message ("Published archive verification failed: $($publishedCheck.Reason); source retained." + $collisionNote)
                continue
            }

            $removal = Remove-LhmVerifiedSourceFile -Path $file.FullName -ExpectedLength $sourceLength -ExpectedHash $sourceHash
            if (-not $removal.Removed) {
                New-LhmLogResult -Action 'Archive' -Status 'Failed' -Source $file.FullName -Destination $destination -Message ('Verified archive published but source retained: ' + $removal.Reason + $collisionNote)
            }
            elseif ($collisionNote) {
                New-LhmLogResult -Action 'Archive' -Status 'ArchivedConflict' -Source $file.FullName -Destination $destination -Message ('Verified archive published; source removed.' + $collisionNote)
            }
            else {
                New-LhmLogResult -Action 'Archive' -Status 'Archived' -Source $file.FullName -Destination $destination -Message 'Verified archive published; source removed.'
            }
        }
        catch {
            if ($temporary -and [System.IO.File]::Exists($temporary)) {
                Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
            }
            New-LhmLogResult -Action 'Archive' -Status 'Failed' -Source $file.FullName -Destination $destination -Message $_.Exception.Message
        }
    }
}

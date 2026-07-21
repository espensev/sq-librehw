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

foreach ($sourceCandidate in $SourceDirectory) {
    $sourcePath = Resolve-LhmFullPath -Path $sourceCandidate
    if (-not [System.IO.Directory]::Exists($sourcePath)) {
        New-LhmLogResult -Action 'Scan' -Status 'Skipped' -Source $sourcePath -Message 'Source directory does not exist.'
        continue
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

        $month = $logDate.ToString('MM-MMM', [System.Globalization.CultureInfo]::InvariantCulture)
        $destinationDirectory = Join-Path (Join-Path $machineRoot $logDate.ToString('yyyy')) $month
        $destination = Join-Path $destinationDirectory ($file.BaseName + '.zip')
        $sourceLength = $file.Length
        $sourceHash = Get-LhmFileSha256 -Path $file.FullName

        if ([System.IO.File]::Exists($destination)) {
            $existing = Test-LhmZipArchive -Path $destination -ExpectedEntryName $file.Name -ExpectedLength $sourceLength -ExpectedHash $sourceHash
            if (-not $existing.Valid) {
                New-LhmLogResult -Action 'Archive' -Status 'Failed' -Source $file.FullName -Destination $destination -Message ('Archive collision: ' + $existing.Reason)
                continue
            }

            if ($PSCmdlet.ShouldProcess($file.FullName, 'Remove source already present in a verified archive')) {
                Remove-Item -LiteralPath $file.FullName -Force
                New-LhmLogResult -Action 'Archive' -Status 'Duplicate' -Source $file.FullName -Destination $destination -Message 'Verified duplicate source removed.'
            }
            else {
                New-LhmLogResult -Action 'Archive' -Status 'Planned' -Source $file.FullName -Destination $destination -Message 'Would remove verified duplicate source.'
            }
            continue
        }

        if (-not $PSCmdlet.ShouldProcess($file.FullName, "Archive to $destination and remove verified source")) {
            New-LhmLogResult -Action 'Archive' -Status 'Planned' -Source $file.FullName -Destination $destination -Message 'Would create and verify archive.'
            continue
        }

        [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
        $temporary = $destination + '.tmp-' + [guid]::NewGuid().ToString('N')
        try {
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
                throw "Published archive verification failed: $($publishedCheck.Reason)"
            }

            Remove-Item -LiteralPath $file.FullName -Force
            New-LhmLogResult -Action 'Archive' -Status 'Archived' -Source $file.FullName -Destination $destination -Message 'Verified archive published; source removed.'
        }
        catch {
            if ([System.IO.File]::Exists($temporary)) {
                Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
            }
            New-LhmLogResult -Action 'Archive' -Status 'Failed' -Source $file.FullName -Destination $destination -Message $_.Exception.Message
        }
    }
}

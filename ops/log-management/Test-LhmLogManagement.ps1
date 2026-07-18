[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLogManagement.Common.ps1')

function Assert-LhmTest {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

$tempBase = Resolve-LhmFullPath -Path ([System.IO.Path]::GetTempPath())
$testRoot = Join-Path $tempBase ('sq-lhm-log-test-' + [guid]::NewGuid().ToString('N'))
$testRoot = Resolve-LhmFullPath -Path $testRoot
$tempPrefix = $tempBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $testRoot.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use test root outside the system temp directory: $testRoot"
}

$source = Join-Path $testRoot 'source'
$archiveRoot = Join-Path $testRoot 'archive'
$runtime = Join-Path $testRoot 'runtime'
$archiveScript = Join-Path $PSScriptRoot 'Archive-LhmLogs.ps1'
$cleanScript = Join-Path $PSScriptRoot 'Clean-LhmLogArchives.ps1'
$invokeScript = Join-Path $PSScriptRoot 'Invoke-LhmLogManagement.ps1'
$installScript = Join-Path $PSScriptRoot 'Install-LhmLogManagementTask.ps1'
$now = [datetime]::SpecifyKind([datetime]'2030-01-10T12:00:00', [System.DateTimeKind]::Local)

try {
    [System.IO.Directory]::CreateDirectory($source) | Out-Null

    $traversalRejected = $false
    try {
        Get-LhmSafeMachineName -MachineName '..' | Out-Null
    }
    catch {
        $traversalRejected = $true
    }
    Assert-LhmTest $traversalRejected 'machine label must not escape the archive root'

    $completed = Join-Path $source 'LibreHardwareMonitorLog-2030-01-09.csv'
    [System.IO.File]::WriteAllText($completed, "Time,CPU`r`n00:00,42`r`n")
    $results = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
    $destination = Join-Path $archiveRoot 'TEST-HOST\2030\01-Jan\LibreHardwareMonitorLog-2030-01-09.zip'
    $archiveSummary = $results | ConvertTo-Json -Compress
    Assert-LhmTest ($results.Status -contains 'Archived') "completed log should archive: $archiveSummary"
    Assert-LhmTest (-not [System.IO.File]::Exists($completed)) 'verified archived source should be removed'
    Assert-LhmTest ([System.IO.File]::Exists($destination)) 'verified ZIP should be published'
    $check = Test-LhmZipArchive -Path $destination -ExpectedEntryName 'LibreHardwareMonitorLog-2030-01-09.csv'
    Assert-LhmTest $check.Valid 'published ZIP should contain exactly the source entry'

    [System.IO.File]::WriteAllText($completed, "Time,CPU`r`n00:00,42`r`n")
    $duplicate = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
    Assert-LhmTest ($duplicate.Status -contains 'Duplicate') 'exact duplicate should converge'
    Assert-LhmTest (-not [System.IO.File]::Exists($completed)) 'exact duplicate source should be removed'

    [System.IO.File]::WriteAllText($completed, "Time,CPU`r`n00:00,99`r`n")
    $collision = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
    Assert-LhmTest ($collision.Status -contains 'Failed') 'content collision should fail closed'
    Assert-LhmTest ([System.IO.File]::Exists($completed)) 'colliding source should be retained'

    $current = Join-Path $source 'LibreHardwareMonitorLog-2030-01-10.csv'
    [System.IO.File]::WriteAllText($current, "Time,CPU`r`n12:00,45`r`n")
    $currentResult = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
    Assert-LhmTest ([System.IO.File]::Exists($current)) 'current-day log should be retained'
    Assert-LhmTest ($currentResult.Message -contains 'Current-day or invalid-date log retained.') 'current-day retention should be reported'

    $whatIfSource = Join-Path $source 'LibreHardwareMonitorLog-2030-01-08-whatif.csv'
    [System.IO.File]::WriteAllText($whatIfSource, "Time,CPU`r`n00:00,40`r`n")
    $preview = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now -WhatIf)
    Assert-LhmTest ($preview.Status -contains 'Planned') 'archive WhatIf should report planned work'
    Assert-LhmTest ([System.IO.File]::Exists($whatIfSource)) 'archive WhatIf should retain source'
    [System.IO.File]::Delete($whatIfSource)

    $lockedSource = Join-Path $source 'LibreHardwareMonitorLog-2030-01-07-locked.csv'
    [System.IO.File]::WriteAllText($lockedSource, "Time,CPU`r`n00:00,39`r`n")
    $lock = [System.IO.File]::Open($lockedSource, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $locked = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
        Assert-LhmTest ([System.IO.File]::Exists($lockedSource)) 'locked source should be retained'
        Assert-LhmTest ($locked.Message -contains 'Log is locked or unreadable.') 'locked source should be reported'
    }
    finally {
        $lock.Dispose()
    }

    [System.IO.File]::SetLastWriteTimeUtc($destination, [datetime]'2020-01-01T00:00:00Z')
    $retentionPreview = @(& $cleanScript -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -RetentionDays 365 -Now $now -WhatIf)
    Assert-LhmTest ($retentionPreview.Status -contains 'Planned') 'retention WhatIf should report planned removal'
    Assert-LhmTest ([System.IO.File]::Exists($destination)) 'retention WhatIf should preserve archive'
    $retention = @(& $cleanScript -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -RetentionDays 365 -Now $now -Confirm:$false)
    Assert-LhmTest ($retention.Status -contains 'Removed') 'verified expired archive should be removed'
    Assert-LhmTest (-not [System.IO.File]::Exists($destination)) 'expired verified archive should no longer exist'

    $invokeSource = Join-Path $testRoot 'invoke-source'
    $invokeArchive = Join-Path $testRoot 'invoke-archive'
    [System.IO.Directory]::CreateDirectory($invokeSource) | Out-Null
    $invokeDate = (Get-Date).AddDays(-2).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
    $invokeLog = Join-Path $invokeSource "LibreHardwareMonitorLog-$invokeDate.csv"
    [System.IO.File]::WriteAllText($invokeLog, "Time,GPU`r`n00:00,55`r`n")
    $configPath = Join-Path $testRoot 'invoke-config.json'
    $config = [ordered]@{
        Schema = 'sq.lhm-log-management'
        Version = 1
        SourceDirectories = @($invokeSource)
        ArchiveRoot = $invokeArchive
        MachineName = 'TEST-HOST'
        RetentionDays = 365
    }
    [System.IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 4))
    $invokeResult = @(& $invokeScript -ConfigPath $configPath)
    Assert-LhmTest ($invokeResult.Status -contains 'Archived') 'config-driven invoker should archive completed logs'
    Assert-LhmTest (-not [System.IO.File]::Exists($invokeLog)) 'invoker should remove only the verified source'
    $invokeZip = Get-ChildItem -LiteralPath $invokeArchive -Filter '*.zip' -File -Recurse | Select-Object -First 1
    Assert-LhmTest ($null -ne $invokeZip) 'invoker should publish a ZIP'
    [System.IO.File]::SetLastWriteTimeUtc($invokeZip.FullName, [datetime]'2020-01-01T00:00:00Z')
    $invokeCleanup = @(& $invokeScript -ConfigPath $configPath)
    Assert-LhmTest ($invokeCleanup.Status -contains 'Removed') 'invoker should prune without an interactive task prompt'
    Assert-LhmTest (-not [System.IO.File]::Exists($invokeZip.FullName)) 'invoker retention should remove verified expired archive'

    $installPreview = @(& $installScript -RuntimeDirectory $runtime -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -WhatIf)
    Assert-LhmTest ($installPreview.Status -contains 'Planned') 'installer WhatIf should report planned task'
    Assert-LhmTest (-not [System.IO.Directory]::Exists($runtime)) 'installer WhatIf should not create runtime directory'

    Write-Output 'PASS: log-management archive, collision, retention, lock, and installer-preview checks'
}
finally {
    if ([System.IO.Directory]::Exists($testRoot)) {
        $resolvedCleanup = Resolve-LhmFullPath -Path $testRoot
        if (-not $resolvedCleanup.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean test path outside temp: $resolvedCleanup"
        }
        Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force
    }
}

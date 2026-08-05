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
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

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
    Assert-LhmTest ($collision.Status -contains 'ArchivedConflict') 'content collision should divert to a verified conflict archive'
    Assert-LhmTest (-not [System.IO.File]::Exists($completed)) 'diverted source should be removed after conflict verification'
    Assert-LhmTest ([System.IO.File]::Exists($destination)) 'mismatched original archive should be retained for review'
    $conflictZips = @(Get-ChildItem -LiteralPath (Split-Path $destination -Parent) -Filter 'LibreHardwareMonitorLog-2030-01-09-conflict-*.zip' -File)
    Assert-LhmTest ($conflictZips.Count -eq 1) 'conflict archive should be published under a deterministic name'
    $conflictCheck = Test-LhmZipArchive -Path $conflictZips[0].FullName -ExpectedEntryName 'LibreHardwareMonitorLog-2030-01-09.csv'
    Assert-LhmTest $conflictCheck.Valid 'conflict archive should contain exactly the source entry'

    [System.IO.File]::WriteAllText($completed, "Time,CPU`r`n00:00,99`r`n")
    $conflictDuplicate = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
    Assert-LhmTest ($conflictDuplicate.Status -contains 'Duplicate') 'repeated conflict content should converge as a duplicate'
    Assert-LhmTest (-not [System.IO.File]::Exists($completed)) 'repeated conflict source should be removed'
    Assert-LhmTest (@(Get-ChildItem -LiteralPath (Split-Path $destination -Parent) -Filter 'LibreHardwareMonitorLog-2030-01-09-conflict-*.zip' -File).Count -eq 1) 'conflict archive should not multiply'

    $orphan = Join-Path $source ('LibreHardwareMonitorLog-2030-01-05.csv.pending-delete-' + ('0' * 32))
    [System.IO.File]::WriteAllText($orphan, "Time,CPU`r`n00:00,38`r`n")
    $restoredRun = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
    Assert-LhmTest ($restoredRun.Status -contains 'Restored') 'interrupted removal should be restored'
    $orphanZip = Join-Path $archiveRoot 'TEST-HOST\2030\01-Jan\LibreHardwareMonitorLog-2030-01-05.zip'
    Assert-LhmTest ([System.IO.File]::Exists($orphanZip)) 'restored log should archive in the same run'
    Assert-LhmTest (-not [System.IO.File]::Exists((Join-Path $source 'LibreHardwareMonitorLog-2030-01-05.csv'))) 'restored source should be removed after verification'

    $occupied = Join-Path $source 'LibreHardwareMonitorLog-2030-01-04.csv'
    [System.IO.File]::WriteAllText($occupied, "Time,CPU`r`n00:00,36`r`n")
    $occupiedOrphan = Join-Path $source ('LibreHardwareMonitorLog-2030-01-04.csv.pending-delete-' + ('1' * 32))
    [System.IO.File]::WriteAllText($occupiedOrphan, "Time,CPU`r`n00:00,35`r`n")
    $occupiedRun = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
    $occupiedRetained = @($occupiedRun | Where-Object { $_.Status -eq 'Retained' })
    Assert-LhmTest ($occupiedRetained.Count -eq 1) 'occupied interrupted removal should be retained'
    Assert-LhmTest ([System.IO.File]::Exists($occupiedOrphan)) 'occupied interrupted removal should remain on disk'
    Assert-LhmTest (-not [System.IO.File]::Exists($occupied)) 'occupying source should archive normally'
    [System.IO.File]::Delete($occupiedOrphan)

    $lockedOrphan = Join-Path $source ('LibreHardwareMonitorLog-2030-01-03.csv.pending-delete-' + ('2' * 32))
    [System.IO.File]::WriteAllText($lockedOrphan, "Time,CPU`r`n00:00,34`r`n")
    $besideLocked = Join-Path $source 'LibreHardwareMonitorLog-2030-01-02.csv'
    [System.IO.File]::WriteAllText($besideLocked, "Time,CPU`r`n00:00,33`r`n")
    $orphanLock = [System.IO.File]::Open($lockedOrphan, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        $lockedOrphanRun = @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now)
        Assert-LhmTest (@($lockedOrphanRun | Where-Object { $_.Status -eq 'Failed' -and $_.Source -eq $lockedOrphan }).Count -eq 1) 'a locked interrupted removal should fail in isolation'
        Assert-LhmTest ($lockedOrphanRun.Status -contains 'Archived') 'other files should archive despite a locked interrupted removal'
    }
    finally {
        $orphanLock.Dispose()
    }
    [System.IO.File]::Delete($lockedOrphan)

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

    $expiredSource = Join-Path $source 'LibreHardwareMonitorLog-2028-06-15.csv'
    [System.IO.File]::WriteAllText($expiredSource, "Time,CPU`r`n00:00,37`r`n")
    @(& $archiveScript -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -Now $now) | Out-Null
    $expiredZip = Join-Path $archiveRoot 'TEST-HOST\2028\06-Jun\LibreHardwareMonitorLog-2028-06-15.zip'
    Assert-LhmTest ([System.IO.File]::Exists($expiredZip)) 'expired-dated log should archive before retention'
    $retentionPreview = @(& $cleanScript -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -RetentionDays 365 -Now $now -WhatIf)
    Assert-LhmTest ($retentionPreview.Status -contains 'Planned') 'retention WhatIf should report planned removal'
    Assert-LhmTest ([System.IO.File]::Exists($expiredZip)) 'retention WhatIf should preserve archive'
    $retention = @(& $cleanScript -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -RetentionDays 365 -Now $now -Confirm:$false)
    Assert-LhmTest ($retention.Status -contains 'Removed') 'expired log date with a fresh timestamp should be removed'
    Assert-LhmTest (-not [System.IO.File]::Exists($expiredZip)) 'expired verified archive should no longer exist'

    [System.IO.File]::SetLastWriteTimeUtc($destination, [datetime]'2020-01-01T00:00:00Z')
    @(& $cleanScript -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -RetentionDays 365 -Now $now -Confirm:$false) | Out-Null
    Assert-LhmTest ([System.IO.File]::Exists($destination)) 'young log date with an ancient timestamp should be retained'

    $corruptDir = Join-Path $archiveRoot 'TEST-HOST\2027\03-Mar'
    [System.IO.Directory]::CreateDirectory($corruptDir) | Out-Null
    $corrupt = Join-Path $corruptDir 'LibreHardwareMonitorLog-2027-03-05.zip'
    [System.IO.File]::WriteAllText($corrupt, 'not a zip')
    $invalidRun = @(& $cleanScript -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -RetentionDays 365 -Now $now -Confirm:$false)
    Assert-LhmTest ($invalidRun.Status -contains 'RetainedInvalid') 'expired invalid archive should be retained and flagged'
    Assert-LhmTest ([System.IO.File]::Exists($corrupt)) 'expired invalid archive should not be deleted'
    [System.IO.File]::Delete($corrupt)

    $staleTmp = Join-Path $archiveRoot ('TEST-HOST\2030\01-Jan\LibreHardwareMonitorLog-2030-01-02.zip.tmp-' + ('a' * 32))
    [System.IO.File]::WriteAllText($staleTmp, 'partial')
    $freshTmp = Join-Path $archiveRoot ('TEST-HOST\2030\01-Jan\LibreHardwareMonitorLog-2030-01-03.zip.tmp-' + ('b' * 32))
    [System.IO.File]::WriteAllText($freshTmp, 'partial')
    [System.IO.File]::SetLastWriteTimeUtc($freshTmp, $now.ToUniversalTime())
    $sweep = @(& $cleanScript -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -RetentionDays 365 -Now $now -Confirm:$false)
    Assert-LhmTest ($sweep.Status -contains 'TempRemoved') 'orphaned temporary archive should be swept'
    Assert-LhmTest (-not [System.IO.File]::Exists($staleTmp)) 'stale temporary archive should be removed'
    Assert-LhmTest ([System.IO.File]::Exists($freshTmp)) 'recent temporary archive should be retained'
    [System.IO.File]::Delete($freshTmp)

    $invokeSource = Join-Path $testRoot 'invoke-source'
    $invokeArchive = Join-Path $testRoot 'invoke-archive'
    [System.IO.Directory]::CreateDirectory($invokeSource) | Out-Null
    $invokeYoung = (Get-Date).AddDays(-2)
    $invokeOld = (Get-Date).AddDays(-390)
    $invokeLog = Join-Path $invokeSource ('LibreHardwareMonitorLog-' + $invokeYoung.ToString('yyyy-MM-dd', $invariant) + '.csv')
    $invokeOldLog = Join-Path $invokeSource ('LibreHardwareMonitorLog-' + $invokeOld.ToString('yyyy-MM-dd', $invariant) + '.csv')
    [System.IO.File]::WriteAllText($invokeLog, "Time,GPU`r`n00:00,55`r`n")
    [System.IO.File]::WriteAllText($invokeOldLog, "Time,GPU`r`n00:00,54`r`n")
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
    $invokeArchived = @($invokeResult | Where-Object { $_.Status -eq 'Archived' })
    Assert-LhmTest ($invokeArchived.Count -eq 2) 'config-driven invoker should archive completed logs'
    Assert-LhmTest (-not [System.IO.File]::Exists($invokeLog)) 'invoker should remove only verified sources'
    Assert-LhmTest (-not [System.IO.File]::Exists($invokeOldLog)) 'invoker should remove the verified expired-dated source'
    Assert-LhmTest ($invokeResult.Status -contains 'Removed') 'invoker should prune expired log dates without an interactive prompt'
    $invokeYoungZip = Join-Path $invokeArchive ('TEST-HOST\' + $invokeYoung.ToString('yyyy', $invariant) + '\' + $invokeYoung.ToString('MM-MMM', $invariant) + '\LibreHardwareMonitorLog-' + $invokeYoung.ToString('yyyy-MM-dd', $invariant) + '.zip')
    $invokeOldZip = Join-Path $invokeArchive ('TEST-HOST\' + $invokeOld.ToString('yyyy', $invariant) + '\' + $invokeOld.ToString('MM-MMM', $invariant) + '\LibreHardwareMonitorLog-' + $invokeOld.ToString('yyyy-MM-dd', $invariant) + '.zip')
    Assert-LhmTest ([System.IO.File]::Exists($invokeYoungZip)) 'invoker should retain the young archive'
    Assert-LhmTest (-not [System.IO.File]::Exists($invokeOldZip)) 'invoker should remove the expired archive in the same pass'

    $alertSource = Join-Path $testRoot 'alert-source'
    $alertArchive = Join-Path $testRoot 'alert-archive'
    [System.IO.Directory]::CreateDirectory($alertSource) | Out-Null
    $alertDate = (Get-Date).AddDays(-3).ToString('yyyy-MM-dd', $invariant)
    $alertLog = Join-Path $alertSource "LibreHardwareMonitorLog-$alertDate.csv"
    [System.IO.File]::WriteAllText($alertLog, "Time,CPU`r`n00:00,50`r`n")
    $alertConfigPath = Join-Path $testRoot 'alert-config.json'
    $alertConfig = [ordered]@{
        Schema = 'sq.lhm-log-management'
        Version = 1
        SourceDirectories = @($alertSource)
        ArchiveRoot = $alertArchive
        MachineName = 'TEST-HOST'
        RetentionDays = 365
    }
    [System.IO.File]::WriteAllText($alertConfigPath, ($alertConfig | ConvertTo-Json -Depth 4))
    @(& $invokeScript -ConfigPath $alertConfigPath) | Out-Null
    [System.IO.File]::WriteAllText($alertLog, "Time,CPU`r`n00:00,51`r`n")
    $alertMessage = ''
    try {
        & $invokeScript -ConfigPath $alertConfigPath | Out-Null
    }
    catch {
        $alertMessage = $_.Exception.Message
    }
    Assert-LhmTest ($alertMessage -like '*conflict name*') 'conflict archival should raise a one-shot alert'
    Assert-LhmTest (-not [System.IO.File]::Exists($alertLog)) 'conflicted source should still be archived and removed'
    Assert-LhmTest (@(Get-ChildItem -LiteralPath $alertArchive -Filter '*-conflict-*.zip' -File -Recurse).Count -eq 1) 'conflict archive should exist after the alert'

    $badConfigPath = Join-Path $testRoot 'bad-config.json'
    $badConfig = [ordered]@{
        Schema = 'sq.lhm-log-management'
        Version = 1
        SourceDirectories = @($alertSource)
        ArchiveRoot = $alertArchive
        MachineName = 'TEST-HOST'
    }
    [System.IO.File]::WriteAllText($badConfigPath, ($badConfig | ConvertTo-Json -Depth 4))
    $missingMessage = ''
    try {
        & $invokeScript -ConfigPath $badConfigPath | Out-Null
    }
    catch {
        $missingMessage = $_.Exception.Message
    }
    Assert-LhmTest ($missingMessage -like "*missing 'RetentionDays'*") 'absent required property should produce the curated error'

    $installPreview = @(& $installScript -RuntimeDirectory $runtime -SourceDirectory $source -ArchiveRoot $archiveRoot -MachineName 'TEST-HOST' -WhatIf)
    Assert-LhmTest ($installPreview.Status -contains 'Planned') 'installer WhatIf should report planned task'
    Assert-LhmTest (-not [System.IO.Directory]::Exists($runtime)) 'installer WhatIf should not create runtime directory'

    $reconcileRuntime = Join-Path $testRoot 'reconcile-runtime'
    [System.IO.Directory]::CreateDirectory($reconcileRuntime) | Out-Null
    $reconcileConfig = [ordered]@{
        Schema = 'sq.lhm-log-management'
        Version = 1
        SourceDirectories = @($source)
        ArchiveRoot = $archiveRoot
        MachineName = 'TEST-HOST'
        RetentionDays = 365
    }
    [System.IO.File]::WriteAllText((Join-Path $reconcileRuntime 'log-management.json'), ($reconcileConfig | ConvertTo-Json -Depth 4))
    $reconcilePreview = @(& $installScript -RuntimeDirectory $reconcileRuntime -ReconcileFromExistingConfig -WhatIf)
    Assert-LhmTest ($reconcilePreview.Status -contains 'Planned') 'reconcile preview should report the planned task'
    $reconcileMissing = ''
    try {
        & $installScript -RuntimeDirectory (Join-Path $testRoot 'reconcile-missing') -ReconcileFromExistingConfig -WhatIf | Out-Null
    }
    catch {
        $reconcileMissing = $_.Exception.Message
    }
    Assert-LhmTest ($reconcileMissing -like '*existing configuration*') 'reconcile without configuration should fail closed'

    Write-Output 'PASS: log-management archive, conflict, restore, retention, sweep, alert, config, and installer checks'
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

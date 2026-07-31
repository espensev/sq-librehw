[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $CandidateDirectory,

    [string] $InstallRoot = 'E:\SQ_HQ\Monitoring\LibreHW',

    [string] $DataRoot = 'E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor',

    [string] $ManagedStartupTaskPath = '\SevGrp\AdminTask\LibreHW-No-UAC',

    [string] $InitialConfigSource,

    [switch] $AllowStopExactProcess,

    [uri] $HealthUri = 'http://localhost:8085/data.json',

    [ValidateRange(5, 300)]
    [int] $ActivationTimeoutSeconds = 45,

    [string] $LauncherTargetPath =
        'E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1',

    [string] $PublicShimPath = 'E:\SQ_HQ\u-programs\bin\librehw.cmd',

    [switch] $NonLiveTestMode,

    [string] $TestExternalStateRoot,

    [ValidateSet('None', 'AfterPreviousRemoval', 'AfterCandidateInstall', 'AfterTaskStage', 'AfterHealth')]
    [string] $TestFailurePoint = 'None',

    [switch] $TestSimulateCrash
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')

function Invoke-TestFailurePoint {
    param([Parameter(Mandatory)][string] $Point)
    if ($TestFailurePoint -ceq $Point) {
        throw "Injected non-live release failure: $Point"
    }
}

$mode = Assert-LhmOperationMode `
    -InstallRoot $InstallRoot `
    -DataRoot $DataRoot `
    -ManagedStartupTaskPath $ManagedStartupTaskPath `
    -NonLiveTestMode:$NonLiveTestMode
$InstallRoot = $mode.InstallRoot
$DataRoot = $mode.DataRoot
$LauncherTargetPath = Resolve-LhmFullPath -Path $LauncherTargetPath
$PublicShimPath = Resolve-LhmFullPath -Path $PublicShimPath

if ($mode.IsTest) {
    $tempRoot = [System.IO.Path]::GetTempPath()
    if (-not (Test-LhmPathWithin -Path $LauncherTargetPath -Root $tempRoot) -or
        -not (Test-LhmPathWithin -Path $PublicShimPath -Root $tempRoot) -or
        [string]::IsNullOrWhiteSpace($TestExternalStateRoot) -or
        -not (Test-LhmPathWithin -Path $TestExternalStateRoot -Root $tempRoot)) {
        throw 'Non-live launcher, shim, and external-state paths must be beneath the OS temporary directory.'
    }
    $TestExternalStateRoot = Resolve-LhmFullPath -Path $TestExternalStateRoot
    if ($TestSimulateCrash -and $TestFailurePoint -ceq 'None') {
        throw 'TestSimulateCrash requires an explicit TestFailurePoint.'
    }
}
else {
    if (-not (Test-LhmPathEqual -Left $LauncherTargetPath -Right $script:LhmLauncherTargetPath) -or
        -not (Test-LhmPathEqual -Left $PublicShimPath -Right $script:LhmPublicShimPath)) {
        throw 'Production launcher and public shim paths are fixed.'
    }
    if ($TestFailurePoint -cne 'None' -or
        $TestSimulateCrash -or
        -not [string]::IsNullOrWhiteSpace($TestExternalStateRoot)) {
        throw 'Failure injection and test external state are available only in NonLiveTestMode.'
    }
    $null = Assert-LhmVerifiedMachineIdentity
}

$null = Assert-LhmNearestExistingPathAncestry `
    -Path $InstallRoot `
    -Label 'LibreHW install root'
$null = Assert-LhmNearestExistingPathAncestry `
    -Path $DataRoot `
    -Label 'LibreHW data root'
if (Test-Path -LiteralPath $DataRoot) {
    $DataRoot = Assert-LhmNormalDirectoryTree `
        -Path $DataRoot `
        -Label 'LibreHW data root'
}
$LauncherTargetPath = Assert-LhmSafeFileDestination `
    -Path $LauncherTargetPath `
    -Label 'LibreHW launcher'

$PublicShimPath = Assert-LhmNormalFile `
    -Path $PublicShimPath `
    -Label 'Public librehw shim'
$publicShimHash = Get-LhmFileSha256 -Path $PublicShimPath
if (-not $mode.IsTest -and $publicShimHash -cne $script:LhmPublicShimSha256) {
    throw 'Public librehw.cmd does not match the accepted unchanged shim hash.'
}

$canonicalLauncher = Join-Path $PSScriptRoot 'Start-LibreHardwareMonitor.ps1'
$launcherTokens = $null
$launcherErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    $canonicalLauncher,
    [ref]$launcherTokens,
    [ref]$launcherErrors)
if ($launcherErrors.Count -gt 0) {
    throw "Canonical launcher does not parse: $($launcherErrors[0].Message)"
}

$transactionRoot = Join-Path $InstallRoot '.release-transaction'
if (Test-Path -LiteralPath $transactionRoot -PathType Container) {
    if (-not $PSCmdlet.ShouldProcess($InstallRoot, 'Repair interrupted local release transaction')) {
        return
    }
    $null = Repair-LhmInterruptedTransaction `
        -InstallRoot $InstallRoot `
        -LauncherTargetPath $LauncherTargetPath `
        -PublicShimPath $PublicShimPath `
        -NonLiveTestMode:$mode.IsTest `
        -TestExternalStateRoot $TestExternalStateRoot
}
$abandonedPreparations = @(Get-ChildItem `
    -LiteralPath $InstallRoot `
    -Force `
    -Directory `
    -ErrorAction SilentlyContinue | Where-Object {
        Test-LhmOwnedDirectoryLeafName `
            -LeafName $_.Name `
            -RequiredLeafPrefix '.release-preparing-'
    })
if ($abandonedPreparations.Count -gt 0) {
    if (-not $PSCmdlet.ShouldProcess($InstallRoot, 'Remove abandoned pre-mutation release preparation directories')) {
        return
    }
    $null = Remove-LhmAbandonedPreparationDirectories -InstallRoot $InstallRoot
}
Assert-LhmInstalledRootInventory -InstallRoot $InstallRoot

$candidate = Read-LhmReleasePayload -Directory $CandidateDirectory -RequireCandidateShape
if ((Test-LhmPathEqual -Left $candidate.Directory -Right $InstallRoot) -or
    (Test-LhmPathWithin -Path $candidate.Directory -Root $InstallRoot)) {
    throw 'The release candidate must be outside the installed runtime root.'
}

$installed = Get-LhmInstalledPayloadState -InstallRoot $InstallRoot
if ($null -ne $installed -and $installed.Sha256 -ceq $candidate.Sha256) {
    throw "Release '$($candidate.Manifest.releaseId)' is already installed."
}

$rollbackRoot = Join-Path $InstallRoot 'rollback'
$existingRollback = $null
if (Test-Path -LiteralPath $rollbackRoot -PathType Container) {
    $rollbackEntries = @(Get-ChildItem -LiteralPath $rollbackRoot -Force)
    if ($rollbackEntries.Count -gt 0) {
        $existingRollback = Read-LhmReleasePayload -Directory $rollbackRoot -RequireCandidateShape
    }
}
if ($null -eq $installed -and $null -ne $existingRollback) {
    throw 'A rollback payload cannot exist without a current installed release.'
}

$recoveryParent = Join-Path $DataRoot 'release-recovery'
$preStableRecoveryRoot = Join-Path $recoveryParent 'pre-stable-startup'
$reusablePreStableRecovery = $null
$expectedPreStableLauncherBackupSha256 = $null
$expectedPreStableManagedTaskBackupSha256 = $null
if ($mode.IsTest -and $null -eq $installed) {
    if (Test-Path -LiteralPath $LauncherTargetPath -PathType Leaf) {
        $expectedPreStableLauncherBackupSha256 =
            Get-LhmFileSha256 -Path $LauncherTargetPath
    }
    $preStableTestTaskPath = Join-Path $TestExternalStateRoot 'managed-task.json'
    if (Test-Path -LiteralPath $preStableTestTaskPath -PathType Leaf) {
        $expectedPreStableManagedTaskBackupSha256 =
            Get-LhmFileSha256 -Path $preStableTestTaskPath
    }
}
if ($null -eq $installed -and
    (Test-Path -LiteralPath $preStableRecoveryRoot -PathType Container)) {
    $reusablePreStableRecovery = Read-LhmPreStableRecoveryPacket `
        -RecoveryRoot $preStableRecoveryRoot `
        -ExpectedLauncherTargetPath $LauncherTargetPath `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath `
        -ExpectedPublicShimPath $PublicShimPath `
        -ExpectedPublicShimSha256 $publicShimHash `
        -ExpectedLauncherBackupSha256 $expectedPreStableLauncherBackupSha256 `
        -ExpectedManagedTaskBackupSha256 $expectedPreStableManagedTaskBackupSha256 `
        -NonLiveTestMode:$mode.IsTest `
        -TestExternalStateRoot $TestExternalStateRoot `
        -RequireExternalStateMatch
}

$settingsPath = Join-Path $DataRoot $script:LhmSettingsFileName
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    if ([string]::IsNullOrWhiteSpace($InitialConfigSource) -or
        -not (Test-Path -LiteralPath $InitialConfigSource -PathType Leaf) -or
        (Get-Item -LiteralPath $InitialConfigSource).Length -eq 0) {
        throw 'First installation requires an explicit, non-empty -InitialConfigSource.'
    }
}

$runtimeConfigPath = Join-Path $InstallRoot $script:LhmRuntimeConfigName
if (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf) {
    $null = Read-LhmRuntimeConfig `
        -Path $runtimeConfigPath `
        -ExpectedDataRoot $DataRoot `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath
}

$expectedExecutablePath = Join-Path $InstallRoot $script:LhmExecutableName
if (-not $mode.IsTest) {
    Assert-LhmDesktopRuntime
    $null = Assert-LhmProcessStateForRelease `
        -ExpectedExecutablePath $expectedExecutablePath `
        -AllowStopExactProcess:$AllowStopExactProcess
}

$action = "Promote verified release '$($candidate.Manifest.releaseId)' with one rollback slot"
if (-not $PSCmdlet.ShouldProcess($InstallRoot, $action)) {
    return
}

$state = @{
    operation = 'promote'
    phase = 'initializing'
    installRoot = $InstallRoot
    launcherTargetPath = $LauncherTargetPath
    publicShimPath = $PublicShimPath
    publicShimSha256 = $publicShimHash
    hadCurrent = $null -ne $installed
    hadRollback = $null -ne $existingRollback
    hadLauncher = Test-Path -LiteralPath $LauncherTargetPath -PathType Leaf
    managedTaskExisted = $false
    createdAt = [DateTimeOffset]::UtcNow.ToString('o')
}
$activationStarted = $false
$preparingRoot = Join-Path $InstallRoot ".release-preparing-$([guid]::NewGuid().ToString('N'))"

try {
    if (-not $mode.IsTest) {
        Stop-LhmExactProcessForRelease `
            -ExpectedExecutablePath $expectedExecutablePath `
            -AllowStopExactProcess:$AllowStopExactProcess
    }

    [System.IO.Directory]::CreateDirectory($InstallRoot) | Out-Null
    Assert-LhmNoReparsePathAncestry -Path $InstallRoot -Label 'LibreHW install root'
    [System.IO.Directory]::CreateDirectory($DataRoot) | Out-Null
    Assert-LhmNoReparsePathAncestry -Path $DataRoot -Label 'LibreHW data root'
    [System.IO.Directory]::CreateDirectory($rollbackRoot) | Out-Null
    Assert-LhmNoReparsePathAncestry `
        -Path $rollbackRoot `
        -Label 'LibreHW rollback root'
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $LauncherTargetPath)) | Out-Null
    $LauncherTargetPath = Assert-LhmSafeFileDestination `
        -Path $LauncherTargetPath `
        -Label 'LibreHW launcher'
    if ($mode.IsTest) {
        [System.IO.Directory]::CreateDirectory($TestExternalStateRoot) | Out-Null
    }
    $null = Initialize-LhmDataRoot -DataRoot $DataRoot -InitialConfigSource $InitialConfigSource
    if (Test-Path -LiteralPath $recoveryParent) {
        Assert-LhmNoReparsePathAncestry `
            -Path $recoveryParent `
            -Label 'LibreHW recovery parent'
    }
    $null = Ensure-LhmRuntimeConfig `
        -InstallRoot $InstallRoot `
        -DataRoot $DataRoot `
        -ManagedStartupTaskPath $ManagedStartupTaskPath

    [System.IO.Directory]::CreateDirectory($preparingRoot) | Out-Null
    Assert-LhmNoReparsePathAncestry `
        -Path $preparingRoot `
        -Label 'Release preparation root'
    if ($null -ne $installed) {
        $null = Copy-LhmPayloadPair `
            -SourceDirectory $InstallRoot `
            -DestinationDirectory (Join-Path $preparingRoot 'previous-current')
    }
    if ($null -ne $existingRollback) {
        $null = Copy-LhmPayloadPair `
            -SourceDirectory $rollbackRoot `
            -DestinationDirectory (Join-Path $preparingRoot 'previous-rollback')
    }
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate.Directory `
        -DestinationDirectory (Join-Path $preparingRoot 'candidate')
    if ($state.hadLauncher) {
        Copy-Item `
            -LiteralPath $LauncherTargetPath `
            -Destination (Join-Path $preparingRoot 'launcher-backup.ps1')
    }

    if ($mode.IsTest) {
        $managedStatePath = Join-Path $TestExternalStateRoot 'managed-task.json'
        $state.managedTaskExisted = Test-Path -LiteralPath $managedStatePath -PathType Leaf
        if ($state.managedTaskExisted) {
            Copy-Item `
                -LiteralPath $managedStatePath `
                -Destination (Join-Path $preparingRoot 'managed-task.test.json')
        }
    }
    else {
        $state.managedTaskExisted = Export-LhmScheduledTaskSnapshot `
            -TaskPath '\SevGrp\AdminTask\' `
            -TaskName 'LibreHW-No-UAC' `
            -SnapshotPath (Join-Path $preparingRoot 'managed-task.xml')
    }
    $state.phase = 'prepared'
    Write-LhmTransactionState -TransactionRoot $preparingRoot -State $state
    Move-Item -LiteralPath $preparingRoot -Destination $transactionRoot
    $transactionRoot = Assert-LhmNormalDirectoryTree `
        -Path $transactionRoot `
        -Label 'Release transaction'

    Clear-LhmPayloadPair -Directory $InstallRoot
    Clear-LhmPayloadPair -Directory $rollbackRoot
    $state.phase = 'previous-removed'
    Write-LhmTransactionState -TransactionRoot $transactionRoot -State $state
    Invoke-TestFailurePoint -Point 'AfterPreviousRemoval'

    if ($null -ne $installed) {
        $null = Copy-LhmPayloadPair `
            -SourceDirectory (Join-Path $transactionRoot 'previous-current') `
            -DestinationDirectory $rollbackRoot
    }
    $installedCandidate = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $transactionRoot 'candidate') `
        -DestinationDirectory $InstallRoot `
        -DestinationMayContainOtherEntries
    $state.phase = 'candidate-installed'
    Write-LhmTransactionState -TransactionRoot $transactionRoot -State $state
    Invoke-TestFailurePoint -Point 'AfterCandidateInstall'

    $LauncherTargetPath = Assert-LhmSafeFileDestination `
        -Path $LauncherTargetPath `
        -Label 'LibreHW launcher'
    $launcherStage = Join-Path (Split-Path -Parent $LauncherTargetPath) ".Start-LibreHardwareMonitor.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        Copy-Item -LiteralPath $canonicalLauncher -Destination $launcherStage
        Move-Item -LiteralPath $launcherStage -Destination $LauncherTargetPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $launcherStage) {
            Remove-Item -LiteralPath $launcherStage -Force
        }
    }

    if ($mode.IsTest) {
        [ordered]@{
            taskPath = $ManagedStartupTaskPath
            execute = $expectedExecutablePath
            workingDirectory = $InstallRoot
            runLevel = 'Highest'
            logonType = 'InteractiveToken'
            trigger = 'LogonAndOnDemand'
            multipleInstances = 'IgnoreNew'
            startWhenAvailable = $true
            allowHardTerminate = $false
        } | ConvertTo-Json | Set-Content `
            -LiteralPath (Join-Path $TestExternalStateRoot 'managed-task.json') `
            -Encoding UTF8
    }
    else {
        Register-LhmManagedTask `
            -ExecutablePath $expectedExecutablePath `
            -InstallRoot $InstallRoot `
            -ManagedStartupTaskPath $ManagedStartupTaskPath
    }
    $state.phase = 'task-staged'
    Write-LhmTransactionState -TransactionRoot $transactionRoot -State $state
    Invoke-TestFailurePoint -Point 'AfterTaskStage'

    if (-not $mode.IsTest) {
        $activationStarted = $true
        $null = Start-LhmManagedTaskAndWait `
            -ManagedStartupTaskPath $ManagedStartupTaskPath `
            -ExpectedExecutablePath $expectedExecutablePath `
            -TimeoutSeconds $ActivationTimeoutSeconds
        Assert-LhmHttpHealth -HealthUri $HealthUri -TimeoutSeconds $ActivationTimeoutSeconds
        & $LauncherTargetPath -StartupTimeoutSeconds $ActivationTimeoutSeconds
    }

    if ($null -eq $installed) {
        if ($null -eq $reusablePreStableRecovery) {
            $preparingRecovery =
                Join-Path $recoveryParent ".pre-stable-startup-$([guid]::NewGuid().ToString('N'))"
            $null = New-LhmSafeRecoveryPreparationDirectory `
                -DataRoot $DataRoot `
                -RecoveryParent $recoveryParent `
                -PreparationPath $preparingRecovery `
                -RequiredLeafPrefix '.pre-stable-startup-'
            try {
                if ([bool]$state.hadLauncher) {
                    Copy-Item `
                        -LiteralPath (Join-Path $transactionRoot 'launcher-backup.ps1') `
                        -Destination (Join-Path $preparingRecovery 'launcher-backup.ps1')
                }
                $taskBackupName = if ($mode.IsTest) {
                    'managed-task.test.json'
                }
                else {
                    'managed-task.xml'
                }
                if ([bool]$state.managedTaskExisted) {
                    Copy-Item `
                        -LiteralPath (Join-Path $transactionRoot $taskBackupName) `
                        -Destination (Join-Path $preparingRecovery $taskBackupName)
                }

                [ordered]@{
                    schema = 'sq.librehw.pre-stable-startup-recovery.v1'
                    createdAt = [DateTimeOffset]::UtcNow.ToString('o')
                    launcherTargetPath = $LauncherTargetPath
                    launcherExisted = [bool]$state.hadLauncher
                    launcherBackup = if ([bool]$state.hadLauncher) { 'launcher-backup.ps1' } else { $null }
                    launcherSha256 = if ([bool]$state.hadLauncher) {
                        Get-LhmFileSha256 -Path (Join-Path $preparingRecovery 'launcher-backup.ps1')
                    } else { $null }
                    managedTaskPath = $ManagedStartupTaskPath
                    managedTaskExisted = [bool]$state.managedTaskExisted
                    managedTaskBackup = if ([bool]$state.managedTaskExisted) { $taskBackupName } else { $null }
                    managedTaskSha256 = if ([bool]$state.managedTaskExisted) {
                        Get-LhmFileSha256 -Path (Join-Path $preparingRecovery $taskBackupName)
                    } else { $null }
                    publicShimPath = $PublicShimPath
                    publicShimSha256 = $publicShimHash
                } | ConvertTo-Json -Depth 5 | Set-Content `
                    -LiteralPath (Join-Path $preparingRecovery 'recovery.json') `
                    -Encoding UTF8
                $null = Read-LhmPreStableRecoveryPacket `
                    -RecoveryRoot $preparingRecovery `
                    -ExpectedLauncherTargetPath $LauncherTargetPath `
                    -ExpectedManagedTaskPath $ManagedStartupTaskPath `
                    -ExpectedPublicShimPath $PublicShimPath `
                    -ExpectedPublicShimSha256 $publicShimHash `
                    -ExpectedLauncherBackupSha256 $expectedPreStableLauncherBackupSha256 `
                    -ExpectedManagedTaskBackupSha256 $expectedPreStableManagedTaskBackupSha256 `
                    -NonLiveTestMode:$mode.IsTest `
                    -TestExternalStateRoot $TestExternalStateRoot
                Move-Item -LiteralPath $preparingRecovery -Destination $preStableRecoveryRoot
            }
            finally {
                if (Test-Path -LiteralPath $preparingRecovery -PathType Container) {
                    Remove-LhmOwnedDirectory `
                        -Path $preparingRecovery `
                        -ExpectedParent $recoveryParent `
                        -RequiredLeafPrefix '.pre-stable-startup-'
                }
            }
        }
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $preStableRecoveryRoot `
            -ExpectedLauncherTargetPath $LauncherTargetPath `
            -ExpectedManagedTaskPath $ManagedStartupTaskPath `
            -ExpectedPublicShimPath $PublicShimPath `
            -ExpectedPublicShimSha256 $publicShimHash `
            -ExpectedLauncherBackupSha256 $expectedPreStableLauncherBackupSha256 `
            -ExpectedManagedTaskBackupSha256 $expectedPreStableManagedTaskBackupSha256 `
            -NonLiveTestMode:$mode.IsTest `
            -TestExternalStateRoot $TestExternalStateRoot
    }

    $state.phase = 'health-accepted'
    Write-LhmTransactionState -TransactionRoot $transactionRoot -State $state
    Invoke-TestFailurePoint -Point 'AfterHealth'

    if ((Get-LhmFileSha256 -Path $PublicShimPath) -cne $publicShimHash) {
        throw 'Public librehw.cmd changed during release promotion.'
    }
    $state.phase = 'committed'
    Write-LhmTransactionState -TransactionRoot $transactionRoot -State $state
    $null = Repair-LhmInterruptedTransaction `
        -InstallRoot $InstallRoot `
        -LauncherTargetPath $LauncherTargetPath `
        -PublicShimPath $PublicShimPath `
        -NonLiveTestMode:$mode.IsTest `
        -TestExternalStateRoot $TestExternalStateRoot
    Assert-LhmInstalledRootInventory -InstallRoot $InstallRoot

    [pscustomobject]@{
        InstalledReleaseId = $installedCandidate.Manifest.releaseId
        InstalledExecutable = $installedCandidate.ExecutablePath
        InstalledSha256 = $installedCandidate.Sha256
        RuntimeConfig = $runtimeConfigPath
        DataRoot = $DataRoot
        Launcher = $LauncherTargetPath
        RollbackReleaseId = if ($null -ne $installed) { $installed.Manifest.releaseId } else { $null }
        Activated = -not $mode.IsTest
        TestMode = $mode.IsTest
    }
}
catch {
    $failure = $_
    if ($mode.IsTest -and $TestSimulateCrash) {
        throw $failure
    }
    if (-not $mode.IsTest -and $activationStarted) {
        try {
            Stop-LhmExactProcessForRelease `
                -ExpectedExecutablePath $expectedExecutablePath `
                -AllowStopExactProcess
        }
        catch {
            throw "Promotion failed and the candidate could not be stopped safely. Original failure: $($failure.Exception.Message). Stop failure: $($_.Exception.Message)"
        }
    }

    if (Test-Path -LiteralPath $transactionRoot -PathType Container) {
        $null = Repair-LhmInterruptedTransaction `
            -InstallRoot $InstallRoot `
            -LauncherTargetPath $LauncherTargetPath `
            -PublicShimPath $PublicShimPath `
            -NonLiveTestMode:$mode.IsTest `
            -TestExternalStateRoot $TestExternalStateRoot
    }
    if (Test-Path -LiteralPath $preparingRoot -PathType Container) {
        Remove-LhmOwnedDirectory `
            -Path $preparingRoot `
            -ExpectedParent $InstallRoot `
            -RequiredLeafPrefix '.release-preparing-'
    }

    if (-not $mode.IsTest -and $null -ne $installed) {
        Register-LhmManagedTask `
            -ExecutablePath $expectedExecutablePath `
            -InstallRoot $InstallRoot `
            -ManagedStartupTaskPath $ManagedStartupTaskPath
        $null = Start-LhmManagedTaskAndWait `
            -ManagedStartupTaskPath $ManagedStartupTaskPath `
            -ExpectedExecutablePath $expectedExecutablePath `
            -TimeoutSeconds $ActivationTimeoutSeconds
        Assert-LhmHttpHealth -HealthUri $HealthUri -TimeoutSeconds $ActivationTimeoutSeconds
    }
    throw $failure
}

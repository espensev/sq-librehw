[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string] $InstallRoot = 'E:\SQ_HQ\Monitoring\LibreHW',

    [string] $DataRoot = 'E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor',

    [string] $ManagedStartupTaskPath = '\SevGrp\AdminTask\LibreHW-No-UAC',

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
    [string] $TestFailurePoint = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')

function Invoke-TestFailurePoint {
    param([Parameter(Mandatory)][string] $Point)
    if ($TestFailurePoint -ceq $Point) {
        throw "Injected non-live rollback failure: $Point"
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
}
else {
    if (-not (Test-LhmPathEqual -Left $LauncherTargetPath -Right $script:LhmLauncherTargetPath) -or
        -not (Test-LhmPathEqual -Left $PublicShimPath -Right $script:LhmPublicShimPath)) {
        throw 'Production launcher and public shim paths are fixed.'
    }
    if ($TestFailurePoint -cne 'None' -or
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

$current = Get-LhmInstalledPayloadState -InstallRoot $InstallRoot
if ($null -eq $current) {
    throw "No current release is installed at '$InstallRoot'."
}
$rollbackRoot = Join-Path $InstallRoot 'rollback'
$rollback = Read-LhmReleasePayload -Directory $rollbackRoot -RequireCandidateShape
if ($current.Sha256 -ceq $rollback.Sha256) {
    throw 'Current and rollback payloads are identical.'
}

$runtimeConfigPath = Join-Path $InstallRoot $script:LhmRuntimeConfigName
if (-not (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf)) {
    throw "Installed runtime config not found: '$runtimeConfigPath'."
}
$null = Read-LhmRuntimeConfig `
    -Path $runtimeConfigPath `
    -ExpectedDataRoot $DataRoot `
    -ExpectedManagedTaskPath $ManagedStartupTaskPath

$expectedExecutablePath = Join-Path $InstallRoot $script:LhmExecutableName
if (-not $mode.IsTest) {
    Assert-LhmDesktopRuntime
    $null = Assert-LhmProcessStateForRelease `
        -ExpectedExecutablePath $expectedExecutablePath `
        -AllowStopExactProcess:$AllowStopExactProcess
}

$action = "Swap current '$($current.Manifest.releaseId)' with rollback '$($rollback.Manifest.releaseId)'"
if (-not $PSCmdlet.ShouldProcess($InstallRoot, $action)) {
    return
}

$state = @{
    operation = 'rollback'
    phase = 'initializing'
    installRoot = $InstallRoot
    launcherTargetPath = $LauncherTargetPath
    publicShimPath = $PublicShimPath
    publicShimSha256 = $publicShimHash
    hadCurrent = $true
    hadRollback = $true
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

    [System.IO.Directory]::CreateDirectory($preparingRoot) | Out-Null
    Assert-LhmNoReparsePathAncestry `
        -Path $preparingRoot `
        -Label 'Release preparation root'
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $InstallRoot `
        -DestinationDirectory (Join-Path $preparingRoot 'previous-current')
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $rollbackRoot `
        -DestinationDirectory (Join-Path $preparingRoot 'previous-rollback')
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

    $restored = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $transactionRoot 'previous-rollback') `
        -DestinationDirectory $InstallRoot `
        -DestinationMayContainOtherEntries
    $newRollback = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $transactionRoot 'previous-current') `
        -DestinationDirectory $rollbackRoot
    $state.phase = 'candidate-installed'
    Write-LhmTransactionState -TransactionRoot $transactionRoot -State $state
    Invoke-TestFailurePoint -Point 'AfterCandidateInstall'

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
    $state.phase = 'health-accepted'
    Write-LhmTransactionState -TransactionRoot $transactionRoot -State $state
    Invoke-TestFailurePoint -Point 'AfterHealth'

    if ((Get-LhmFileSha256 -Path $PublicShimPath) -cne $publicShimHash) {
        throw 'Public librehw.cmd changed during rollback.'
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
        InstalledReleaseId = $restored.Manifest.releaseId
        InstalledExecutable = $restored.ExecutablePath
        InstalledSha256 = $restored.Sha256
        RollbackReleaseId = $newRollback.Manifest.releaseId
        Activated = -not $mode.IsTest
        TestMode = $mode.IsTest
    }
}
catch {
    $failure = $_
    if (-not $mode.IsTest -and $activationStarted) {
        try {
            Stop-LhmExactProcessForRelease `
                -ExpectedExecutablePath $expectedExecutablePath `
                -AllowStopExactProcess
        }
        catch {
            throw "Rollback failed and the candidate could not be stopped safely. Original failure: $($failure.Exception.Message). Stop failure: $($_.Exception.Message)"
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

    if (-not $mode.IsTest) {
        $null = Start-LhmManagedTaskAndWait `
            -ManagedStartupTaskPath $ManagedStartupTaskPath `
            -ExpectedExecutablePath $expectedExecutablePath `
            -TimeoutSeconds $ActivationTimeoutSeconds
        Assert-LhmHttpHealth -HealthUri $HealthUri -TimeoutSeconds $ActivationTimeoutSeconds
    }
    throw $failure
}

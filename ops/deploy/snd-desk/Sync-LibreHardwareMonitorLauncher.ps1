#Requires -Version 5.1

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidateSet('Plan', 'Apply', 'Validate')]
    [string] $Mode = 'Plan',

    [string] $CanonicalLauncherPath,

    [string] $CanonicalShimPath,

    [string] $RuntimeLauncherPath =
        'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1',

    [string] $RuntimeExecutablePath =
        'E:\Monitoring\LibreHW\Runtime\LibreHardwareMonitor.Windows.Forms.exe',

    [string] $PublicShimPath,

    [string] $CentralLauncherPath,

    [string] $CentralLauncherAuthorityReceiptPath,

    [string] $ReceiptPath,

    [string] $RollbackRoot,

    [string] $ManagedStartupTaskPath = '\SevGrp\AdminTask\LibreHW-No-UAC',

    [switch] $NonLiveTestMode,

    [string] $TestManagedTaskStatePath,

    [string] $TestIdentityVerifierPath,

    [ValidateRange(1, 20)]
    [int] $RollbackRetentionCount = 5,

    [ValidateSet('None', 'launcher', 'publicShim')]
    [string] $TestCorruptRollbackBackupRole = 'None',

    [ValidateSet(
        'None',
        'AfterLauncherDeployment',
        'AfterPublicShimDeployment',
        'AfterReceiptDeployment'
    )]
    [string] $TestFailurePoint = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')

if ([string]::IsNullOrWhiteSpace($CanonicalLauncherPath)) {
    $CanonicalLauncherPath = Join-Path $PSScriptRoot 'Start-LibreHardwareMonitor.ps1'
}
if ([string]::IsNullOrWhiteSpace($CanonicalShimPath)) {
    $CanonicalShimPath = Join-Path $PSScriptRoot 'librehw.cmd'
}

$PublicShimPath = Resolve-LhmUnspecifiedProductionPath `
    -ParameterName 'PublicShimPath' `
    -CurrentValue $PublicShimPath `
    -Resolver { Get-LhmProductionPublicShimPath } `
    -NonLiveTestMode:$NonLiveTestMode
$CentralLauncherPath = Resolve-LhmUnspecifiedProductionPath `
    -ParameterName 'CentralLauncherPath' `
    -CurrentValue $CentralLauncherPath `
    -Resolver { Get-LhmProductionCentralLauncherPath } `
    -NonLiveTestMode:$NonLiveTestMode
$CentralLauncherAuthorityReceiptPath = Resolve-LhmUnspecifiedProductionPath `
    -ParameterName 'CentralLauncherAuthorityReceiptPath' `
    -CurrentValue $CentralLauncherAuthorityReceiptPath `
    -Resolver { Get-LhmProductionRunWReceiptPath } `
    -NonLiveTestMode:$NonLiveTestMode
$ReceiptPath = Resolve-LhmUnspecifiedProductionPath `
    -ParameterName 'ReceiptPath' `
    -CurrentValue $ReceiptPath `
    -Resolver { Get-LhmProductionLauncherConvergenceReceiptPath } `
    -NonLiveTestMode:$NonLiveTestMode
$RollbackRoot = Resolve-LhmUnspecifiedProductionPath `
    -ParameterName 'RollbackRoot' `
    -CurrentValue $RollbackRoot `
    -Resolver { Get-LhmProductionLauncherConvergenceRollbackRoot } `
    -NonLiveTestMode:$NonLiveTestMode

$script:LauncherConvergenceSchema = 'sq.librehw.launcher-convergence.v2'
$script:LauncherConvergenceMigrationSchema =
    'sq.librehw.launcher-convergence.v1'
$script:ProductionCanonicalLauncherPath =
    Join-Path $PSScriptRoot 'Start-LibreHardwareMonitor.ps1'
$script:ProductionCanonicalShimPath = Join-Path $PSScriptRoot 'librehw.cmd'
$script:ProductionRuntimeLauncherPath =
    'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
$script:ProductionRuntimeExecutablePath =
    'E:\Monitoring\LibreHW\Runtime\LibreHardwareMonitor.Windows.Forms.exe'
$script:ProductionCentralLauncherSourceRoot = 'D:\Devtools\runW'
$script:ProductionCentralLauncherSourceCommit =
    'b5cda6dc0d98c8a6be28a6b27c347d780280fe4a'
$script:ProductionCentralLauncherArtifactPath = 'D:\Devtools\runW\bin\runw.exe'
$script:ProductionCentralLauncherArtifactSha256 =
    '987ECB227C630AB810EEDD7D5DC8622A5720ECE77261888C822BC20AE5FEF9C5'
$script:ProductionCentralLauncherArtifactLength = [Int64]316928
$script:ProductionCentralLauncherArtifactVersion = '1.3.1'

function Get-LhmLauncherExpectedShimText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $CentralLauncherPath,
        [Parameter(Mandatory)][string] $LauncherPath
    )

    $shimCentralLauncherPath = if ($NonLiveTestMode) {
        $CentralLauncherPath
    }
    else {
        Get-LhmPublicShimCentralLauncherToken
    }

    return "@echo off`r`n" +
        "`"$shimCentralLauncherPath`" /wait /quiet /cwd:- " +
        "`"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`" " +
        "-NoLogo -NoProfile -ExecutionPolicy Bypass " +
        "-File `"$LauncherPath`" %*`r`n" +
        "exit /b %ERRORLEVEL%`r`n"
}

function ConvertTo-LhmLauncherNormalizedText {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Text)

    return ($Text -replace "`r?`n", "`r`n")
}

function Get-LhmLauncherTextSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Text)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::ASCII.GetBytes($Text)
        return ([System.BitConverter]::ToString(
            $sha256.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Assert-LhmLauncherConvergenceScope {
    [CmdletBinding()]
    param()

    $paths = [ordered]@{
        CanonicalLauncherPath = Resolve-LhmFullPath -Path $CanonicalLauncherPath
        CanonicalShimPath = Resolve-LhmFullPath -Path $CanonicalShimPath
        RuntimeLauncherPath = Resolve-LhmFullPath -Path $RuntimeLauncherPath
        RuntimeExecutablePath = Resolve-LhmFullPath -Path $RuntimeExecutablePath
        PublicShimPath = Resolve-LhmFullPath -Path $PublicShimPath
        CentralLauncherPath = Resolve-LhmFullPath -Path $CentralLauncherPath
        CentralLauncherAuthorityReceiptPath =
            Resolve-LhmFullPath -Path $CentralLauncherAuthorityReceiptPath
        ReceiptPath = Resolve-LhmFullPath -Path $ReceiptPath
        RollbackRoot = Resolve-LhmFullPath -Path $RollbackRoot
    }

    if ($ManagedStartupTaskPath -cne $script:LhmManagedTaskPath) {
        throw "Unsupported managed task path '$ManagedStartupTaskPath'."
    }

    if ($NonLiveTestMode) {
        if ([string]::IsNullOrWhiteSpace($TestManagedTaskStatePath) -or
            [string]::IsNullOrWhiteSpace($TestIdentityVerifierPath)) {
            throw 'NonLiveTestMode requires explicit task-state and identity-verifier fixture paths.'
        }
        $testRoot = Resolve-LhmFullPath -Path ([System.IO.Path]::GetTempPath())
        foreach ($entry in @($paths.GetEnumerator())) {
            if (-not (Test-LhmPathWithin -Path $entry.Value -Root $testRoot)) {
                throw "NonLiveTestMode path '$($entry.Key)' must be beneath the OS temporary directory."
            }
        }
        foreach ($fixturePath in @($TestManagedTaskStatePath, $TestIdentityVerifierPath)) {
            if (-not (Test-LhmPathWithin -Path $fixturePath -Root $testRoot)) {
                throw 'NonLiveTestMode fixture paths must be beneath the OS temporary directory.'
            }
        }
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($TestManagedTaskStatePath) -or
            -not [string]::IsNullOrWhiteSpace($TestIdentityVerifierPath) -or
            $TestFailurePoint -cne 'None' -or
            $TestCorruptRollbackBackupRole -cne 'None') {
            throw 'Test-only launcher convergence parameters require NonLiveTestMode.'
        }
        $expected = [ordered]@{
            CanonicalLauncherPath = $script:ProductionCanonicalLauncherPath
            CanonicalShimPath = $script:ProductionCanonicalShimPath
            RuntimeLauncherPath = $script:ProductionRuntimeLauncherPath
            RuntimeExecutablePath = $script:ProductionRuntimeExecutablePath
            PublicShimPath = Get-LhmProductionPublicShimPath
            CentralLauncherPath = Get-LhmProductionCentralLauncherPath
            CentralLauncherAuthorityReceiptPath =
                Get-LhmProductionRunWReceiptPath
            ReceiptPath = Get-LhmProductionLauncherConvergenceReceiptPath
            RollbackRoot = Get-LhmProductionLauncherConvergenceRollbackRoot
        }
        foreach ($entry in @($expected.GetEnumerator())) {
            if (-not (Test-LhmPathEqual -Left $paths[$entry.Key] -Right $entry.Value)) {
                throw "Canonical Apply path '$($entry.Key)' must be '$($entry.Value)'."
            }
        }
    }

    return [pscustomobject]$paths
}

function Assert-LhmLauncherApplyIdentity {
    [CmdletBinding()]
    param()

    $verifierPath = if ($NonLiveTestMode) {
        Resolve-LhmFullPath -Path $TestIdentityVerifierPath
    }
    else {
        [System.IO.Path]::Combine(
            [Environment]::GetFolderPath('LocalApplicationData'),
            'common_dev\v2\Test-LocalMachineIdentity.ps1')
    }
    if (-not (Test-Path -LiteralPath $verifierPath -PathType Leaf)) {
        throw "Machine identity verifier not found: '$verifierPath'."
    }
    $identity = @(& $verifierPath)
    if ($identity.Count -ne 1) {
        throw 'Machine identity verifier must return exactly one result.'
    }
    if ([string]$identity[0].status -cne 'VERIFIED' -or
        [string]$identity[0].machineId -cne $script:LhmExpectedMachineId -or
        [string]$identity[0].instanceId -cne $script:LhmExpectedInstanceId) {
        throw "Launcher Apply is restricted to verified machine '$($script:LhmExpectedMachineId)' instance '$($script:LhmExpectedInstanceId)'."
    }
    return $identity[0]
}

function Read-LhmLauncherAuthorityReceipt {
    [CmdletBinding()]
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    $receiptPath = Assert-LhmNormalFile `
        -Path $Scope.CentralLauncherAuthorityReceiptPath `
        -Label 'Central RunW authority receipt'
    try {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "Central RunW authority receipt is invalid: $($_.Exception.Message)"
    }
    $launcherPath = Assert-LhmNormalFile `
        -Path $Scope.CentralLauncherPath `
        -Label 'Installed central RunW launcher'
    $launcherHash = (Get-LhmFileSha256 -Path $launcherPath).ToUpperInvariant()
    $launcherLength = (Get-Item -LiteralPath $launcherPath -Force).Length
    $expectedLauncherHash = if ($NonLiveTestMode) {
        $launcherHash
    }
    else {
        $script:ProductionCentralLauncherArtifactSha256
    }
    $expectedLauncherLength = if ($NonLiveTestMode) {
        [Int64]$launcherLength
    }
    else {
        $script:ProductionCentralLauncherArtifactLength
    }
    $destinations = @($receipt.Destinations)
    $destination = if ($destinations.Count -eq 1) { $destinations[0] } else { $null }
    $legacyDestinationShape = Test-LhmLauncherExactProperties `
        -Value $destination `
        -Expected @('Path', 'Sha256')
    $currentDestinationShape = Test-LhmLauncherExactProperties `
        -Value $destination `
        -Expected @('Path', 'Existed', 'BeforeHash', 'Sha256')
    $currentDestinationEvidence = $false
    if ($currentDestinationShape -and $destination.Existed -is [System.Boolean]) {
        $currentDestinationEvidence =
            ([bool]$destination.Existed -and
                [string]$destination.BeforeHash -match '^[0-9A-F]{64}$') -or
            (-not [bool]$destination.Existed -and
                [string]::IsNullOrWhiteSpace([string]$destination.BeforeHash))
    }
    if (-not (Test-LhmLauncherExactProperties `
            -Value $receipt.Source `
            -Expected @('Root', 'Commit', 'Dirty', 'Inputs')) -or
        -not (Test-LhmLauncherExactProperties `
            -Value $receipt.Artifact `
            -Expected @('Path', 'Sha256', 'Length', 'Version')) -or
        [string]$receipt.Schema -cne 'runw.deployment.v2' -or
        [string]$receipt.Operation -cne 'Applied' -or
        -not (Test-LhmPathEqual `
            -Left ([string]$receipt.ReceiptPath) `
            -Right $receiptPath) -or
        $destinations.Count -ne 1 -or
        -not ($legacyDestinationShape -or $currentDestinationEvidence) -or
        -not (Test-LhmPathEqual `
            -Left ([string]$destination.Path) `
            -Right $launcherPath) -or
        [string]$destination.Sha256 -cne $launcherHash -or
        [string]$receipt.Artifact.Sha256 -cne $launcherHash -or
        $launcherHash -cne $expectedLauncherHash -or
        [Int64]$receipt.Artifact.Length -ne [Int64]$launcherLength -or
        [Int64]$launcherLength -ne $expectedLauncherLength -or
        -not (Test-LhmPathEqual `
            -Left ([string]$receipt.Source.Root) `
            -Right $script:ProductionCentralLauncherSourceRoot) -or
        [string]$receipt.Source.Commit -cne
            $script:ProductionCentralLauncherSourceCommit -or
        -not ($receipt.Source.Dirty -is [System.Boolean]) -or
        [bool]$receipt.Source.Dirty -or
        -not (Test-LhmPathEqual `
            -Left ([string]$receipt.Artifact.Path) `
            -Right $script:ProductionCentralLauncherArtifactPath) -or
        [string]$receipt.Artifact.Version -cne
            $script:ProductionCentralLauncherArtifactVersion) {
        throw 'Central RunW authority receipt does not prove the installed launcher bytes.'
    }

    return [pscustomobject]@{
        Receipt = $receipt
        LauncherSha256 = $launcherHash
        LauncherLength = [Int64]$launcherLength
        AuthorityReceiptSha256 =
            (Get-LhmFileSha256 -Path $receiptPath).ToUpperInvariant()
    }
}

function Get-LhmLauncherManagedTaskState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    if ($NonLiveTestMode) {
        $taskStatePath = Assert-LhmNormalFile `
            -Path $TestManagedTaskStatePath `
            -Label 'Managed task fixture'
        try {
            return Get-Content -LiteralPath $taskStatePath -Raw -Encoding UTF8 |
                ConvertFrom-Json
        }
        catch {
            throw "Managed task fixture is invalid: $($_.Exception.Message)"
        }
    }

    $parts = Split-LhmManagedTaskPath -ManagedStartupTaskPath $ManagedStartupTaskPath
    $task = Get-ScheduledTask `
        -TaskPath $parts.TaskPath `
        -TaskName $parts.TaskName `
        -ErrorAction Stop
    $actions = @($task.Actions)
    $triggers = @($task.Triggers)
    if ($actions.Count -ne 1 -or $triggers.Count -ne 1) {
        throw "Managed task '$ManagedStartupTaskPath' must have one action and one trigger."
    }
    return [pscustomobject][ordered]@{
        taskPath = $ManagedStartupTaskPath
        execute = [string]$actions[0].Execute
        arguments = [string]$actions[0].Arguments
        workingDirectory = [string]$actions[0].WorkingDirectory
        principalUserId = [string]$task.Principal.UserId
        logonType = [string]$task.Principal.LogonType
        runLevel = [string]$task.Principal.RunLevel
        multipleInstances = [string]$task.Settings.MultipleInstances
        startWhenAvailable = [bool]$task.Settings.StartWhenAvailable
        allowHardTerminate = [bool]$task.Settings.AllowHardTerminate
        restartCount = [int]$task.Settings.RestartCount
        restartInterval = [string]$task.Settings.RestartInterval
        trigger = [string]$triggers[0].CimClass.CimClassName
        triggerUserId = [string]$triggers[0].UserId
        enabled = [bool]$task.Settings.Enabled -and [string]$task.State -cne 'Disabled'
    }
}

function Assert-LhmLauncherManagedTaskContract {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][pscustomobject] $Scope,
        [Parameter(Mandatory)][pscustomobject] $TaskState
    )

    $runtimeRoot = Split-Path -Parent $Scope.RuntimeExecutablePath
    $restartInterval = [string]$TaskState.restartInterval
    $triggerKind = [string]$TaskState.trigger
    if ([string]$TaskState.taskPath -cne $ManagedStartupTaskPath -or
        -not (Test-LhmPathEqual `
            -Left ([string]$TaskState.execute) `
            -Right $Scope.RuntimeExecutablePath) -or
        -not [string]::IsNullOrWhiteSpace([string]$TaskState.arguments) -or
        -not (Test-LhmPathEqual `
            -Left ([string]$TaskState.workingDirectory) `
            -Right $runtimeRoot) -or
        [string]$TaskState.principalUserId -notin @('Sev', 'SND-DESK\Sev') -or
        [string]$TaskState.logonType -cne 'Interactive' -or
        [string]$TaskState.runLevel -cne 'Highest' -or
        [string]$TaskState.multipleInstances -cne 'IgnoreNew' -or
        -not [bool]$TaskState.startWhenAvailable -or
        [bool]$TaskState.allowHardTerminate -or
        [int]$TaskState.restartCount -ne 3 -or
        $restartInterval -notin @('PT1M', '00:01:00') -or
        $triggerKind -notin @('Logon', 'MSFT_TaskLogonTrigger') -or
        [string]$TaskState.triggerUserId -notin @('SND-Desk\Sev', 'SND-DESK\Sev') -or
        -not [bool]$TaskState.enabled) {
        throw "Managed task '$ManagedStartupTaskPath' does not match the accepted stable-runtime contract."
    }
}

function Test-LhmLauncherTargetCurrent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ExpectedSha256
    )

    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        (Get-LhmFileSha256 -Path $Path) -ceq $ExpectedSha256
}

function Test-LhmLauncherExactProperties {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object] $Value,
        [Parameter(Mandatory)][string[]] $Expected
    )

    if ($null -eq $Value) {
        return $false
    }
    $actualNames = @($Value.PSObject.Properties.Name | Sort-Object)
    $expectedNames = @($Expected | Sort-Object)
    return ($actualNames -join "`n") -ceq ($expectedNames -join "`n")
}

function Test-LhmLauncherDirectChildPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Parent,
        [string] $RequiredLeafPrefix
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        -not (Test-LhmPathEqual -Left (Split-Path -Parent $Path) -Right $Parent)) {
        return $false
    }
    return [string]::IsNullOrWhiteSpace($RequiredLeafPrefix) -or
        (Split-Path -Leaf $Path).StartsWith(
            $RequiredLeafPrefix,
            [System.StringComparison]::Ordinal)
}

function Test-LhmLauncherReceiptCurrent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][pscustomobject] $Scope,
        [Parameter(Mandatory)][string] $LauncherSha256,
        [Parameter(Mandatory)][string] $ShimSha256,
        [Parameter(Mandatory)][string] $CentralLauncherSha256,
        [Parameter(Mandatory)][string] $AuthorityReceiptSha256
    )

    if (-not (Test-Path -LiteralPath $Scope.ReceiptPath -PathType Leaf)) {
        return $false
    }
    try {
        $receipt = Get-Content -LiteralPath $Scope.ReceiptPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        $targets = @($receipt.targets)
        if (-not (Test-LhmLauncherExactProperties -Value $receipt -Expected @(
                'schema',
                'appliedAtUtc',
                'source',
                'centralLauncher',
                'managedTask',
                'targets',
                'rollbackDirectory',
                'rollbackManifestPath',
                'rollbackManifestSha256'
            )) -or
            -not (Test-LhmLauncherExactProperties -Value $receipt.source -Expected @(
                'launcherPath', 'launcherSha256', 'shimPath', 'shimSha256'
            )) -or
            -not (Test-LhmLauncherExactProperties -Value $receipt.centralLauncher -Expected @(
                'path',
                'sha256',
                'authorityReceiptPath',
                'authorityReceiptSha256'
            )) -or
            -not (Test-LhmLauncherExactProperties -Value $receipt.managedTask -Expected @(
                'path', 'executablePath', 'validationOnly'
            )) -or
            [string]$receipt.schema -cne $script:LauncherConvergenceSchema -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.source.launcherPath) `
                -Right $Scope.CanonicalLauncherPath) -or
            [string]$receipt.source.launcherSha256 -cne $LauncherSha256 -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.source.shimPath) `
                -Right $Scope.CanonicalShimPath) -or
            [string]$receipt.source.shimSha256 -cne $ShimSha256 -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.centralLauncher.path) `
                -Right $Scope.CentralLauncherPath) -or
            [string]$receipt.centralLauncher.sha256 -cne
                $CentralLauncherSha256 -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.centralLauncher.authorityReceiptPath) `
                -Right $Scope.CentralLauncherAuthorityReceiptPath) -or
            [string]$receipt.centralLauncher.authorityReceiptSha256 -cne
                $AuthorityReceiptSha256 -or
            [string]$receipt.managedTask.path -cne $ManagedStartupTaskPath -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.managedTask.executablePath) `
                -Right $Scope.RuntimeExecutablePath) -or
            -not ($receipt.managedTask.validationOnly -is [System.Boolean]) -or
            $receipt.managedTask.validationOnly -ne $true -or
            $targets.Count -ne 2) {
            return $false
        }
        $expectedTargets = @(
            [pscustomobject]@{
                role = 'launcher'
                path = $Scope.RuntimeLauncherPath
                sha256 = $LauncherSha256
            },
            [pscustomobject]@{
                role = 'publicShim'
                path = $Scope.PublicShimPath
                sha256 = $ShimSha256
            }
        )
        foreach ($expected in $expectedTargets) {
            $matching = @($targets | Where-Object {
                (Test-LhmLauncherExactProperties -Value $_ -Expected @(
                    'role', 'path', 'sha256'
                )) -and
                [string]$_.role -ceq $expected.role -and
                (Test-LhmPathEqual -Left ([string]$_.path) -Right $expected.path) -and
                [string]$_.sha256 -ceq $expected.sha256
            })
            if ($matching.Count -ne 1) {
                return $false
            }
        }

        $rollbackDirectory = Resolve-LhmFullPath -Path ([string]$receipt.rollbackDirectory)
        if (-not (Test-LhmLauncherDirectChildPath `
                -Path $rollbackDirectory `
                -Parent $Scope.RollbackRoot `
                -RequiredLeafPrefix 'launcher-rollback-') -or
            -not (Test-Path -LiteralPath $rollbackDirectory -PathType Container)) {
            return $false
        }
        $null = Assert-LhmNormalDirectoryTree `
            -Path $rollbackDirectory `
            -Label 'Launcher receipt rollback directory'
        $rollbackManifestPath =
            Resolve-LhmFullPath -Path ([string]$receipt.rollbackManifestPath)
        if (-not (Test-LhmPathEqual `
                -Left $rollbackManifestPath `
                -Right (Join-Path $rollbackDirectory 'rollback.json')) -or
            -not (Test-Path -LiteralPath $rollbackManifestPath -PathType Leaf) -or
            (Get-LhmFileSha256 -Path $rollbackManifestPath) -cne
                [string]$receipt.rollbackManifestSha256) {
            return $false
        }
        $rollback = Get-Content `
            -LiteralPath $rollbackManifestPath `
            -Raw `
            -Encoding UTF8 | ConvertFrom-Json
        if (-not (Test-LhmLauncherExactProperties -Value $rollback -Expected @(
                'schema',
                'createdAtUtc',
                'receiptPath',
                'receiptExisted',
                'receiptBackup',
                'receiptBackupSha256',
                'files'
            )) -or
            [string]$rollback.schema -cne 'sq.librehw.launcher-rollback.v1' -or
            -not (Test-LhmPathEqual `
                -Left ([string]$rollback.receiptPath) `
                -Right $Scope.ReceiptPath) -or
            -not ($rollback.receiptExisted -is [System.Boolean]) -or
            @($rollback.files).Count -ne 2) {
            return $false
        }
        if ($rollback.receiptExisted) {
            if (-not (Test-LhmPathEqual `
                    -Left ([string]$rollback.receiptBackup) `
                    -Right (Join-Path $rollbackDirectory 'current-receipt.json')) -or
                -not (Test-Path `
                    -LiteralPath ([string]$rollback.receiptBackup) `
                    -PathType Leaf) -or
                (Get-LhmFileSha256 -Path ([string]$rollback.receiptBackup)) -cne
                    [string]$rollback.receiptBackupSha256) {
                return $false
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$rollback.receiptBackup) -or
            -not [string]::IsNullOrWhiteSpace([string]$rollback.receiptBackupSha256)) {
            return $false
        }

        foreach ($expected in $expectedTargets) {
            $rollbackMatches = @($rollback.files | Where-Object {
                (Test-LhmLauncherExactProperties -Value $_ -Expected @(
                    'role', 'originalPath', 'existed', 'backupPath', 'sha256'
                )) -and
                [string]$_.role -ceq $expected.role -and
                (Test-LhmPathEqual `
                    -Left ([string]$_.originalPath) `
                    -Right $expected.path)
            })
            if ($rollbackMatches.Count -ne 1) {
                return $false
            }
            $rollbackFile = $rollbackMatches[0]
            if (-not ($rollbackFile.existed -is [System.Boolean])) {
                return $false
            }
            if ($rollbackFile.existed) {
                if (-not (Test-LhmLauncherDirectChildPath `
                        -Path ([string]$rollbackFile.backupPath) `
                        -Parent $rollbackDirectory) -or
                    -not (Test-Path `
                        -LiteralPath ([string]$rollbackFile.backupPath) `
                        -PathType Leaf) -or
                    (Get-LhmFileSha256 -Path ([string]$rollbackFile.backupPath)) -cne
                        [string]$rollbackFile.sha256) {
                    return $false
                }
            }
            elseif (-not [string]::IsNullOrWhiteSpace([string]$rollbackFile.backupPath) -or
                -not [string]::IsNullOrWhiteSpace([string]$rollbackFile.sha256)) {
                return $false
            }
        }
        return $true
    }
    catch {
        return $false
    }
}

function Test-LhmLauncherReceiptV1MigrationInput {
    [CmdletBinding()]
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    if (-not (Test-Path -LiteralPath $Scope.ReceiptPath -PathType Leaf)) {
        return $false
    }
    try {
        $receipt = Get-Content -LiteralPath $Scope.ReceiptPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        $targets = @($receipt.targets)
        if (-not (Test-LhmLauncherExactProperties -Value $receipt -Expected @(
                'schema',
                'appliedAtUtc',
                'source',
                'hideLaunch',
                'managedTask',
                'targets',
                'rollbackDirectory',
                'rollbackManifestPath',
                'rollbackManifestSha256'
            )) -or
            -not (Test-LhmLauncherExactProperties -Value $receipt.source -Expected @(
                'launcherPath', 'launcherSha256', 'shimPath', 'shimSha256'
            )) -or
            -not (Test-LhmLauncherExactProperties -Value $receipt.hideLaunch -Expected @(
                'artifactPath',
                'artifactSha256',
                'authorityReceiptPath',
                'authorityReceiptSha256',
                'runtimePath'
            )) -or
            -not (Test-LhmLauncherExactProperties -Value $receipt.managedTask -Expected @(
                'path', 'executablePath', 'validationOnly'
            )) -or
            [string]$receipt.schema -cne $script:LauncherConvergenceMigrationSchema -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.source.launcherPath) `
                -Right $Scope.CanonicalLauncherPath) -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.source.shimPath) `
                -Right $Scope.CanonicalShimPath) -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.hideLaunch.authorityReceiptPath) `
                -Right $Scope.CentralLauncherAuthorityReceiptPath) -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.hideLaunch.runtimePath) `
                -Right (Join-Path `
                    (Split-Path -Parent $Scope.RuntimeLauncherPath) `
                    'hidelaunch.exe')) -or
            [string]$receipt.hideLaunch.artifactSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
            [string]$receipt.hideLaunch.authorityReceiptSha256 -notmatch
                '^[0-9a-fA-F]{64}$' -or
            [string]$receipt.managedTask.path -cne $ManagedStartupTaskPath -or
            -not (Test-LhmPathEqual `
                -Left ([string]$receipt.managedTask.executablePath) `
                -Right $Scope.RuntimeExecutablePath) -or
            -not ($receipt.managedTask.validationOnly -is [System.Boolean]) -or
            $receipt.managedTask.validationOnly -ne $true -or
            $targets.Count -ne 3) {
            return $false
        }
        $expectedTargetPaths = [ordered]@{
            launcher = $Scope.RuntimeLauncherPath
            hidelaunch = Join-Path `
                (Split-Path -Parent $Scope.RuntimeLauncherPath) `
                'hidelaunch.exe'
            publicShim = $Scope.PublicShimPath
        }
        foreach ($entry in @($expectedTargetPaths.GetEnumerator())) {
            $matching = @($targets | Where-Object {
                (Test-LhmLauncherExactProperties -Value $_ -Expected @(
                    'role', 'path', 'sha256'
                )) -and
                [string]$_.role -ceq [string]$entry.Key -and
                (Test-LhmPathEqual `
                    -Left ([string]$_.path) `
                    -Right ([string]$entry.Value)) -and
                [string]$_.sha256 -match '^[0-9a-fA-F]{64}$'
            })
            if ($matching.Count -ne 1) {
                return $false
            }
        }
        return $true
    }
    catch {
        return $false
    }
}

function Get-LhmLauncherRollbackDirectories {
    [CmdletBinding()]
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    if (-not (Test-Path -LiteralPath $Scope.RollbackRoot -PathType Container)) {
        return @()
    }
    $null = Assert-LhmNormalDirectoryTree `
        -Path $Scope.RollbackRoot `
        -Label 'Launcher rollback root'
    return @(Get-ChildItem `
        -LiteralPath $Scope.RollbackRoot `
        -Directory `
        -Force | Where-Object {
            $_.Name.StartsWith(
                'launcher-rollback-',
                [System.StringComparison]::Ordinal)
        } | Sort-Object Name -Descending)
}

function Invoke-LhmLauncherRollbackRetention {
    [CmdletBinding()]
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    $directories = @(Get-LhmLauncherRollbackDirectories -Scope $Scope)
    $protectedPath = $null
    if (Test-Path -LiteralPath $Scope.ReceiptPath -PathType Leaf) {
        try {
            $currentReceipt = Get-Content `
                -LiteralPath $Scope.ReceiptPath `
                -Raw `
                -Encoding UTF8 | ConvertFrom-Json
            $candidate = Resolve-LhmFullPath `
                -Path ([string]$currentReceipt.rollbackDirectory)
            if (Test-LhmLauncherDirectChildPath `
                -Path $candidate `
                -Parent $Scope.RollbackRoot `
                -RequiredLeafPrefix 'launcher-rollback-') {
                $protectedPath = $candidate
            }
        }
        catch {
            $protectedPath = $null
        }
    }
    $kept = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    if ($null -ne $protectedPath) {
        $null = $kept.Add($protectedPath)
    }
    foreach ($directory in $directories) {
        if ($kept.Count -ge $RollbackRetentionCount) {
            break
        }
        $null = $kept.Add((Resolve-LhmFullPath -Path $directory.FullName))
    }
    foreach ($directory in $directories) {
        $resolvedPath = Resolve-LhmFullPath -Path $directory.FullName
        if ($kept.Contains($resolvedPath)) {
            continue
        }
        if (-not (Test-LhmLauncherDirectChildPath `
                -Path $resolvedPath `
                -Parent $Scope.RollbackRoot `
                -RequiredLeafPrefix 'launcher-rollback-')) {
            throw "Refusing to prune unexpected launcher rollback path '$resolvedPath'."
        }
        $null = Assert-LhmNormalDirectoryTree `
            -Path $resolvedPath `
            -Label 'Expired launcher rollback packet'
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Get-LhmLauncherConvergenceState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    $issues = [System.Collections.Generic.List[string]]::new()
    $blockingIssues = [System.Collections.Generic.List[string]]::new()
    $authority = $null
    $taskState = $null
    $taskValid = $false
    $launcherHash = $null
    $shimHash = $null
    try {
        $null = Assert-LhmNormalFile `
            -Path $Scope.CanonicalLauncherPath `
            -Label 'Canonical launcher source'
        $launcherHash = Get-LhmFileSha256 -Path $Scope.CanonicalLauncherPath
    }
    catch {
        $blockingIssues.Add($_.Exception.Message)
    }
    try {
        $null = Assert-LhmNormalFile `
            -Path $Scope.CanonicalShimPath `
            -Label 'Canonical public shim source'
        $actualShimText = ConvertTo-LhmLauncherNormalizedText `
            -Text ([System.IO.File]::ReadAllText($Scope.CanonicalShimPath))
        $expectedShimText = Get-LhmLauncherExpectedShimText `
            -CentralLauncherPath $Scope.CentralLauncherPath `
            -LauncherPath $Scope.RuntimeLauncherPath
        if ($actualShimText -cne $expectedShimText) {
            throw 'Canonical public shim source does not match the wait-capable launcher contract.'
        }
        $shimHash = Get-LhmLauncherTextSha256 -Text $expectedShimText
    }
    catch {
        $blockingIssues.Add($_.Exception.Message)
    }
    try {
        $null = Assert-LhmNormalFile `
            -Path $Scope.RuntimeExecutablePath `
            -Label 'Installed Libre Hardware Monitor executable'
        $authority = Read-LhmLauncherAuthorityReceipt -Scope $Scope
    }
    catch {
        $blockingIssues.Add($_.Exception.Message)
    }
    try {
        $taskState = Get-LhmLauncherManagedTaskState -Scope $Scope
        Assert-LhmLauncherManagedTaskContract -Scope $Scope -TaskState $taskState
        $taskValid = $true
    }
    catch {
        $blockingIssues.Add($_.Exception.Message)
    }
    foreach ($issue in $blockingIssues) {
        $issues.Add($issue)
    }

    $launcherCurrent = $false
    $shimCurrent = $false
    $centralLauncherCurrent = $false
    $receiptCurrent = $false
    $receiptMigrationRequired = $false
    if ($null -ne $launcherHash) {
        $launcherCurrent = Test-LhmLauncherTargetCurrent `
            -Path $Scope.RuntimeLauncherPath `
            -ExpectedSha256 $launcherHash
        if (-not $launcherCurrent) {
            $issues.Add('Runtime launcher is missing or does not match canonical source.')
        }
    }
    if ($null -ne $shimHash) {
        $shimCurrent = Test-LhmLauncherTargetCurrent `
            -Path $Scope.PublicShimPath `
            -ExpectedSha256 $shimHash
        if (-not $shimCurrent) {
            $issues.Add('LibreHW public shim is missing or does not match canonical source.')
        }
    }
    if ($null -ne $authority) {
        $centralLauncherCurrent = $true
    }
    if ($null -ne $launcherHash -and
        $null -ne $shimHash -and
        $null -ne $authority) {
        $receiptCurrent = Test-LhmLauncherReceiptCurrent `
            -Scope $Scope `
            -LauncherSha256 $launcherHash `
            -ShimSha256 $shimHash `
            -CentralLauncherSha256 (
                [string]$authority.LauncherSha256).ToLowerInvariant() `
            -AuthorityReceiptSha256 (
                [string]$authority.AuthorityReceiptSha256).ToLowerInvariant()
        if (-not $receiptCurrent) {
            $receiptMigrationRequired =
                Test-LhmLauncherReceiptV1MigrationInput -Scope $Scope
            if ($receiptMigrationRequired) {
                $issues.Add(
                    'Launcher convergence receipt schema v1 requires migration to v2.')
            }
            else {
                $issues.Add('Launcher convergence receipt is missing or stale.')
            }
        }
    }
    $rollbackPacketCount = @(Get-LhmLauncherRollbackDirectories -Scope $Scope).Count

    return [pscustomobject][ordered]@{
        Result = if ($issues.Count -eq 0) { 'PASS' } else { 'DRIFT' }
        Mode = $Mode
        DriftDetected = $issues.Count -gt 0
        Issues = @($issues)
        BlockingIssues = @($blockingIssues)
        CanonicalLauncherSha256 = $launcherHash
        CanonicalShimSha256 = $shimHash
        CentralLauncherSha256 = if ($null -ne $authority) {
            ([string]$authority.LauncherSha256).ToLowerInvariant()
        } else { $null }
        CentralLauncherLength = if ($null -ne $authority) {
            [Int64]$authority.LauncherLength
        } else { $null }
        CentralLauncherAuthorityReceiptSha256 = if ($null -ne $authority) {
            ([string]$authority.AuthorityReceiptSha256).ToLowerInvariant()
        } else { $null }
        LauncherCurrent = $launcherCurrent
        CentralLauncherCurrent = $centralLauncherCurrent
        PublicShimCurrent = $shimCurrent
        ReceiptCurrent = $receiptCurrent
        ReceiptMigrationRequired = $receiptMigrationRequired
        ProposedReceiptSchema = $script:LauncherConvergenceSchema
        ManagedTaskCurrent = $taskValid
        RollbackPacketCount = $rollbackPacketCount
        RollbackRetentionCount = $RollbackRetentionCount
        RollbackRetentionExceeded =
            $rollbackPacketCount -gt $RollbackRetentionCount
        MutationPerformed = $false
        TestMode = [bool]$NonLiveTestMode
    }
}

function Copy-LhmLauncherStagedFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][string] $ExpectedSha256,
        [AllowNull()][object] $CanonicalText
    )

    $destination = Assert-LhmSafeFileDestination `
        -Path $Destination `
        -Label 'Launcher convergence target'
    $parent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw "Launcher convergence target parent is missing: '$parent'."
    }
    $stagePath = Join-Path `
        $parent `
        ".$([System.IO.Path]::GetFileName($destination)).$([guid]::NewGuid().ToString('N')).tmp"
    if ($null -ne $CanonicalText) {
        [System.IO.File]::WriteAllText(
            $stagePath,
            $CanonicalText,
            [System.Text.Encoding]::ASCII)
    }
    else {
        Copy-Item -LiteralPath $Source -Destination $stagePath
    }
    if ((Get-LhmFileSha256 -Path $stagePath) -cne $ExpectedSha256) {
        Remove-Item -LiteralPath $stagePath -Force -ErrorAction SilentlyContinue
        throw "Staged launcher artifact hash mismatch for '$Destination'."
    }
    return $stagePath
}

function Write-LhmLauncherJsonAtomically {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object] $Value,
        [Parameter(Mandatory)][string] $Path
    )

    $path = Assert-LhmSafeFileDestination -Path $Path -Label 'Launcher receipt'
    $parent = Split-Path -Parent $path
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporaryPath = Join-Path `
        $parent `
        ".$([System.IO.Path]::GetFileName($path)).$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $Value | ConvertTo-Json -Depth 8 | Set-Content `
            -LiteralPath $temporaryPath `
            -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Invoke-LhmLauncherConvergenceApply {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][pscustomobject] $Scope,
        [Parameter(Mandatory)][pscustomobject] $State
    )

    if (@($State.BlockingIssues).Count -gt 0) {
        throw "Launcher convergence cannot Apply: $(@($State.BlockingIssues) -join ' ')"
    }
    if (-not $State.DriftDetected -and -not $State.RollbackRetentionExceeded) {
        return $State
    }
    if (-not $PSCmdlet.ShouldProcess(
        "$($Scope.RuntimeLauncherPath), $($Scope.PublicShimPath)",
        'Atomically converge LibreHW launcher artifacts and receipt')) {
        return $State
    }

    if (-not $State.DriftDetected) {
        Invoke-LhmLauncherRollbackRetention -Scope $Scope
        $retentionState = Get-LhmLauncherConvergenceState -Scope $Scope
        $retentionState.MutationPerformed = $true
        return $retentionState
    }

    $targets = @(
        [pscustomobject]@{
            Role = 'launcher'
            Source = $Scope.CanonicalLauncherPath
            Destination = $Scope.RuntimeLauncherPath
            Sha256 = [string]$State.CanonicalLauncherSha256
            CanonicalText = $null
        },
        [pscustomobject]@{
            Role = 'publicShim'
            Source = $Scope.CanonicalShimPath
            Destination = $Scope.PublicShimPath
            Sha256 = [string]$State.CanonicalShimSha256
            CanonicalText = Get-LhmLauncherExpectedShimText `
                -CentralLauncherPath $Scope.CentralLauncherPath `
                -LauncherPath $Scope.RuntimeLauncherPath
        }
    )

    $stages = @()
    $rollbackFiles = @()
    $rollbackDirectory = $null
    $receiptBackup = $null
    $receiptBackupSha256 = $null
    $rollbackManifestPath = $null
    $rollbackManifestSha256 = $null
    $postState = $null
    $receiptExisted = Test-Path -LiteralPath $Scope.ReceiptPath -PathType Leaf
    try {
        foreach ($target in $targets) {
            $stages += [pscustomobject]@{
                Target = $target
                Path = Copy-LhmLauncherStagedFile `
                    -Source $target.Source `
                    -Destination $target.Destination `
                    -ExpectedSha256 $target.Sha256 `
                    -CanonicalText $target.CanonicalText
            }
        }

        $rollbackParent = Assert-LhmNearestExistingPathAncestry `
            -Path $Scope.RollbackRoot `
            -Label 'Launcher rollback root'
        $null = $rollbackParent
        [System.IO.Directory]::CreateDirectory($Scope.RollbackRoot) | Out-Null
        $timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
        $rollbackDirectory = Join-Path `
            $Scope.RollbackRoot `
            "launcher-rollback-$timestamp"
        [System.IO.Directory]::CreateDirectory($rollbackDirectory) | Out-Null

        $index = 0
        foreach ($target in $targets) {
            $index++
            $existed = Test-Path -LiteralPath $target.Destination -PathType Leaf
            $backupPath = $null
            if ($existed) {
                $backupPath = Join-Path `
                    $rollbackDirectory `
                    ('{0:D2}-{1}' -f $index, [System.IO.Path]::GetFileName($target.Destination))
                Copy-Item -LiteralPath $target.Destination -Destination $backupPath
            }
            $rollbackFiles += [pscustomobject][ordered]@{
                role = $target.Role
                originalPath = $target.Destination
                existed = $existed
                backupPath = $backupPath
                sha256 = if ($existed) {
                    Get-LhmFileSha256 -Path $backupPath
                } else { $null }
            }
        }
        if ($receiptExisted) {
            $receiptBackup = Join-Path $rollbackDirectory 'current-receipt.json'
            Copy-Item -LiteralPath $Scope.ReceiptPath -Destination $receiptBackup
            $receiptBackupSha256 = Get-LhmFileSha256 -Path $receiptBackup
        }
        $rollbackManifestPath = Join-Path $rollbackDirectory 'rollback.json'
        Write-LhmLauncherJsonAtomically `
            -Path $rollbackManifestPath `
            -Value ([ordered]@{
                schema = 'sq.librehw.launcher-rollback.v1'
                createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                receiptPath = $Scope.ReceiptPath
                receiptExisted = $receiptExisted
                receiptBackup = $receiptBackup
                receiptBackupSha256 = $receiptBackupSha256
                files = @($rollbackFiles)
            })
        $rollbackManifestSha256 = Get-LhmFileSha256 -Path $rollbackManifestPath

        if ($TestCorruptRollbackBackupRole -cne 'None') {
            $backupToCorrupt = @($rollbackFiles | Where-Object {
                [string]$_.role -ceq $TestCorruptRollbackBackupRole -and
                [bool]$_.existed
            })
            if ($backupToCorrupt.Count -ne 1) {
                throw "No unique rollback backup exists for test role '$TestCorruptRollbackBackupRole'."
            }
            Add-Content `
                -LiteralPath ([string]$backupToCorrupt[0].backupPath) `
                -Value 'injected rollback corruption'
        }

        foreach ($stage in $stages) {
            Move-Item `
                -LiteralPath $stage.Path `
                -Destination $stage.Target.Destination `
                -Force
            $failurePoint = switch ([string]$stage.Target.Role) {
                'launcher' { 'AfterLauncherDeployment' }
                'publicShim' { 'AfterPublicShimDeployment' }
                default { throw "Unexpected launcher target role '$($stage.Target.Role)'." }
            }
            if ($TestFailurePoint -ceq $failurePoint) {
                throw "Injected launcher convergence failure: $failurePoint"
            }
        }
        foreach ($target in $targets) {
            if ((Get-LhmFileSha256 -Path $target.Destination) -cne $target.Sha256) {
                throw "Launcher artifact deployment verification failed: '$($target.Destination)'."
            }
        }

        Write-LhmLauncherJsonAtomically `
            -Path $Scope.ReceiptPath `
            -Value ([ordered]@{
                schema = $script:LauncherConvergenceSchema
                appliedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                source = [ordered]@{
                    launcherPath = $Scope.CanonicalLauncherPath
                    launcherSha256 = $State.CanonicalLauncherSha256
                    shimPath = $Scope.CanonicalShimPath
                    shimSha256 = $State.CanonicalShimSha256
                }
                centralLauncher = [ordered]@{
                    path = $Scope.CentralLauncherPath
                    sha256 = $State.CentralLauncherSha256
                    authorityReceiptPath =
                        $Scope.CentralLauncherAuthorityReceiptPath
                    authorityReceiptSha256 =
                        $State.CentralLauncherAuthorityReceiptSha256
                }
                managedTask = [ordered]@{
                    path = $ManagedStartupTaskPath
                    executablePath = $Scope.RuntimeExecutablePath
                    validationOnly = $true
                }
                targets = @($targets | ForEach-Object {
                    [ordered]@{
                        role = $_.Role
                        path = $_.Destination
                        sha256 = $_.Sha256
                    }
                })
                rollbackDirectory = $rollbackDirectory
                rollbackManifestPath = $rollbackManifestPath
                rollbackManifestSha256 = $rollbackManifestSha256
            })
        if ($TestFailurePoint -ceq 'AfterReceiptDeployment') {
            throw 'Injected launcher convergence failure: AfterReceiptDeployment'
        }
        $postState = Get-LhmLauncherConvergenceState -Scope $Scope
        if ($postState.DriftDetected) {
            throw "Launcher convergence Apply left drift: $(@($postState.Issues) -join ' ')"
        }
    }
    catch {
        $originalError = $_
        $rollbackErrors = [System.Collections.Generic.List[string]]::new()
        if ($null -ne $rollbackDirectory) {
            foreach ($target in $targets) {
                try {
                    $records = @($rollbackFiles | Where-Object {
                        [string]$_.role -ceq [string]$target.Role -and
                        (Test-LhmPathEqual `
                            -Left ([string]$_.originalPath) `
                            -Right ([string]$target.Destination))
                    })
                    if ($records.Count -ne 1) {
                        throw "In-memory rollback plan has no unique '$($target.Role)' record."
                    }
                    $record = $records[0]
                    if ([bool]$record.existed) {
                        if (-not (Test-LhmLauncherDirectChildPath `
                                -Path ([string]$record.backupPath) `
                                -Parent $rollbackDirectory) -or
                            -not (Test-Path `
                                -LiteralPath ([string]$record.backupPath) `
                                -PathType Leaf) -or
                            (Get-LhmFileSha256 -Path ([string]$record.backupPath)) -cne
                                [string]$record.sha256) {
                            throw "Rollback backup validation failed for '$($target.Role)'."
                        }
                        $null = Assert-LhmNormalFile `
                            -Path ([string]$record.backupPath) `
                            -Label "Rollback backup '$($target.Role)'"
                        $restoreStage = Join-Path `
                            (Split-Path -Parent $target.Destination) `
                            ".$([System.IO.Path]::GetFileName($target.Destination)).restore.$([guid]::NewGuid().ToString('N')).tmp"
                        try {
                            Copy-Item `
                                -LiteralPath ([string]$record.backupPath) `
                                -Destination $restoreStage
                            if ((Get-LhmFileSha256 -Path $restoreStage) -cne
                                [string]$record.sha256) {
                                throw "Rollback restore stage hash mismatch for '$($target.Role)'."
                            }
                            Move-Item `
                                -LiteralPath $restoreStage `
                                -Destination $target.Destination `
                                -Force
                        }
                        finally {
                            if (Test-Path -LiteralPath $restoreStage -PathType Leaf) {
                                Remove-Item -LiteralPath $restoreStage -Force
                            }
                        }
                    }
                    elseif (Test-Path -LiteralPath $target.Destination -PathType Leaf) {
                        Remove-Item -LiteralPath $target.Destination -Force
                    }
                }
                catch {
                    $rollbackErrors.Add(
                        "Target '$($target.Role)': $($_.Exception.Message)")
                }
            }
            try {
                if ($receiptExisted) {
                    if (-not (Test-LhmPathEqual `
                            -Left $receiptBackup `
                            -Right (Join-Path $rollbackDirectory 'current-receipt.json')) -or
                        -not (Test-Path -LiteralPath $receiptBackup -PathType Leaf) -or
                        (Get-LhmFileSha256 -Path $receiptBackup) -cne
                            $receiptBackupSha256) {
                        throw 'Prior launcher receipt backup failed validation.'
                    }
                    $null = Assert-LhmNormalFile `
                        -Path $receiptBackup `
                        -Label 'Prior launcher receipt backup'
                    $receiptRestoreStage = Join-Path `
                        (Split-Path -Parent $Scope.ReceiptPath) `
                        ".current-receipt.restore.$([guid]::NewGuid().ToString('N')).tmp"
                    try {
                        Copy-Item -LiteralPath $receiptBackup -Destination $receiptRestoreStage
                        Move-Item `
                            -LiteralPath $receiptRestoreStage `
                            -Destination $Scope.ReceiptPath `
                            -Force
                    }
                    finally {
                        if (Test-Path -LiteralPath $receiptRestoreStage -PathType Leaf) {
                            Remove-Item -LiteralPath $receiptRestoreStage -Force
                        }
                    }
                }
                elseif (Test-Path -LiteralPath $Scope.ReceiptPath -PathType Leaf) {
                    Remove-Item -LiteralPath $Scope.ReceiptPath -Force
                }
            }
            catch {
                $rollbackErrors.Add("Receipt: $($_.Exception.Message)")
            }
            if ($rollbackErrors.Count -eq 0) {
                try {
                    Invoke-LhmLauncherRollbackRetention -Scope $Scope
                }
                catch {
                    $rollbackErrors.Add("Retention: $($_.Exception.Message)")
                }
            }
        }
        if ($rollbackErrors.Count -gt 0) {
            $combinedMessage =
                "Launcher convergence failed: $($originalError.Exception.Message) " +
                "Rollback also failed: $($rollbackErrors -join ' ')"
            throw [System.Exception]::new(
                $combinedMessage,
                $originalError.Exception)
        }
        throw $originalError
    }
    finally {
        foreach ($stage in $stages) {
            if (Test-Path -LiteralPath $stage.Path -PathType Leaf) {
                Remove-Item -LiteralPath $stage.Path -Force
            }
        }
    }

    Invoke-LhmLauncherRollbackRetention -Scope $Scope
    $postState = Get-LhmLauncherConvergenceState -Scope $Scope
    if ($postState.DriftDetected -or $postState.RollbackRetentionExceeded) {
        throw 'Launcher convergence post-commit retention validation failed.'
    }
    $postState.MutationPerformed = $true
    return $postState
}

$scope = Assert-LhmLauncherConvergenceScope

if ($Mode -ceq 'Apply') {
    $null = Assert-LhmLauncherApplyIdentity
}

$state = Get-LhmLauncherConvergenceState -Scope $scope
if ($Mode -cne 'Apply') {
    $state
    return
}

Invoke-LhmLauncherConvergenceApply -Scope $scope -State $state

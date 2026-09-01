[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidateSet('Plan', 'Apply', 'Validate')]
    [string] $Mode = 'Plan',

    [string] $LegacyInstallRoot = 'E:\SQ_HQ\Monitoring\LibreHW',

    [string] $InstallRoot = 'E:\Monitoring\LibreHW\Runtime',

    [string] $DataRoot,

    [string] $ManagedStartupTaskPath = '\SevGrp\AdminTask\LibreHW-No-UAC',

    [string] $LegacyLauncherPath =
        'E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1',

    [string] $LauncherTargetPath =
        'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1',

    [string] $PublicShimPath,

    [string] $HideLaunchArtifactPath =
        'D:\Development\System\launch-hidden-shim\bin\runw.exe',

    [string] $HideLaunchAuthorityReceiptPath =
        'E:\Data\RunW\install-receipt-v2.json',

    [uri] $HealthUri = 'http://localhost:8085/data.json',

    [ValidateRange(5, 300)]
    [int] $ActivationTimeoutSeconds = 45,

    [switch] $NonLiveTestMode,

    [string] $TestExternalTaskStatePath,

    [ValidateSet('None', 'AfterRuntimeCopy', 'AfterBindings')]
    [string] $TestFailurePoint = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')

function Invoke-TestFailurePoint {
    param([Parameter(Mandatory)][string] $Point)
    if ($TestFailurePoint -ceq $Point) {
        throw "Injected non-live runtime-root migration failure: $Point"
    }
}

if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity
}

$DataRoot = Resolve-LhmUnspecifiedProductionPath `
    -ParameterName 'DataRoot' `
    -CurrentValue $DataRoot `
    -Resolver { Get-LhmProductionDataRoot } `
    -NonLiveTestMode:$NonLiveTestMode
$PublicShimPath = Resolve-LhmUnspecifiedProductionPath `
    -ParameterName 'PublicShimPath' `
    -CurrentValue $PublicShimPath `
    -Resolver { Get-LhmProductionPublicShimPath } `
    -NonLiveTestMode:$NonLiveTestMode

function Resolve-LhmRuntimeMigrationScope {
    $modeState = Assert-LhmOperationMode `
        -InstallRoot $InstallRoot `
        -DataRoot $DataRoot `
        -ManagedStartupTaskPath $ManagedStartupTaskPath `
        -NonLiveTestMode:$NonLiveTestMode

    $resolved = [ordered]@{
        LegacyInstallRoot = Resolve-LhmFullPath -Path $LegacyInstallRoot
        InstallRoot = $modeState.InstallRoot
        DataRoot = $modeState.DataRoot
        LegacyLauncherPath = Resolve-LhmFullPath -Path $LegacyLauncherPath
        LauncherTargetPath = Resolve-LhmFullPath -Path $LauncherTargetPath
        RuntimeHideLaunchPath = Resolve-LhmFullPath -Path (Join-Path `
            (Split-Path -Parent $LauncherTargetPath) `
            'hidelaunch.exe')
        PublicShimPath = Resolve-LhmFullPath -Path $PublicShimPath
        HideLaunchArtifactPath = Resolve-LhmFullPath -Path $HideLaunchArtifactPath
        HideLaunchAuthorityReceiptPath =
            Resolve-LhmFullPath -Path $HideLaunchAuthorityReceiptPath
        IsTest = $modeState.IsTest
    }

    if ($resolved.IsTest) {
        $tempRoot = Resolve-LhmFullPath -Path ([System.IO.Path]::GetTempPath())
        foreach ($path in @(
            $resolved.LegacyInstallRoot,
            $resolved.InstallRoot,
            $resolved.DataRoot,
            $resolved.LegacyLauncherPath,
            $resolved.LauncherTargetPath,
            $resolved.RuntimeHideLaunchPath,
            $resolved.PublicShimPath,
            $resolved.HideLaunchArtifactPath,
            $resolved.HideLaunchAuthorityReceiptPath,
            $TestExternalTaskStatePath
        )) {
            if ([string]::IsNullOrWhiteSpace([string]$path) -or
                -not (Test-LhmPathWithin -Path $path -Root $tempRoot)) {
                throw 'Non-live runtime migration paths must be beneath the OS temporary directory.'
            }
        }
    }
    else {
        if (-not (Test-LhmPathEqual `
            -Left $resolved.LegacyInstallRoot `
            -Right $script:LhmLegacyProductionInstallRoot) -or
            -not (Test-LhmPathEqual `
                -Left $resolved.LegacyLauncherPath `
                -Right $script:LhmLegacyLauncherTargetPath) -or
            -not (Test-LhmPathEqual `
                -Left $resolved.LauncherTargetPath `
                -Right $script:LhmLauncherTargetPath) -or
            -not (Test-LhmPathEqual `
                -Left $resolved.PublicShimPath `
                -Right (Get-LhmProductionPublicShimPath)) -or
            -not (Test-LhmPathEqual `
                -Left $resolved.HideLaunchArtifactPath `
                -Right 'D:\Development\System\launch-hidden-shim\bin\runw.exe') -or
            -not (Test-LhmPathEqual `
                -Left $resolved.HideLaunchAuthorityReceiptPath `
                -Right 'E:\Data\RunW\install-receipt-v2.json') -or
            [string]$HealthUri.AbsoluteUri -cne $script:LhmProductionHealthUri) {
            throw 'Production runtime migration paths and health URI are fixed.'
        }
        if ($TestFailurePoint -cne 'None' -or
            -not [string]::IsNullOrWhiteSpace($TestExternalTaskStatePath)) {
            throw 'Production runtime migration does not accept test hooks.'
        }
    }

    foreach ($pair in @(
        @($resolved.LegacyInstallRoot, $resolved.InstallRoot),
        @($resolved.LegacyLauncherPath, $resolved.LauncherTargetPath),
        @($resolved.InstallRoot, $resolved.DataRoot)
    )) {
        if (Test-LhmPathEqual -Left $pair[0] -Right $pair[1]) {
            throw 'Runtime migration source and destination paths must be distinct.'
        }
    }

    return [pscustomobject]$resolved
}

function Get-LhmRuntimeMigrationHideLaunchAuthority {
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    $receiptPath = Assert-LhmNormalFile `
        -Path $Scope.HideLaunchAuthorityReceiptPath `
        -Label 'HideLaunch authority receipt'
    $artifactPath = Assert-LhmNormalFile `
        -Path $Scope.HideLaunchArtifactPath `
        -Label 'Validated HideLaunch artifact'
    try {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "HideLaunch authority receipt is invalid: $($_.Exception.Message)"
    }
    $artifactHash = (Get-LhmFileSha256 -Path $artifactPath).ToUpperInvariant()
    if ([string]$receipt.Schema -cne 'runw.deployment.v2' -or
        -not (Test-LhmPathEqual `
            -Left ([string]$receipt.Artifact.Path) `
            -Right $artifactPath) -or
        [string]$receipt.Artifact.Sha256 -cne $artifactHash -or
        [Int64]$receipt.Artifact.Length -ne
            [Int64](Get-Item -LiteralPath $artifactPath -Force).Length -or
        [string]$receipt.Artifact.Version -notmatch '^1\.[0-9]+\.[0-9]+$' -or
        [string]$receipt.Source.Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'HideLaunch authority receipt does not prove its selected artifact.'
    }
    if (-not $Scope.IsTest) {
        $sourceRoot = Assert-LhmNormalDirectoryTree `
            -Path ([string]$receipt.Source.Root) `
            -Label 'HideLaunch source repository'
        $gitCommands = @(Get-Command git.exe -CommandType Application -ErrorAction Stop)
        $head = @(& ([string]$gitCommands[0].Source) `
            -C $sourceRoot rev-parse HEAD 2>&1)
        if ($LASTEXITCODE -ne 0 -or
            ($head -join '').Trim() -cne [string]$receipt.Source.Commit) {
            throw 'HideLaunch source repository no longer matches its authority receipt.'
        }
    }
    return [pscustomobject]@{
        ArtifactSha256 = $artifactHash.ToLowerInvariant()
        ReceiptSha256 = Get-LhmFileSha256 -Path $receiptPath
    }
}

function Assert-LhmRuntimeMigrationHideLaunchCurrent {
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    $authority = Get-LhmRuntimeMigrationHideLaunchAuthority -Scope $Scope
    if (-not (Test-Path `
            -LiteralPath $Scope.RuntimeHideLaunchPath `
            -PathType Leaf) -or
        (Get-LhmFileSha256 -Path $Scope.RuntimeHideLaunchPath) -cne
            [string]$authority.ArtifactSha256) {
        throw "The app-vendored HideLaunch must already match its validated authority before runtime migration: '$($Scope.RuntimeHideLaunchPath)'."
    }
    $null = Assert-LhmNormalFile `
        -Path $Scope.RuntimeHideLaunchPath `
        -Label 'App-vendored HideLaunch'
    return $authority
}

function Get-LhmRuntimeMigrationTaskState {
    param(
        [Parameter(Mandatory)][pscustomobject] $Scope
    )

    if ($Scope.IsTest) {
        if (-not (Test-Path -LiteralPath $TestExternalTaskStatePath -PathType Leaf)) {
            throw "Non-live managed task state is missing: '$TestExternalTaskStatePath'."
        }
        return Get-Content -LiteralPath $TestExternalTaskStatePath -Raw |
            ConvertFrom-Json
    }

    $parts = Split-LhmManagedTaskPath -ManagedStartupTaskPath $ManagedStartupTaskPath
    $task = Get-ScheduledTask `
        -TaskPath $parts.TaskPath `
        -TaskName $parts.TaskName `
        -ErrorAction Stop
    $action = @($task.Actions)
    $triggers = @($task.Triggers)
    if ($action.Count -ne 1 -or
        $triggers.Count -ne 1 -or
        [string]$triggers[0].CimClass.CimClassName -cne 'MSFT_TaskLogonTrigger' -or
        [string]$task.Principal.LogonType -cne 'Interactive' -or
        [string]$task.Principal.RunLevel -cne 'Highest' -or
        [string]$task.Settings.MultipleInstances -cne 'IgnoreNew' -or
        -not [bool]$task.Settings.StartWhenAvailable -or
        [bool]$task.Settings.AllowHardTerminate -or
        [int]$task.Settings.RestartCount -ne $script:LhmManagedTaskRestartCount -or
        [string]$task.Settings.RestartInterval -cne 'PT1M') {
        throw "Managed task '$ManagedStartupTaskPath' does not match the accepted task contract."
    }

    $principalSid = ([System.Security.Principal.NTAccount]::new(
        [string]$task.Principal.UserId)).Translate(
            [System.Security.Principal.SecurityIdentifier]).Value
    if ($principalSid -cne $script:LhmManagedTaskPrincipalSid) {
        throw "Managed task '$ManagedStartupTaskPath' has an unexpected principal."
    }

    return [pscustomobject]@{
        execute = [string]$action[0].Execute
        workingDirectory = [string]$action[0].WorkingDirectory
        restartCount = [int]$task.Settings.RestartCount
        restartInterval = [string]$task.Settings.RestartInterval
        enabled = [bool]$task.Settings.Enabled
    }
}

function Assert-LhmRuntimeMigrationTaskTarget {
    param(
        [Parameter(Mandatory)][pscustomobject] $Scope,
        [Parameter(Mandatory)][string] $ExpectedInstallRoot
    )

    $taskState = Get-LhmRuntimeMigrationTaskState -Scope $Scope
    $expectedExecutable = Join-Path $ExpectedInstallRoot $script:LhmExecutableName
    if (-not [bool]$taskState.enabled -or
        [int]$taskState.restartCount -ne $script:LhmManagedTaskRestartCount -or
        [string]$taskState.restartInterval -cne 'PT1M' -or
        -not (Test-LhmPathEqual -Left ([string]$taskState.execute) -Right $expectedExecutable) -or
        -not (Test-LhmPathEqual `
            -Left ([string]$taskState.workingDirectory) `
            -Right $ExpectedInstallRoot)) {
        throw "Managed task '$ManagedStartupTaskPath' does not target '$ExpectedInstallRoot'."
    }
}

function Set-LhmRuntimeMigrationTaskTarget {
    param(
        [Parameter(Mandatory)][pscustomobject] $Scope,
        [Parameter(Mandatory)][string] $TargetInstallRoot
    )

    $targetExecutable = Join-Path $TargetInstallRoot $script:LhmExecutableName
    if ($Scope.IsTest) {
        [pscustomobject][ordered]@{
            execute = $targetExecutable
            workingDirectory = $TargetInstallRoot
            restartCount = $script:LhmManagedTaskRestartCount
            restartInterval = 'PT1M'
            enabled = $true
        } | ConvertTo-Json | Set-Content `
            -LiteralPath $TestExternalTaskStatePath `
            -Encoding UTF8
        return
    }

    Register-LhmManagedTask `
        -ExecutablePath $targetExecutable `
        -InstallRoot $TargetInstallRoot `
        -ManagedStartupTaskPath $ManagedStartupTaskPath
}

function Get-LhmRuntimeMigrationShimText {
    param([Parameter(Mandatory)][string] $LauncherPath)

    $hideLaunchPath = Join-Path (Split-Path -Parent $LauncherPath) 'hidelaunch.exe'
    return "@echo off`r`n" +
        "`"$hideLaunchPath`" /wait /quiet /cwd:- " +
        "`"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`" " +
        "-NoLogo -NoProfile -ExecutionPolicy Bypass " +
        "-File `"$LauncherPath`" %*`r`n" +
        "exit /b %ERRORLEVEL%`r`n"
}

function Write-LhmRuntimeMigrationShim {
    param(
        [Parameter(Mandatory)][pscustomobject] $Scope
    )

    $destination = Assert-LhmSafeFileDestination `
        -Path $Scope.PublicShimPath `
        -Label 'LibreHW public shim'
    $temporaryPath = Join-Path `
        (Split-Path -Parent $destination) `
        ".librehw.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        Set-Content `
            -LiteralPath $temporaryPath `
            -Value (Get-LhmRuntimeMigrationShimText -LauncherPath $Scope.LauncherTargetPath) `
            -Encoding Ascii `
            -NoNewline
        if (-not $Scope.IsTest -and
            (Get-LhmFileSha256 -Path $temporaryPath) -cne $script:LhmPublicShimSha256) {
            throw 'Generated public shim does not match the pinned production hash.'
        }
        Move-Item -LiteralPath $temporaryPath -Destination $destination -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Copy-LhmRuntimeMigrationTree {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination
    )

    $null = Assert-LhmNormalDirectoryTree -Path $Source -Label 'Legacy runtime root'
    Assert-LhmInstalledRootInventory -InstallRoot $Source
    $null = Get-LhmInstalledPayloadState -InstallRoot $Source
    $null = Read-LhmRuntimeConfig `
        -Path (Join-Path $Source $script:LhmRuntimeConfigName) `
        -ExpectedDataRoot $DataRoot `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath

    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($entry in @(Get-ChildItem -LiteralPath $Source -Force)) {
        Copy-Item `
            -LiteralPath $entry.FullName `
            -Destination (Join-Path $Destination $entry.Name) `
            -Recurse `
            -Force
    }
    Assert-LhmInstalledRootInventory -InstallRoot $Destination
    $sourcePayload = Get-LhmInstalledPayloadState -InstallRoot $Source
    $copiedPayload = Get-LhmInstalledPayloadState -InstallRoot $Destination
    if ([string]$sourcePayload.Sha256 -cne [string]$copiedPayload.Sha256 -or
        [string]$sourcePayload.Manifest.releaseId -cne
            [string]$copiedPayload.Manifest.releaseId) {
        throw 'Copied runtime payload does not match the legacy runtime.'
    }
    $null = Read-LhmRuntimeConfig `
        -Path (Join-Path $Destination $script:LhmRuntimeConfigName) `
        -ExpectedDataRoot $DataRoot `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath
}

function Remove-LhmRuntimeMigrationDirectory {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ExpectedPath
    )

    if (-not (Test-LhmPathEqual -Left $Path -Right $ExpectedPath)) {
        throw "Refusing to remove unexpected runtime migration path '$Path'."
    }
    if (Test-Path -LiteralPath $Path -PathType Container) {
        $null = Assert-LhmNormalDirectoryTree -Path $Path -Label 'Runtime migration directory'
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Remove-LhmEmptyCompatibilityParent {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }
    $null = Assert-LhmNormalDirectoryTree -Path $Path -Label 'Compatibility parent'
    if (@(Get-ChildItem -LiteralPath $Path -Force).Count -eq 0) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Assert-LhmRuntimeMigrationTarget {
    param([Parameter(Mandatory)][pscustomobject] $Scope)

    if (Test-Path -LiteralPath $Scope.LegacyInstallRoot) {
        throw "Legacy runtime root still exists: '$($Scope.LegacyInstallRoot)'."
    }
    if (Test-Path -LiteralPath $Scope.LegacyLauncherPath) {
        throw "Legacy launcher still exists: '$($Scope.LegacyLauncherPath)'."
    }

    Assert-LhmInstalledRootInventory -InstallRoot $Scope.InstallRoot
    $payload = Get-LhmInstalledPayloadState -InstallRoot $Scope.InstallRoot
    if ($null -eq $payload) {
        throw 'Migrated runtime payload is absent.'
    }
    $null = Read-LhmRuntimeConfig `
        -Path (Join-Path $Scope.InstallRoot $script:LhmRuntimeConfigName) `
        -ExpectedDataRoot $Scope.DataRoot `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath

    $canonicalLauncher = Join-Path $PSScriptRoot 'Start-LibreHardwareMonitor.ps1'
    if (-not (Test-Path -LiteralPath $Scope.LauncherTargetPath -PathType Leaf) -or
        (Get-LhmFileSha256 -Path $Scope.LauncherTargetPath) -cne
            (Get-LhmFileSha256 -Path $canonicalLauncher)) {
        throw 'Migrated launcher does not match canonical source.'
    }
    if (-not (Test-Path -LiteralPath $Scope.PublicShimPath -PathType Leaf)) {
        throw 'Migrated public shim is absent.'
    }
    $expectedShimText = Get-LhmRuntimeMigrationShimText `
        -LauncherPath $Scope.LauncherTargetPath
    $actualShimText =
        [System.IO.File]::ReadAllText($Scope.PublicShimPath) -replace "`r?`n", "`r`n"
    if ($actualShimText -cne $expectedShimText) {
        throw 'Migrated public shim does not match the complete launcher chain.'
    }
    if (-not $Scope.IsTest -and
        (Get-LhmFileSha256 -Path $Scope.PublicShimPath) -cne
            $script:LhmPublicShimSha256) {
        throw 'Migrated public shim does not match the pinned production hash.'
    }
    $hideLaunchAuthority =
        Assert-LhmRuntimeMigrationHideLaunchCurrent -Scope $Scope
    Assert-LhmRuntimeMigrationTaskTarget `
        -Scope $Scope `
        -ExpectedInstallRoot $Scope.InstallRoot

    if (-not $Scope.IsTest) {
        $expectedExecutable = Join-Path $Scope.InstallRoot $script:LhmExecutableName
        $processes = @(Get-LhmProcesses -ExpectedExecutablePath $expectedExecutable)
        if ($processes.Count -ne 1 -or -not $processes[0].IsExact) {
            throw 'Runtime migration requires one exact migrated process.'
        }
        Assert-LhmHttpHealth `
            -HealthUri $HealthUri `
            -TimeoutSeconds $ActivationTimeoutSeconds
    }

    return [pscustomobject][ordered]@{
        Result = 'PASS'
        Mode = 'Validate'
        InstallRoot = $Scope.InstallRoot
        LauncherTargetPath = $Scope.LauncherTargetPath
        PublicShimPath = $Scope.PublicShimPath
        RuntimeHideLaunchPath = $Scope.RuntimeHideLaunchPath
        HideLaunchSha256 = [string]$hideLaunchAuthority.ArtifactSha256
        ReleaseId = [string]$payload.Manifest.releaseId
        LegacyInstallRootAbsent = $true
        LegacyLauncherAbsent = $true
        MutationPerformed = $false
    }
}

$scope = Resolve-LhmRuntimeMigrationScope

if ($Mode -ceq 'Validate') {
    Assert-LhmRuntimeMigrationTarget -Scope $scope
    return
}

$legacyExists = Test-Path -LiteralPath $scope.LegacyInstallRoot -PathType Container
$targetExists = Test-Path -LiteralPath $scope.InstallRoot -PathType Container
if ($Mode -ceq 'Plan') {
    $hideLaunchCurrent = $true
    $hideLaunchIssue = $null
    try {
        $null = Assert-LhmRuntimeMigrationHideLaunchCurrent -Scope $scope
    }
    catch {
        $hideLaunchCurrent = $false
        $hideLaunchIssue = $_.Exception.Message
    }
    [pscustomobject][ordered]@{
        Result = if ($hideLaunchCurrent) { 'PASS' } else { 'DRIFT' }
        Mode = 'Plan'
        LegacyInstallRoot = $scope.LegacyInstallRoot
        InstallRoot = $scope.InstallRoot
        LegacyRuntimePresent = $legacyExists
        MigratedRuntimePresent = $targetExists
        LegacyLauncherPresent = Test-Path -LiteralPath $scope.LegacyLauncherPath -PathType Leaf
        HideLaunchCurrent = $hideLaunchCurrent
        HideLaunchIssue = $hideLaunchIssue
        MutationPerformed = $false
    }
    return
}

$null = Assert-LhmRuntimeMigrationHideLaunchCurrent -Scope $scope

if ($targetExists -and -not $legacyExists) {
    Assert-LhmRuntimeMigrationTarget -Scope $scope
    return
}
if (-not $legacyExists -or $targetExists) {
    throw 'Runtime migration requires the exact legacy root present and target root absent.'
}

$null = Assert-LhmNormalDirectoryTree `
    -Path $scope.LegacyInstallRoot `
    -Label 'Legacy runtime root'
Assert-LhmInstalledRootInventory -InstallRoot $scope.LegacyInstallRoot
$legacyPayload = Get-LhmInstalledPayloadState -InstallRoot $scope.LegacyInstallRoot
if ($null -eq $legacyPayload) {
    throw 'Legacy runtime payload is absent.'
}
$null = Read-LhmRuntimeConfig `
    -Path (Join-Path $scope.LegacyInstallRoot $script:LhmRuntimeConfigName) `
    -ExpectedDataRoot $scope.DataRoot `
    -ExpectedManagedTaskPath $ManagedStartupTaskPath
Assert-LhmRuntimeMigrationTaskTarget `
    -Scope $scope `
    -ExpectedInstallRoot $scope.LegacyInstallRoot

if (-not (Test-Path -LiteralPath $scope.LegacyLauncherPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $scope.PublicShimPath -PathType Leaf)) {
    throw 'Legacy launcher and public shim must both exist before migration.'
}
if (-not $scope.IsTest -and
    ((Get-LhmFileSha256 -Path $scope.LegacyLauncherPath) -cne
        $script:LhmLegacyLauncherSha256 -or
    (Get-LhmFileSha256 -Path $scope.PublicShimPath) -cne
        $script:LhmLegacyPublicShimSha256)) {
    throw 'Legacy launcher or public shim does not match the reviewed pre-migration hashes.'
}

$canonicalLauncher = Join-Path $PSScriptRoot 'Start-LibreHardwareMonitor.ps1'
$null = Assert-LhmNormalFile -Path $canonicalLauncher -Label 'Canonical launcher'
$targetParent = Split-Path -Parent $scope.InstallRoot
$launcherParent = Split-Path -Parent $scope.LauncherTargetPath
$recoveryRoot = Join-Path $scope.DataRoot 'release-recovery\runtime-root-relocation'
$stageRoot = Join-Path `
    $targetParent `
    ".Runtime-migration-$([guid]::NewGuid().ToString('N'))"
$taskBackupPath = Join-Path `
    $recoveryRoot `
    $(if ($scope.IsTest) { 'managed-task.json' } else { 'managed-task.xml' })
$legacyLauncherBackup = Join-Path $recoveryRoot 'legacy-launcher.ps1'
$legacyShimBackup = Join-Path $recoveryRoot 'legacy-shim.cmd'
$manifestPath = Join-Path $recoveryRoot 'manifest.json'

if (Test-Path -LiteralPath $recoveryRoot) {
    throw "Runtime migration recovery already exists: '$recoveryRoot'."
}

if (-not $PSCmdlet.ShouldProcess(
    $scope.LegacyInstallRoot,
    "Move LibreHW runtime authority to '$($scope.InstallRoot)' and update its launcher/task bindings")) {
    [pscustomobject][ordered]@{
        Result = 'PASS'
        Mode = 'Apply'
        Planned = $true
        MutationPerformed = $false
    }
    return
}

$oldProcessStopped = $false
$bindingsChanged = $false
$activationPassed = $false

try {
    [System.IO.Directory]::CreateDirectory($recoveryRoot) | Out-Null
    Copy-Item -LiteralPath $scope.LegacyLauncherPath -Destination $legacyLauncherBackup
    Copy-Item -LiteralPath $scope.PublicShimPath -Destination $legacyShimBackup
    if ($scope.IsTest) {
        Copy-Item -LiteralPath $TestExternalTaskStatePath -Destination $taskBackupPath
    }
    else {
        $parts = Split-LhmManagedTaskPath -ManagedStartupTaskPath $ManagedStartupTaskPath
        Export-LhmScheduledTaskSnapshot `
            -TaskPath $parts.TaskPath `
            -TaskName $parts.TaskName `
            -SnapshotPath $taskBackupPath
    }

    [pscustomobject][ordered]@{
        schema = 'sq.librehw.runtime-root-relocation.v1'
        phase = 'prepared'
        machineId = if ($scope.IsTest) { 'fixture' } else { $script:LhmExpectedMachineId }
        legacyInstallRoot = $scope.LegacyInstallRoot
        installRoot = $scope.InstallRoot
        legacyLauncherPath = $scope.LegacyLauncherPath
        launcherTargetPath = $scope.LauncherTargetPath
        publicShimPath = $scope.PublicShimPath
        managedStartupTaskPath = $ManagedStartupTaskPath
        releaseId = [string]$legacyPayload.Manifest.releaseId
        legacyExecutableSha256 = [string]$legacyPayload.Sha256
        legacyLauncherSha256 = Get-LhmFileSha256 -Path $legacyLauncherBackup
        legacyShimSha256 = Get-LhmFileSha256 -Path $legacyShimBackup
        preparedAtUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    Copy-LhmRuntimeMigrationTree `
        -Source $scope.LegacyInstallRoot `
        -Destination $stageRoot
    Invoke-TestFailurePoint -Point 'AfterRuntimeCopy'

    if (-not $scope.IsTest) {
        Stop-LhmExactProcessForRelease `
            -ExpectedExecutablePath (Join-Path `
                $scope.LegacyInstallRoot `
                $script:LhmExecutableName) `
            -AllowStopExactProcess
        $oldProcessStopped = $true
    }

    [System.IO.Directory]::CreateDirectory($targetParent) | Out-Null
    Move-Item -LiteralPath $stageRoot -Destination $scope.InstallRoot
    [System.IO.Directory]::CreateDirectory($launcherParent) | Out-Null
    Copy-Item `
        -LiteralPath $canonicalLauncher `
        -Destination $scope.LauncherTargetPath `
        -Force
    Write-LhmRuntimeMigrationShim -Scope $scope
    Set-LhmRuntimeMigrationTaskTarget `
        -Scope $scope `
        -TargetInstallRoot $scope.InstallRoot
    $bindingsChanged = $true
    Invoke-TestFailurePoint -Point 'AfterBindings'

    if (-not $scope.IsTest) {
        $null = Start-LhmManagedTaskAndWait `
            -ManagedStartupTaskPath $ManagedStartupTaskPath `
            -ExpectedExecutablePath (Join-Path `
                $scope.InstallRoot `
                $script:LhmExecutableName) `
            -TimeoutSeconds $ActivationTimeoutSeconds
        Assert-LhmHttpHealth `
            -HealthUri $HealthUri `
            -TimeoutSeconds $ActivationTimeoutSeconds
    }
    $activationPassed = $true

    Remove-LhmRuntimeMigrationDirectory `
        -Path $scope.LegacyInstallRoot `
        -ExpectedPath $scope.LegacyInstallRoot
    Remove-Item -LiteralPath $scope.LegacyLauncherPath -Force
    foreach ($parent in @(
        (Split-Path -Parent $scope.LegacyLauncherPath),
        (Split-Path -Parent (Split-Path -Parent $scope.LegacyLauncherPath)),
        (Split-Path -Parent $scope.LegacyInstallRoot),
        (Split-Path -Parent (Split-Path -Parent $scope.LegacyInstallRoot))
    )) {
        Remove-LhmEmptyCompatibilityParent -Path $parent
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.phase = 'complete'
    $manifest | Add-Member `
        -NotePropertyName completedAtUtc `
        -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    $result = Assert-LhmRuntimeMigrationTarget -Scope $scope
    $result.Mode = 'Apply'
    $result.MutationPerformed = $true
    $result | Add-Member -NotePropertyName RecoveryRoot -NotePropertyValue $recoveryRoot
    $result
}
catch {
    $failure = $_
    if (-not $activationPassed) {
        if (-not $scope.IsTest -and $oldProcessStopped) {
            try {
                Stop-LhmExactProcessForRelease `
                    -ExpectedExecutablePath (Join-Path `
                        $scope.InstallRoot `
                        $script:LhmExecutableName) `
                    -AllowStopExactProcess
            }
            catch {
            }
        }

        if ($bindingsChanged) {
            if ($scope.IsTest) {
                Copy-Item `
                    -LiteralPath $taskBackupPath `
                    -Destination $TestExternalTaskStatePath `
                    -Force
            }
            else {
                $parts = Split-LhmManagedTaskPath `
                    -ManagedStartupTaskPath $ManagedStartupTaskPath
                Restore-LhmScheduledTaskSnapshot `
                    -TaskPath $parts.TaskPath `
                    -TaskName $parts.TaskName `
                    -Existed $true `
                    -SnapshotPath $taskBackupPath
            }
            Copy-Item `
                -LiteralPath $legacyShimBackup `
                -Destination $scope.PublicShimPath `
                -Force
        }

        if (Test-Path -LiteralPath $scope.LauncherTargetPath -PathType Leaf) {
            Remove-Item -LiteralPath $scope.LauncherTargetPath -Force
        }
        Remove-LhmRuntimeMigrationDirectory `
            -Path $scope.InstallRoot `
            -ExpectedPath $scope.InstallRoot
        Remove-LhmRuntimeMigrationDirectory `
            -Path $stageRoot `
            -ExpectedPath $stageRoot

        if (-not $scope.IsTest -and $oldProcessStopped) {
            $null = Start-LhmManagedTaskAndWait `
                -ManagedStartupTaskPath $ManagedStartupTaskPath `
                -ExpectedExecutablePath (Join-Path `
                    $scope.LegacyInstallRoot `
                    $script:LhmExecutableName) `
                -TimeoutSeconds $ActivationTimeoutSeconds
            Assert-LhmHttpHealth `
                -HealthUri $HealthUri `
                -TimeoutSeconds $ActivationTimeoutSeconds
        }
    }
    throw $failure
}
finally {
    if (Test-Path -LiteralPath $stageRoot -PathType Container) {
        Remove-LhmRuntimeMigrationDirectory `
            -Path $stageRoot `
            -ExpectedPath $stageRoot
    }
}

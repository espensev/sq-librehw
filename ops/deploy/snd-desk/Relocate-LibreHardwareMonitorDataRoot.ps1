[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string] $InstallRoot = 'E:\SQ_HQ\Monitoring\LibreHW',

    [string] $SourceDataRoot = 'E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor',

    [string] $DataRoot = 'E:\Data\LibreHardwareMonitor',

    [string] $ManagedStartupTaskPath = '\SevGrp\AdminTask\LibreHW-No-UAC',

    [string] $LauncherTargetPath =
        'E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1',

    [string] $PublicShimPath = 'E:\SQ_HQ\u-programs\bin\librehw.cmd',

    [uri] $HealthUri = 'http://localhost:8085/data.json',

    [ValidateRange(5, 300)]
    [int] $ActivationTimeoutSeconds = 45,

    [Parameter(Mandatory)]
    [switch] $DataMoveAlreadyCompleted,

    [switch] $NonLiveTestMode,

    [string] $TestExternalStateRoot,

    [ValidateSet(
        'None',
        'AfterRecovery',
        'AfterRuntimeConfig',
        'AfterLauncher',
        'AfterTaskReadback',
        'AfterTaskEnabled')]
    [string] $TestFailurePoint = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')

function Invoke-TestFailurePoint {
    param([Parameter(Mandatory)][string] $Point)
    if ($TestFailurePoint -ceq $Point) {
        throw "Injected non-live data-root relocation failure: $Point"
    }
}

if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity
}

function Assert-LhmRelocationHealthUri {
    param(
        [Parameter(Mandatory)][uri] $HealthUri,
        [switch] $IsTest
    )

    if (-not $IsTest -and
        [string]$HealthUri.AbsoluteUri -cne $script:LhmProductionHealthUri) {
        throw "Production health URI must be '$($script:LhmProductionHealthUri)'."
    }
    return $HealthUri
}

function Get-LhmRelocationTaskState {
    param(
        [Parameter(Mandatory)][string] $ManagedTaskPath,
        [switch] $IsTest,
        [string] $ExternalStateRoot
    )

    if ($IsTest) {
        if ([string]::IsNullOrWhiteSpace($ExternalStateRoot)) {
            throw 'TestExternalStateRoot is required for non-live relocation.'
        }
        $taskStatePath = Join-Path $ExternalStateRoot 'managed-task.json'
        $taskStatePath = Assert-LhmNormalFile `
            -Path $taskStatePath `
            -Label 'Non-live managed-task state'
        if ((Get-Item -LiteralPath $taskStatePath).Length -gt 65536) {
            throw 'Non-live managed-task state is unexpectedly large.'
        }
        try {
            return Get-Content -LiteralPath $taskStatePath -Raw -Encoding UTF8 |
                ConvertFrom-Json
        }
        catch {
            throw "Non-live managed-task state is invalid JSON: $($_.Exception.Message)"
        }
    }

    $taskParts = Split-LhmManagedTaskPath -ManagedStartupTaskPath $ManagedTaskPath
    $task = Get-ScheduledTask `
        -TaskPath $taskParts.TaskPath `
        -TaskName $taskParts.TaskName `
        -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        throw "Managed task '$ManagedTaskPath' does not exist."
    }
    return $task
}

function Assert-LhmRelocationTaskContract {
    param(
        [Parameter(Mandatory)] $Task,
        [Parameter(Mandatory)][string] $ManagedTaskPath,
        [Parameter(Mandatory)][string] $ExpectedExecutablePath,
        [Parameter(Mandatory)][string] $ExpectedInstallRoot,
        [switch] $IsTest,
        [switch] $RequireDisabled,
        [switch] $RequireEnabled
    )

    if ($RequireDisabled -and $RequireEnabled) {
        throw 'Managed-task validation cannot require both disabled and enabled state.'
    }

    if ($IsTest) {
        $expectedProperties = @(@(
            'allowHardTerminate',
            'arguments',
            'enabled',
            'execute',
            'logonType',
            'multipleInstances',
            'principalUserId',
            'runLevel',
            'startWhenAvailable',
            'taskPath',
            'trigger',
            'triggerUserId',
            'workingDirectory'
        ) | Sort-Object)
        $actualProperties = @($Task.PSObject.Properties.Name | Sort-Object)
        if (($actualProperties -join "`n") -cne ($expectedProperties -join "`n") -or
            [string]$Task.taskPath -cne $ManagedTaskPath -or
            -not (Test-LhmPathEqual `
                -Left ([string]$Task.execute) `
                -Right $ExpectedExecutablePath) -or
            -not (Test-LhmPathEqual `
                -Left ([string]$Task.workingDirectory) `
                -Right $ExpectedInstallRoot) -or
            -not [string]::IsNullOrEmpty([string]$Task.arguments) -or
            -not [string]::Equals(
                [string]$Task.principalUserId,
                $script:LhmManagedTaskPrincipalUserId,
                [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]$Task.runLevel -cne 'Highest' -or
            [string]$Task.logonType -cne 'InteractiveToken' -or
            [string]$Task.trigger -cne 'LogonAndOnDemand' -or
            -not [string]::Equals(
                [string]$Task.triggerUserId,
                $script:LhmManagedTaskLogonUserId,
                [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]$Task.multipleInstances -cne 'IgnoreNew' -or
            $Task.startWhenAvailable -isnot [bool] -or
            -not [bool]$Task.startWhenAvailable -or
            $Task.allowHardTerminate -isnot [bool] -or
            [bool]$Task.allowHardTerminate -or
            $Task.enabled -isnot [bool]) {
            throw "Managed task '$ManagedTaskPath' does not match the exact relocation contract."
        }
        $enabled = [bool]$Task.enabled
    }
    else {
        $actions = @($Task.Actions)
        $triggers = @($Task.Triggers)
        if ($actions.Count -ne 1 -or
            -not (Test-LhmPathEqual `
                -Left ([string]$actions[0].Execute) `
                -Right $ExpectedExecutablePath) -or
            -not (Test-LhmPathEqual `
                -Left ([string]$actions[0].WorkingDirectory) `
                -Right $ExpectedInstallRoot) -or
            -not [string]::IsNullOrEmpty([string]$actions[0].Arguments) -or
            -not [string]::Equals(
                [string]$Task.Principal.UserId,
                $script:LhmManagedTaskPrincipalUserId,
                [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]$Task.Principal.LogonType -cne 'Interactive' -or
            [string]$Task.Principal.RunLevel -cne 'Highest' -or
            [string]$Task.Settings.MultipleInstances -cne 'IgnoreNew' -or
            -not [bool]$Task.Settings.StartWhenAvailable -or
            [bool]$Task.Settings.AllowHardTerminate -or
            $triggers.Count -ne 1 -or
            [string]$triggers[0].CimClass.CimClassName -cne 'MSFT_TaskLogonTrigger' -or
            -not [bool]$triggers[0].Enabled -or
            -not [string]::Equals(
                [string]$triggers[0].UserId,
                $script:LhmManagedTaskLogonUserId,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Managed task '$ManagedTaskPath' does not match the exact relocation contract."
        }
        $enabled =
            [string]$Task.State -cne 'Disabled' -and
            [bool]$Task.Settings.Enabled
    }

    if ($RequireDisabled -and $enabled) {
        throw "Managed task '$ManagedTaskPath' must be disabled before relocation."
    }
    if ($RequireEnabled -and -not $enabled) {
        throw "Managed task '$ManagedTaskPath' did not read back as enabled."
    }

    return [pscustomobject]@{
        Task = $Task
        Enabled = $enabled
    }
}

function Set-LhmRelocationTestTaskEnabled {
    param(
        [Parameter(Mandatory)][string] $ExternalStateRoot,
        [Parameter(Mandatory)][string] $ManagedTaskPath,
        [Parameter(Mandatory)][string] $ExpectedExecutablePath,
        [Parameter(Mandatory)][string] $ExpectedInstallRoot,
        [switch] $Enabled
    )

    $task = Get-LhmRelocationTaskState `
        -ManagedTaskPath $ManagedTaskPath `
        -IsTest `
        -ExternalStateRoot $ExternalStateRoot
    $state = Assert-LhmRelocationTaskContract `
        -Task $task `
        -ManagedTaskPath $ManagedTaskPath `
        -ExpectedExecutablePath $ExpectedExecutablePath `
        -ExpectedInstallRoot $ExpectedInstallRoot `
        -IsTest
    if ([bool]$state.Enabled -eq [bool]$Enabled) {
        return
    }

    $taskStatePath = Join-Path $ExternalStateRoot 'managed-task.json'
    $stagePath = Join-Path `
        $ExternalStateRoot `
        ".managed-task.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [ordered]@{
            taskPath = [string]$task.taskPath
            execute = [string]$task.execute
            arguments = $task.arguments
            workingDirectory = [string]$task.workingDirectory
            principalUserId = [string]$task.principalUserId
            runLevel = [string]$task.runLevel
            logonType = [string]$task.logonType
            trigger = [string]$task.trigger
            triggerUserId = [string]$task.triggerUserId
            multipleInstances = [string]$task.multipleInstances
            startWhenAvailable = [bool]$task.startWhenAvailable
            allowHardTerminate = [bool]$task.allowHardTerminate
            enabled = [bool]$Enabled
        } | ConvertTo-Json | Set-Content -LiteralPath $stagePath -Encoding UTF8
        $null = Assert-LhmNormalFile `
            -Path $stagePath `
            -Label 'Staged non-live managed-task state'
        Move-Item -LiteralPath $stagePath -Destination $taskStatePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $stagePath) {
            Remove-Item -LiteralPath $stagePath -Force
        }
    }
}

function Assert-LhmRelocationTaskXmlBackup {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ManagedTaskPath,
        [Parameter(Mandatory)][string] $ExpectedExecutablePath,
        [Parameter(Mandatory)][string] $ExpectedInstallRoot
    )

    $taskXml = Read-LhmSafeTaskXml `
        -Path $Path `
        -Label 'Data-root relocation managed-task backup'
    $namespace = [System.Xml.XmlNamespaceManager]::new($taskXml.NameTable)
    $namespace.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    $actionNodes = @($taskXml.SelectNodes('/t:Task/t:Actions/*', $namespace))
    $triggerNodes = @($taskXml.SelectNodes('/t:Task/t:Triggers/*', $namespace))
    $principalNodes = @($taskXml.SelectNodes('/t:Task/t:Principals/t:Principal', $namespace))
    $commandNode = $taskXml.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:Command', $namespace)
    $argumentsNode = $taskXml.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:Arguments', $namespace)
    $workingNode =
        $taskXml.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:WorkingDirectory', $namespace)
    $userNode =
        $taskXml.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:UserId', $namespace)
    $triggerUserNode =
        $taskXml.SelectSingleNode('/t:Task/t:Triggers/t:LogonTrigger/t:UserId', $namespace)
    $logonNode =
        $taskXml.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:LogonType', $namespace)
    $runLevelNode =
        $taskXml.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:RunLevel', $namespace)
    $uriNode = $taskXml.SelectSingleNode('/t:Task/t:RegistrationInfo/t:URI', $namespace)
    $multipleNode =
        $taskXml.SelectSingleNode('/t:Task/t:Settings/t:MultipleInstancesPolicy', $namespace)
    $startAvailableNode =
        $taskXml.SelectSingleNode('/t:Task/t:Settings/t:StartWhenAvailable', $namespace)
    $hardTerminateNode =
        $taskXml.SelectSingleNode('/t:Task/t:Settings/t:AllowHardTerminate', $namespace)
    $enabledNode = $taskXml.SelectSingleNode('/t:Task/t:Settings/t:Enabled', $namespace)
    if ($actionNodes.Count -ne 1 -or
        $actionNodes[0].LocalName -cne 'Exec' -or
        $triggerNodes.Count -ne 1 -or
        $triggerNodes[0].LocalName -cne 'LogonTrigger' -or
        $principalNodes.Count -ne 1 -or
        $null -eq $commandNode -or
        -not (Test-LhmPathEqual `
            -Left $commandNode.InnerText `
            -Right $ExpectedExecutablePath) -or
        ($null -ne $argumentsNode -and
            -not [string]::IsNullOrEmpty($argumentsNode.InnerText)) -or
        $null -eq $workingNode -or
        -not (Test-LhmPathEqual `
            -Left $workingNode.InnerText `
            -Right $ExpectedInstallRoot) -or
        $null -eq $userNode -or
        $userNode.InnerText -cne $script:LhmManagedTaskPrincipalSid -or
        $null -eq $triggerUserNode -or
        -not [string]::Equals(
            $triggerUserNode.InnerText,
            $script:LhmManagedTaskLogonUserId,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        $null -eq $logonNode -or
        $logonNode.InnerText -cne 'InteractiveToken' -or
        $null -eq $runLevelNode -or
        $runLevelNode.InnerText -cne 'HighestAvailable' -or
        $null -eq $uriNode -or
        $uriNode.InnerText -cne $ManagedTaskPath -or
        $null -eq $multipleNode -or
        $multipleNode.InnerText -cne 'IgnoreNew' -or
        $null -eq $startAvailableNode -or
        $startAvailableNode.InnerText -cne 'true' -or
        $null -eq $hardTerminateNode -or
        $hardTerminateNode.InnerText -cne 'false' -or
        $null -eq $enabledNode -or
        $enabledNode.InnerText -cne 'false') {
        throw 'Data-root relocation managed-task backup does not match the exact disabled task contract.'
    }
}

function Read-LhmDataRootRelocationRecovery {
    param(
        [Parameter(Mandatory)][string] $RecoveryRoot,
        [Parameter(Mandatory)][string] $ExpectedInstallRoot,
        [Parameter(Mandatory)][string] $ExpectedSourceDataRoot,
        [Parameter(Mandatory)][string] $ExpectedDataRoot,
        [Parameter(Mandatory)][string] $ExpectedRuntimeConfigPath,
        [Parameter(Mandatory)][string] $ExpectedLauncherTargetPath,
        [Parameter(Mandatory)][string] $ExpectedManagedTaskPath,
        [Parameter(Mandatory)][string] $ExpectedExecutablePath,
        [Parameter(Mandatory)][string] $ExpectedPublicShimPath,
        [Parameter(Mandatory)][string] $ExpectedPublicShimSha256,
        [string] $ExpectedLauncherBackupSha256,
        [switch] $IsTest
    )

    $RecoveryRoot = Assert-LhmNormalDirectoryTree `
        -Path $RecoveryRoot `
        -Label 'Data-root relocation recovery'
    $manifestPath = Join-Path $RecoveryRoot 'recovery.json'
    $runtimeBackupPath = Join-Path $RecoveryRoot 'runtime-config-backup.json'
    $launcherBackupPath = Join-Path $RecoveryRoot 'launcher-backup.ps1'
    $taskBackupName = if ($IsTest) {
        'managed-task.test.json'
    }
    else {
        'managed-task.xml'
    }
    $taskBackupPath = Join-Path $RecoveryRoot $taskBackupName
    $allowedNames = @(
        'recovery.json',
        'runtime-config-backup.json',
        'launcher-backup.ps1',
        $taskBackupName
    )
    $entries = @(Get-ChildItem -LiteralPath $RecoveryRoot -Force)
    if ($entries.Count -ne $allowedNames.Count -or
        @($entries | Where-Object {
            $_.PSIsContainer -or
            ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or
            $_.Name -cnotin $allowedNames
        }).Count -gt 0) {
        throw 'Data-root relocation recovery contains unexpected or unsafe entries.'
    }
    foreach ($recoveryFile in @(
        $manifestPath,
        $runtimeBackupPath,
        $launcherBackupPath,
        $taskBackupPath
    )) {
        $null = Assert-LhmNormalFile `
            -Path $recoveryFile `
            -Label 'Data-root relocation recovery file'
    }
    if ((Get-Item -LiteralPath $manifestPath).Length -gt 65536 -or
        (Get-Item -LiteralPath $runtimeBackupPath).Length -gt 65536 -or
        (Get-Item -LiteralPath $launcherBackupPath).Length -gt 1048576 -or
        (Get-Item -LiteralPath $taskBackupPath).Length -gt 1048576) {
        throw 'Data-root relocation recovery contains an oversized file.'
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "Data-root relocation recovery manifest is invalid JSON: $($_.Exception.Message)"
    }
    $expectedProperties = @(@(
        'createdAt',
        'dataRoot',
        'installRoot',
        'launcherBackup',
        'launcherBackupSha256',
        'launcherTargetPath',
        'managedTaskBackup',
        'managedTaskBackupSha256',
        'managedTaskPath',
        'publicShimPath',
        'publicShimSha256',
        'runtimeConfigBackup',
        'runtimeConfigBackupSha256',
        'runtimeConfigPath',
        'schema',
        'sourceDataRoot'
    ) | Sort-Object)
    $actualProperties = @($manifest.PSObject.Properties.Name | Sort-Object)
    if (($actualProperties -join "`n") -cne ($expectedProperties -join "`n")) {
        throw 'Data-root relocation recovery manifest contains unsupported or missing fields.'
    }
    $createdAt = [DateTimeOffset]::MinValue
    if ([string]$manifest.schema -cne 'sq.librehw.data-root-relocation-recovery.v1' -or
        -not [DateTimeOffset]::TryParse(
            [string]$manifest.createdAt,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$createdAt) -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.installRoot) `
            -Right $ExpectedInstallRoot) -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.sourceDataRoot) `
            -Right $ExpectedSourceDataRoot) -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.dataRoot) `
            -Right $ExpectedDataRoot) -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.runtimeConfigPath) `
            -Right $ExpectedRuntimeConfigPath) -or
        [string]$manifest.runtimeConfigBackup -cne 'runtime-config-backup.json' -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.launcherTargetPath) `
            -Right $ExpectedLauncherTargetPath) -or
        [string]$manifest.launcherBackup -cne 'launcher-backup.ps1' -or
        [string]$manifest.managedTaskPath -cne $ExpectedManagedTaskPath -or
        [string]$manifest.managedTaskBackup -cne $taskBackupName -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.publicShimPath) `
            -Right $ExpectedPublicShimPath) -or
        [string]$manifest.publicShimSha256 -cne $ExpectedPublicShimSha256) {
        throw 'Data-root relocation recovery manifest is incompatible with this operation.'
    }
    foreach ($hashProperty in @(
        'runtimeConfigBackupSha256',
        'launcherBackupSha256',
        'managedTaskBackupSha256',
        'publicShimSha256'
    )) {
        if ([string]$manifest.$hashProperty -cnotmatch '^[0-9a-f]{64}$') {
            throw "Data-root relocation recovery '$hashProperty' is not a SHA-256 hash."
        }
    }
    if ((Get-LhmFileSha256 -Path $runtimeBackupPath) -cne
            [string]$manifest.runtimeConfigBackupSha256 -or
        (Get-LhmFileSha256 -Path $launcherBackupPath) -cne
            [string]$manifest.launcherBackupSha256 -or
        (Get-LhmFileSha256 -Path $taskBackupPath) -cne
            [string]$manifest.managedTaskBackupSha256) {
        throw 'Data-root relocation recovery file hash validation failed.'
    }
    $trustedLauncherHash = if ($IsTest -and
        -not [string]::IsNullOrWhiteSpace($ExpectedLauncherBackupSha256)) {
        $ExpectedLauncherBackupSha256
    }
    elseif ($IsTest) {
        [string]$manifest.launcherBackupSha256
    }
    else {
        $script:LhmPreRelocationLauncherSha256
    }
    if ([string]$manifest.launcherBackupSha256 -cne $trustedLauncherHash) {
        throw 'Data-root relocation launcher backup does not match trusted pre-relocation content.'
    }
    if ((Get-LhmFileSha256 -Path $ExpectedPublicShimPath) -cne
        $ExpectedPublicShimSha256) {
        throw 'Public librehw.cmd does not match the data-root relocation recovery packet.'
    }

    $null = Read-LhmRuntimeConfig `
        -Path $runtimeBackupPath `
        -ExpectedDataRoot $ExpectedSourceDataRoot `
        -ExpectedManagedTaskPath $ExpectedManagedTaskPath
    $launcherTokens = $null
    $launcherErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $launcherBackupPath,
        [ref]$launcherTokens,
        [ref]$launcherErrors)
    if ($launcherErrors.Count -gt 0) {
        throw "Data-root relocation launcher backup does not parse: $($launcherErrors[0].Message)"
    }
    if ($IsTest) {
        $taskBackup = Get-Content -LiteralPath $taskBackupPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        $null = Assert-LhmRelocationTaskContract `
            -Task $taskBackup `
            -ManagedTaskPath $ExpectedManagedTaskPath `
            -ExpectedExecutablePath $ExpectedExecutablePath `
            -ExpectedInstallRoot $ExpectedInstallRoot `
            -IsTest `
            -RequireDisabled
    }
    else {
        Assert-LhmRelocationTaskXmlBackup `
            -Path $taskBackupPath `
            -ManagedTaskPath $ExpectedManagedTaskPath `
            -ExpectedExecutablePath $ExpectedExecutablePath `
            -ExpectedInstallRoot $ExpectedInstallRoot
    }

    return [pscustomobject]@{
        Root = $RecoveryRoot
        Manifest = $manifest
        ManifestPath = $manifestPath
        RuntimeConfigBackupPath = $runtimeBackupPath
        LauncherBackupPath = $launcherBackupPath
        ManagedTaskBackupPath = $taskBackupPath
    }
}

if (-not $DataMoveAlreadyCompleted) {
    throw 'Use -DataMoveAlreadyCompleted only after the mutable data is present at the new root and the old root is absent.'
}

$mode = Assert-LhmOperationMode `
    -InstallRoot $InstallRoot `
    -DataRoot $DataRoot `
    -ManagedStartupTaskPath $ManagedStartupTaskPath `
    -NonLiveTestMode:$NonLiveTestMode
$InstallRoot = $mode.InstallRoot
$DataRoot = $mode.DataRoot
$HealthUri = Assert-LhmRelocationHealthUri `
    -HealthUri $HealthUri `
    -IsTest:$mode.IsTest
$SourceDataRoot = Resolve-LhmFullPath -Path $SourceDataRoot
$LauncherTargetPath = Resolve-LhmFullPath -Path $LauncherTargetPath
$PublicShimPath = Resolve-LhmFullPath -Path $PublicShimPath

if ($mode.IsTest) {
    $tempRoot = Resolve-LhmFullPath -Path ([System.IO.Path]::GetTempPath())
    if (-not (Test-LhmPathWithin -Path $SourceDataRoot -Root $tempRoot) -or
        -not (Test-LhmPathWithin -Path $LauncherTargetPath -Root $tempRoot) -or
        -not (Test-LhmPathWithin -Path $PublicShimPath -Root $tempRoot) -or
        [string]::IsNullOrWhiteSpace($TestExternalStateRoot) -or
        -not (Test-LhmPathWithin -Path $TestExternalStateRoot -Root $tempRoot)) {
        throw 'Non-live relocation paths must remain beneath the OS temporary directory.'
    }
    $TestExternalStateRoot = Resolve-LhmFullPath -Path $TestExternalStateRoot
}
else {
    if (-not (Test-LhmPathEqual `
            -Left $SourceDataRoot `
            -Right $script:LhmPreviousProductionDataRoot) -or
        -not (Test-LhmPathEqual `
            -Left $LauncherTargetPath `
            -Right $script:LhmLauncherTargetPath) -or
        -not (Test-LhmPathEqual `
            -Left $PublicShimPath `
            -Right $script:LhmPublicShimPath)) {
        throw 'Production source-data, launcher, and public-shim paths are fixed.'
    }
    if ($TestFailurePoint -cne 'None' -or
        -not [string]::IsNullOrWhiteSpace($TestExternalStateRoot)) {
        throw 'Failure injection and test external state are available only in NonLiveTestMode.'
    }
}

if ((Test-LhmPathEqual -Left $SourceDataRoot -Right $DataRoot) -or
    (Test-LhmPathEqual -Left $SourceDataRoot -Right $InstallRoot) -or
    (Test-LhmPathEqual -Left $DataRoot -Right $InstallRoot)) {
    throw 'Install, source-data, and target-data roots must be distinct.'
}
$null = Assert-LhmNearestExistingPathAncestry `
    -Path $SourceDataRoot `
    -Label 'Previous LibreHW data root'
if (Test-Path -LiteralPath $SourceDataRoot) {
    throw "Previous source data root must be absent after the completed move: '$SourceDataRoot'."
}
$InstallRoot = Assert-LhmNormalDirectoryTree `
    -Path $InstallRoot `
    -Label 'Installed runtime root'
$DataRoot = Assert-LhmNormalDirectoryTree `
    -Path $DataRoot `
    -Label 'Relocated LibreHW data root'
$LauncherTargetPath = Assert-LhmNormalFile `
    -Path $LauncherTargetPath `
    -Label 'Deployed LibreHW launcher'
$PublicShimPath = Assert-LhmNormalFile `
    -Path $PublicShimPath `
    -Label 'Public librehw shim'
if ($mode.IsTest) {
    $TestExternalStateRoot = Assert-LhmNormalDirectoryTree `
        -Path $TestExternalStateRoot `
        -Label 'Non-live external-state root'
}
$null = Assert-LhmNormalFile `
    -Path (Join-Path $DataRoot $script:LhmSettingsFileName) `
    -Label 'Relocated LibreHW settings'
$null = Assert-LhmNormalDirectoryTree `
    -Path (Join-Path $DataRoot 'logs') `
    -Label 'Relocated LibreHW logs root'
Assert-LhmInstalledRootInventory -InstallRoot $InstallRoot
$installed = Get-LhmInstalledPayloadState -InstallRoot $InstallRoot
if ($null -eq $installed) {
    throw "No installed Libre Hardware Monitor release exists at '$InstallRoot'."
}
$expectedExecutablePath = Join-Path $InstallRoot $script:LhmExecutableName
$runtimeConfigPath = Join-Path $InstallRoot $script:LhmRuntimeConfigName

$publicShimHash = Get-LhmFileSha256 -Path $PublicShimPath
if (-not $mode.IsTest -and $publicShimHash -cne $script:LhmPublicShimSha256) {
    throw 'Public librehw.cmd does not match the accepted unchanged shim hash.'
}
$publicShimText = Get-Content -LiteralPath $PublicShimPath -Raw
$expectedShimDelegation = "-File `"$LauncherTargetPath`""
if ($publicShimText -notmatch [regex]::Escape($expectedShimDelegation)) {
    throw 'Public librehw.cmd does not delegate to the exact compatibility launcher path.'
}

$canonicalLauncher = Join-Path $PSScriptRoot 'Start-LibreHardwareMonitor.ps1'
$canonicalLauncher = Assert-LhmNormalFile `
    -Path $canonicalLauncher `
    -Label 'Canonical LibreHW launcher'
$canonicalLauncherHash = Get-LhmFileSha256 -Path $canonicalLauncher
$launcherTokens = $null
$launcherErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    $canonicalLauncher,
    [ref]$launcherTokens,
    [ref]$launcherErrors)
if ($launcherErrors.Count -gt 0) {
    throw "Canonical launcher does not parse: $($launcherErrors[0].Message)"
}
$currentLauncherHash = Get-LhmFileSha256 -Path $LauncherTargetPath

$currentUsesSourceDataRoot = $false
$currentUsesTargetDataRoot = $false
try {
    $null = Read-LhmRuntimeConfig `
        -Path $runtimeConfigPath `
        -ExpectedDataRoot $SourceDataRoot `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath
    $currentUsesSourceDataRoot = $true
}
catch {
    try {
        $null = Read-LhmRuntimeConfig `
            -Path $runtimeConfigPath `
            -ExpectedDataRoot $DataRoot `
            -ExpectedManagedTaskPath $ManagedStartupTaskPath
        $currentUsesTargetDataRoot = $true
    }
    catch {
        throw 'Installed runtime config names neither the reviewed previous nor relocated data root.'
    }
}

if (-not $mode.IsTest) {
    if ($currentUsesSourceDataRoot -and
        $currentLauncherHash -cne $script:LhmPreRelocationLauncherSha256) {
        throw 'Deployed launcher does not match trusted pre-relocation content.'
    }
    if ($currentUsesTargetDataRoot -and
        $currentLauncherHash -cne $script:LhmPreRelocationLauncherSha256 -and
        $currentLauncherHash -cne $canonicalLauncherHash) {
        throw 'Deployed launcher is neither trusted pre-relocation nor canonical content.'
    }
}

$recoveryParent = Join-Path $DataRoot 'release-recovery'
$preStableRecoveryRoot = Join-Path $recoveryParent 'pre-stable-startup'
$relocationRecoveryRoot = Join-Path $recoveryParent 'data-root-relocation'
$recoveryParent = Assert-LhmNormalDirectoryTree `
    -Path $recoveryParent `
    -Label 'LibreHW recovery parent'
$preStableRecovery = Read-LhmPreStableRecoveryPacket `
    -RecoveryRoot $preStableRecoveryRoot `
    -ExpectedLauncherTargetPath $LauncherTargetPath `
    -ExpectedManagedTaskPath $ManagedStartupTaskPath `
    -ExpectedPublicShimPath $PublicShimPath `
    -ExpectedPublicShimSha256 $publicShimHash `
    -ExpectedLauncherBackupSha256 $null `
    -ExpectedManagedTaskBackupSha256 $null `
    -NonLiveTestMode:$mode.IsTest `
    -TestExternalStateRoot $TestExternalStateRoot
$preStableManifestHash = Get-LhmFileSha256 -Path $preStableRecovery.ManifestPath

$task = Get-LhmRelocationTaskState `
    -ManagedTaskPath $ManagedStartupTaskPath `
    -IsTest:$mode.IsTest `
    -ExternalStateRoot $TestExternalStateRoot
$taskState = Assert-LhmRelocationTaskContract `
    -Task $task `
    -ManagedTaskPath $ManagedStartupTaskPath `
    -ExpectedExecutablePath $expectedExecutablePath `
    -ExpectedInstallRoot $InstallRoot `
    -IsTest:$mode.IsTest `
    -RequireDisabled:$currentUsesSourceDataRoot

if (-not $mode.IsTest) {
    $processEntries = @(Get-LhmProcesses -ExpectedExecutablePath $expectedExecutablePath)
    $unsafeProcesses = @($processEntries | Where-Object {
        -not $_.Path -or -not $_.IsExact
    })
    if ($unsafeProcesses.Count -gt 0 -or $processEntries.Count -gt 1) {
        throw 'Libre Hardware Monitor process state is not exact and singular.'
    }
    if ($currentUsesSourceDataRoot -and $processEntries.Count -ne 0) {
        throw 'Libre Hardware Monitor must be stopped before the data-root relocation.'
    }
}

$recovery = $null
if (Test-Path -LiteralPath $relocationRecoveryRoot -PathType Container) {
    $recovery = Read-LhmDataRootRelocationRecovery `
        -RecoveryRoot $relocationRecoveryRoot `
        -ExpectedInstallRoot $InstallRoot `
        -ExpectedSourceDataRoot $SourceDataRoot `
        -ExpectedDataRoot $DataRoot `
        -ExpectedRuntimeConfigPath $runtimeConfigPath `
        -ExpectedLauncherTargetPath $LauncherTargetPath `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath `
        -ExpectedExecutablePath $expectedExecutablePath `
        -ExpectedPublicShimPath $PublicShimPath `
        -ExpectedPublicShimSha256 $publicShimHash `
        -IsTest:$mode.IsTest
}
elseif ($currentUsesTargetDataRoot) {
    throw 'Runtime config already names the relocated data root, but bounded relocation recovery is absent.'
}

$alreadyConverged =
    $currentUsesTargetDataRoot -and
    $currentLauncherHash -ceq $canonicalLauncherHash -and
    [bool]$taskState.Enabled -and
    $null -ne $recovery
$action =
    "Persist bounded config/launcher/task recovery, select '$DataRoot', and activate the existing managed task"
if (-not $PSCmdlet.ShouldProcess($InstallRoot, $action)) {
    return
}

$preparingRecovery = $null
$runtimeStage = $null
$launcherStage = $null
$activationStarted = $false
try {
    if ($null -eq $recovery) {
        $preparingRecovery =
            Join-Path $recoveryParent ".data-root-relocation-$([guid]::NewGuid().ToString('N'))"
        $null = New-LhmSafeRecoveryPreparationDirectory `
            -DataRoot $DataRoot `
            -RecoveryParent $recoveryParent `
            -PreparationPath $preparingRecovery `
            -RequiredLeafPrefix '.data-root-relocation-'
        Copy-Item `
            -LiteralPath $runtimeConfigPath `
            -Destination (Join-Path $preparingRecovery 'runtime-config-backup.json')
        Copy-Item `
            -LiteralPath $LauncherTargetPath `
            -Destination (Join-Path $preparingRecovery 'launcher-backup.ps1')
        $taskBackupName = if ($mode.IsTest) {
            'managed-task.test.json'
        }
        else {
            'managed-task.xml'
        }
        $taskBackupPath = Join-Path $preparingRecovery $taskBackupName
        if ($mode.IsTest) {
            Copy-Item `
                -LiteralPath (Join-Path $TestExternalStateRoot 'managed-task.json') `
                -Destination $taskBackupPath
        }
        else {
            $taskParts = Split-LhmManagedTaskPath `
                -ManagedStartupTaskPath $ManagedStartupTaskPath
            Export-ScheduledTask `
                -TaskPath $taskParts.TaskPath `
                -TaskName $taskParts.TaskName |
                Set-Content -LiteralPath $taskBackupPath -Encoding Unicode
        }
        [ordered]@{
            schema = 'sq.librehw.data-root-relocation-recovery.v1'
            createdAt = [DateTimeOffset]::UtcNow.ToString('o')
            installRoot = $InstallRoot
            sourceDataRoot = $SourceDataRoot
            dataRoot = $DataRoot
            runtimeConfigPath = $runtimeConfigPath
            runtimeConfigBackup = 'runtime-config-backup.json'
            runtimeConfigBackupSha256 =
                Get-LhmFileSha256 -Path (Join-Path $preparingRecovery 'runtime-config-backup.json')
            launcherTargetPath = $LauncherTargetPath
            launcherBackup = 'launcher-backup.ps1'
            launcherBackupSha256 =
                Get-LhmFileSha256 -Path (Join-Path $preparingRecovery 'launcher-backup.ps1')
            managedTaskPath = $ManagedStartupTaskPath
            managedTaskBackup = $taskBackupName
            managedTaskBackupSha256 = Get-LhmFileSha256 -Path $taskBackupPath
            publicShimPath = $PublicShimPath
            publicShimSha256 = $publicShimHash
        } | ConvertTo-Json -Depth 5 | Set-Content `
            -LiteralPath (Join-Path $preparingRecovery 'recovery.json') `
            -Encoding UTF8
        $null = Read-LhmDataRootRelocationRecovery `
            -RecoveryRoot $preparingRecovery `
            -ExpectedInstallRoot $InstallRoot `
            -ExpectedSourceDataRoot $SourceDataRoot `
            -ExpectedDataRoot $DataRoot `
            -ExpectedRuntimeConfigPath $runtimeConfigPath `
            -ExpectedLauncherTargetPath $LauncherTargetPath `
            -ExpectedManagedTaskPath $ManagedStartupTaskPath `
            -ExpectedExecutablePath $expectedExecutablePath `
            -ExpectedPublicShimPath $PublicShimPath `
            -ExpectedPublicShimSha256 $publicShimHash `
            -ExpectedLauncherBackupSha256 $currentLauncherHash `
            -IsTest:$mode.IsTest
        Move-Item -LiteralPath $preparingRecovery -Destination $relocationRecoveryRoot
        $preparingRecovery = $null
        $recovery = Read-LhmDataRootRelocationRecovery `
            -RecoveryRoot $relocationRecoveryRoot `
            -ExpectedInstallRoot $InstallRoot `
            -ExpectedSourceDataRoot $SourceDataRoot `
            -ExpectedDataRoot $DataRoot `
            -ExpectedRuntimeConfigPath $runtimeConfigPath `
            -ExpectedLauncherTargetPath $LauncherTargetPath `
            -ExpectedManagedTaskPath $ManagedStartupTaskPath `
            -ExpectedExecutablePath $expectedExecutablePath `
            -ExpectedPublicShimPath $PublicShimPath `
            -ExpectedPublicShimSha256 $publicShimHash `
            -ExpectedLauncherBackupSha256 $currentLauncherHash `
            -IsTest:$mode.IsTest
    }
    Invoke-TestFailurePoint -Point 'AfterRecovery'

    if (-not $currentUsesTargetDataRoot) {
        $runtimeStage = Join-Path `
            $InstallRoot `
            ".$($script:LhmRuntimeConfigName).$([guid]::NewGuid().ToString('N')).tmp"
        [ordered]@{
            schema = $script:LhmRuntimeSchema
            dataRoot = $DataRoot
            managedStartupTaskPath = $ManagedStartupTaskPath
        } | ConvertTo-Json | Set-Content -LiteralPath $runtimeStage -Encoding UTF8
        $null = Read-LhmRuntimeConfig `
            -Path $runtimeStage `
            -ExpectedDataRoot $DataRoot `
            -ExpectedManagedTaskPath $ManagedStartupTaskPath
        Move-Item -LiteralPath $runtimeStage -Destination $runtimeConfigPath -Force
        $runtimeStage = $null
    }
    $null = Read-LhmRuntimeConfig `
        -Path $runtimeConfigPath `
        -ExpectedDataRoot $DataRoot `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath
    Invoke-TestFailurePoint -Point 'AfterRuntimeConfig'

    if ((Get-LhmFileSha256 -Path $LauncherTargetPath) -cne $canonicalLauncherHash) {
        $launcherStage = Join-Path `
            (Split-Path -Parent $LauncherTargetPath) `
            ".Start-LibreHardwareMonitor.$([guid]::NewGuid().ToString('N')).tmp"
        Copy-Item -LiteralPath $canonicalLauncher -Destination $launcherStage
        if ((Get-LhmFileSha256 -Path $launcherStage) -cne $canonicalLauncherHash) {
            throw 'Staged canonical launcher hash does not match source.'
        }
        Move-Item -LiteralPath $launcherStage -Destination $LauncherTargetPath -Force
        $launcherStage = $null
    }
    if ((Get-LhmFileSha256 -Path $LauncherTargetPath) -cne $canonicalLauncherHash) {
        throw 'Deployed launcher did not read back as canonical content.'
    }
    Invoke-TestFailurePoint -Point 'AfterLauncher'

    if ((Get-LhmFileSha256 -Path $PublicShimPath) -cne $publicShimHash) {
        throw 'Public librehw.cmd changed during data-root relocation.'
    }
    if ((Get-LhmFileSha256 -Path $preStableRecovery.ManifestPath) -cne
        $preStableManifestHash) {
        throw 'Pre-stable recovery changed during data-root relocation.'
    }
    $null = Read-LhmDataRootRelocationRecovery `
        -RecoveryRoot $relocationRecoveryRoot `
        -ExpectedInstallRoot $InstallRoot `
        -ExpectedSourceDataRoot $SourceDataRoot `
        -ExpectedDataRoot $DataRoot `
        -ExpectedRuntimeConfigPath $runtimeConfigPath `
        -ExpectedLauncherTargetPath $LauncherTargetPath `
        -ExpectedManagedTaskPath $ManagedStartupTaskPath `
        -ExpectedExecutablePath $expectedExecutablePath `
        -ExpectedPublicShimPath $PublicShimPath `
        -ExpectedPublicShimSha256 $publicShimHash `
        -IsTest:$mode.IsTest

    $task = Get-LhmRelocationTaskState `
        -ManagedTaskPath $ManagedStartupTaskPath `
        -IsTest:$mode.IsTest `
        -ExternalStateRoot $TestExternalStateRoot
    $taskState = Assert-LhmRelocationTaskContract `
        -Task $task `
        -ManagedTaskPath $ManagedStartupTaskPath `
        -ExpectedExecutablePath $expectedExecutablePath `
        -ExpectedInstallRoot $InstallRoot `
        -IsTest:$mode.IsTest
    Invoke-TestFailurePoint -Point 'AfterTaskReadback'

    if (-not [bool]$taskState.Enabled) {
        if ($mode.IsTest) {
            Set-LhmRelocationTestTaskEnabled `
                -ExternalStateRoot $TestExternalStateRoot `
                -ManagedTaskPath $ManagedStartupTaskPath `
                -ExpectedExecutablePath $expectedExecutablePath `
                -ExpectedInstallRoot $InstallRoot `
                -Enabled
        }
        else {
            $taskParts = Split-LhmManagedTaskPath `
                -ManagedStartupTaskPath $ManagedStartupTaskPath
            Enable-ScheduledTask `
                -TaskPath $taskParts.TaskPath `
                -TaskName $taskParts.TaskName | Out-Null
        }
    }
    $task = Get-LhmRelocationTaskState `
        -ManagedTaskPath $ManagedStartupTaskPath `
        -IsTest:$mode.IsTest `
        -ExternalStateRoot $TestExternalStateRoot
    $null = Assert-LhmRelocationTaskContract `
        -Task $task `
        -ManagedTaskPath $ManagedStartupTaskPath `
        -ExpectedExecutablePath $expectedExecutablePath `
        -ExpectedInstallRoot $InstallRoot `
        -IsTest:$mode.IsTest `
        -RequireEnabled
    Invoke-TestFailurePoint -Point 'AfterTaskEnabled'

    $processId = $null
    if (-not $mode.IsTest) {
        $processEntries = @(Get-LhmProcesses -ExpectedExecutablePath $expectedExecutablePath)
        if ($processEntries.Count -eq 0) {
            $activationStarted = $true
            $process = Start-LhmManagedTaskAndWait `
                -ManagedStartupTaskPath $ManagedStartupTaskPath `
                -ExpectedExecutablePath $expectedExecutablePath `
                -TimeoutSeconds $ActivationTimeoutSeconds
            $processId = $process.Id
        }
        elseif ($processEntries.Count -eq 1 -and $processEntries[0].IsExact) {
            $processId = $processEntries[0].Process.Id
        }
        else {
            throw 'Activation did not converge on one exact installed process.'
        }
        $null = Read-LhmRuntimeConfig `
            -Path $runtimeConfigPath `
            -ExpectedDataRoot $DataRoot `
            -ExpectedManagedTaskPath $ManagedStartupTaskPath
        Assert-LhmHttpHealth `
            -HealthUri $HealthUri `
            -TimeoutSeconds $ActivationTimeoutSeconds
        $processEntries = @(Get-LhmProcesses -ExpectedExecutablePath $expectedExecutablePath)
        if ($processEntries.Count -ne 1 -or
            -not $processEntries[0].IsExact -or
            $processEntries[0].Process.Id -ne $processId) {
            throw 'Post-health process readback is not the exact relocated-data-root process.'
        }
    }

    Assert-LhmInstalledRootInventory -InstallRoot $InstallRoot
    if ((Get-LhmFileSha256 -Path $PublicShimPath) -cne $publicShimHash -or
        (Get-LhmFileSha256 -Path $preStableRecovery.ManifestPath) -cne
            $preStableManifestHash) {
        throw 'Relocation changed a preserved shim or pre-stable recovery boundary.'
    }

    [pscustomobject]@{
        Result = 'PASS'
        InstallRoot = $InstallRoot
        DataRoot = $DataRoot
        RuntimeConfig = $runtimeConfigPath
        Launcher = $LauncherTargetPath
        ManagedTask = $ManagedStartupTaskPath
        RecoveryDirectory = $relocationRecoveryRoot
        PublicShimSha256 = $publicShimHash
        ProcessId = $processId
        AlreadyConverged = $alreadyConverged
        Activated = -not $mode.IsTest
        TestMode = $mode.IsTest
    }
}
catch {
    $failure = $_
    $safetyFailure = $null
    if (Test-Path -LiteralPath $relocationRecoveryRoot -PathType Container) {
        try {
            if (-not $mode.IsTest -and $activationStarted) {
                Stop-LhmExactProcessForRelease `
                    -ExpectedExecutablePath $expectedExecutablePath `
                    -AllowStopExactProcess
            }
            if ($mode.IsTest) {
                Set-LhmRelocationTestTaskEnabled `
                    -ExternalStateRoot $TestExternalStateRoot `
                    -ManagedTaskPath $ManagedStartupTaskPath `
                    -ExpectedExecutablePath $expectedExecutablePath `
                    -ExpectedInstallRoot $InstallRoot
            }
            else {
                $taskParts = Split-LhmManagedTaskPath `
                    -ManagedStartupTaskPath $ManagedStartupTaskPath
                Disable-ScheduledTask `
                    -TaskPath $taskParts.TaskPath `
                    -TaskName $taskParts.TaskName | Out-Null
            }
            $task = Get-LhmRelocationTaskState `
                -ManagedTaskPath $ManagedStartupTaskPath `
                -IsTest:$mode.IsTest `
                -ExternalStateRoot $TestExternalStateRoot
            $null = Assert-LhmRelocationTaskContract `
                -Task $task `
                -ManagedTaskPath $ManagedStartupTaskPath `
                -ExpectedExecutablePath $expectedExecutablePath `
                -ExpectedInstallRoot $InstallRoot `
                -IsTest:$mode.IsTest `
                -RequireDisabled
            if ((Get-LhmFileSha256 -Path $PublicShimPath) -cne $publicShimHash -or
                (Get-LhmFileSha256 -Path $preStableRecovery.ManifestPath) -cne
                    $preStableManifestHash) {
                throw 'Failure containment found a changed shim or pre-stable recovery packet.'
            }
        }
        catch {
            $safetyFailure = $_
        }
    }
    if ($null -ne $safetyFailure) {
        throw "Data-root relocation failed and safe task disablement did not complete. Original failure: $($failure.Exception.Message). Safety failure: $($safetyFailure.Exception.Message)"
    }
    throw $failure
}
finally {
    foreach ($stagePath in @($runtimeStage, $launcherStage)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$stagePath) -and
            (Test-Path -LiteralPath $stagePath)) {
            Remove-Item -LiteralPath $stagePath -Force
        }
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$preparingRecovery) -and
        (Test-Path -LiteralPath $preparingRecovery -PathType Container)) {
        Remove-LhmOwnedDirectory `
            -Path $preparingRecovery `
            -ExpectedParent $recoveryParent `
            -RequiredLeafPrefix '.data-root-relocation-'
    }
}

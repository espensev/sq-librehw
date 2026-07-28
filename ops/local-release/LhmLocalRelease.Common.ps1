Set-StrictMode -Version Latest

$script:LhmExecutableName = 'LibreHardwareMonitor.Windows.Forms.exe'
$script:LhmReleaseManifestName = 'release.json'
$script:LhmRuntimeConfigName = 'librehw.runtime.json'
$script:LhmSettingsFileName = 'LibreHardwareMonitor.Windows.Forms.config'
$script:LhmReleaseSchema = 'sq.librehw.local-release.v1'
$script:LhmRuntimeSchema = 'sq.librehw.runtime.v1'
$script:LhmProductionInstallRoot = 'E:\SQ_HQ\Monitoring\LibreHW'
$script:LhmProductionDataRoot = 'E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor'
$script:LhmManagedTaskPath = '\SevGrp\AdminTask\LibreHW-No-UAC'
$script:LhmExpectedMachineId = 'snd-desk'
$script:LhmIdentityVerifierPath =
    'C:\Users\Sev\OneDrive\common\common_dev\Get-VerifiedMachineIdentity.ps1'
$script:LhmProcessName = 'LibreHardwareMonitor.Windows.Forms'
$script:LhmLauncherTargetPath =
    'E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1'
$script:LhmPublicShimPath = 'E:\SQ_HQ\u-programs\bin\librehw.cmd'
$script:LhmPublicShimSha256 =
    'fe319aabd007a3a639cd618c748d480f7881dd18864db6f9c94bac537bd10d73'
$script:LhmPreStableLauncherSha256 =
    '79a45f697d40d7f3d9897228d5b6f35ad04974f3a42e4a69eb0f24a29e2c6432'
$script:LhmPreStableManagedExecutablePath =
    'E:\SQ_HQ\Monitoring\sq-librehwdev\sq-librehw\bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe'
$script:LhmLegacyShortcutSha256 = @(
    '7f4b2ed9a4841d38f1753cfae8645ab74b2dbb7d42b0c8a4abdffa08b1722fe3',
    '6987e1a9d2992c3519eed9c7d37fd3f278b2ffbd91ae42d7060f3dc21ba86d6f'
)
$script:LhmLegacyShortcutTargetPath = @(
    'E:\SQ_HQ\Monitoring\sq-librehw\bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe',
    'C:\Windows\System32\schtasks.exe'
)
$script:LhmLegacyShortcutArguments = @(
    '',
    '/run /tn "\SevGrp\AdminTask\LibreHW-No-UAC"'
)
$script:LhmLegacyShortcutWorkingDirectory =
    'E:\SQ_HQ\Monitoring\sq-librehw\bin\Release\net10.0-windows'
$script:LhmTransactionSchema = 'sq.librehw.release-transaction.v1'

function Resolve-LhmFullPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Path
    )

    $providerPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    return [System.IO.Path]::GetFullPath($providerPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-LhmPathEqual {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Left,

        [Parameter(Mandatory)]
        [string] $Right
    )

    return [string]::Equals(
        (Resolve-LhmFullPath -Path $Left),
        (Resolve-LhmFullPath -Path $Right),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-LhmPathWithin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Root
    )

    $fullPath = Resolve-LhmFullPath -Path $Path
    $fullRoot = Resolve-LhmFullPath -Path $Root
    $prefix = $fullRoot + [System.IO.Path]::DirectorySeparatorChar

    return $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-LhmNoReparsePathAncestry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $currentPath = Resolve-LhmFullPath -Path $Path
    while (-not [string]::IsNullOrWhiteSpace($currentPath)) {
        if (-not (Test-Path -LiteralPath $currentPath)) {
            throw "$Label path ancestry contains a missing component: '$currentPath'."
        }
        $item = Get-Item -LiteralPath $currentPath -Force -ErrorAction Stop
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            throw "$Label path ancestry contains a reparse point: '$currentPath'."
        }

        $parentPath = Split-Path -Parent $currentPath
        if ([string]::IsNullOrWhiteSpace($parentPath) -or
            [string]::Equals(
                $parentPath,
                $currentPath,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $currentPath = $parentPath
    }
}

function Assert-LhmNearestExistingPathAncestry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $fullPath = Resolve-LhmFullPath -Path $Path
    $existingPath = $fullPath
    while (-not (Test-Path -LiteralPath $existingPath)) {
        $parentPath = Split-Path -Parent $existingPath
        if ([string]::IsNullOrWhiteSpace($parentPath) -or
            [string]::Equals(
                $parentPath,
                $existingPath,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$Label has no verifiable existing ancestor: '$fullPath'."
        }
        $existingPath = $parentPath
    }

    $existingInfo = Get-Item -LiteralPath $existingPath -Force -ErrorAction Stop
    if (-not $existingInfo.PSIsContainer) {
        throw "$Label nearest existing path is not a directory: '$existingPath'."
    }
    Assert-LhmNoReparsePathAncestry -Path $existingPath -Label $Label
    return Resolve-LhmFullPath -Path $existingPath
}

function Assert-LhmSafeFileDestination {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $resolvedPath = Resolve-LhmFullPath -Path $Path
    $parentPath = Split-Path -Parent $resolvedPath
    $null = Assert-LhmNearestExistingPathAncestry `
        -Path $parentPath `
        -Label "$Label parent"

    if (Test-Path -LiteralPath $parentPath) {
        if (-not (Test-Path -LiteralPath $parentPath -PathType Container)) {
            throw "$Label parent is not a directory: '$parentPath'."
        }
        Assert-LhmNoReparsePathAncestry -Path $parentPath -Label "$Label parent"

        $leafName = Split-Path -Leaf $resolvedPath
        $entries = @(Get-ChildItem -LiteralPath $parentPath -Force -ErrorAction Stop |
            Where-Object {
                [string]::Equals(
                    $_.Name,
                    $leafName,
                    [System.StringComparison]::OrdinalIgnoreCase)
            })
        if ($entries.Count -gt 1) {
            throw "$Label has ambiguous destination entries: '$resolvedPath'."
        }
        if ($entries.Count -eq 1) {
            if ($entries[0].Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "$Label destination is a reparse point: '$resolvedPath'."
            }
            if ($entries[0].PSIsContainer) {
                throw "$Label destination is a directory: '$resolvedPath'."
            }
        }
    }

    return $resolvedPath
}

function Assert-LhmNormalDirectoryTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $resolvedPath = Resolve-LhmFullPath -Path $Path
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        throw "$Label is not a directory: '$resolvedPath'."
    }

    Assert-LhmNoReparsePathAncestry -Path $resolvedPath -Label $Label
    $pending = [System.Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($resolvedPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "$Label contains a reparse point: '$($entry.FullName)'."
            }
            if ($entry.PSIsContainer) {
                $pending.Enqueue($entry.FullName)
            }
        }
    }

    return $resolvedPath
}

function Assert-LhmNormalFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $resolvedPath = Resolve-LhmFullPath -Path $Path
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$Label is not a file: '$resolvedPath'."
    }
    Assert-LhmNoReparsePathAncestry -Path $resolvedPath -Label $Label
    return $resolvedPath
}

function Test-LhmOwnedDirectoryLeafName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $LeafName,

        [Parameter(Mandatory)]
        [string] $RequiredLeafPrefix
    )

    if ([string]::Equals(
        $RequiredLeafPrefix,
        '.release-transaction',
        [System.StringComparison]::Ordinal)) {
        return [string]::Equals(
            $LeafName,
            $RequiredLeafPrefix,
            [System.StringComparison]::Ordinal)
    }

    if (-not $LeafName.StartsWith(
        $RequiredLeafPrefix,
        [System.StringComparison]::Ordinal)) {
        return $false
    }

    $suffix = $LeafName.Substring($RequiredLeafPrefix.Length)
    return $suffix -cmatch '^[0-9a-f]{32}$'
}

function New-LhmSafeRecoveryPreparationDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $DataRoot,

        [Parameter(Mandatory)]
        [string] $RecoveryParent,

        [Parameter(Mandatory)]
        [string] $PreparationPath,

        [Parameter(Mandatory)]
        [string] $RequiredLeafPrefix
    )

    $expectedRecoveryParent = Join-Path $DataRoot 'release-recovery'
    if (-not (Test-LhmPathEqual -Left $RecoveryParent -Right $expectedRecoveryParent) -or
        -not (Test-LhmPathEqual `
            -Left (Split-Path -Parent $PreparationPath) `
            -Right $RecoveryParent) -or
        -not (Test-LhmOwnedDirectoryLeafName `
            -LeafName (Split-Path -Leaf $PreparationPath) `
            -RequiredLeafPrefix $RequiredLeafPrefix)) {
        throw 'Recovery preparation paths are outside the fixed bounded layout.'
    }

    Assert-LhmNoReparsePathAncestry -Path $DataRoot -Label 'LibreHW data root'
    if (Test-Path -LiteralPath $RecoveryParent) {
        $recoveryParentInfo = Get-Item -LiteralPath $RecoveryParent -Force
        if (-not $recoveryParentInfo.PSIsContainer) {
            throw "Recovery parent is not a directory: '$RecoveryParent'."
        }
        Assert-LhmNoReparsePathAncestry `
            -Path $RecoveryParent `
            -Label 'LibreHW recovery parent'
    }
    else {
        [System.IO.Directory]::CreateDirectory($RecoveryParent) | Out-Null
        Assert-LhmNoReparsePathAncestry `
            -Path $RecoveryParent `
            -Label 'LibreHW recovery parent'
    }

    if (Test-Path -LiteralPath $PreparationPath) {
        throw "Recovery preparation path already exists: '$PreparationPath'."
    }
    [System.IO.Directory]::CreateDirectory($PreparationPath) | Out-Null
    Assert-LhmNoReparsePathAncestry `
        -Path $PreparationPath `
        -Label 'LibreHW recovery preparation'
    return Resolve-LhmFullPath -Path $PreparationPath
}

function Assert-LhmOperationMode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $InstallRoot,

        [Parameter(Mandatory)]
        [string] $DataRoot,

        [Parameter(Mandatory)]
        [string] $ManagedStartupTaskPath,

        [switch] $NonLiveTestMode
    )

    $resolvedInstallRoot = Resolve-LhmFullPath -Path $InstallRoot
    $resolvedDataRoot = Resolve-LhmFullPath -Path $DataRoot

    if ($ManagedStartupTaskPath -cne $script:LhmManagedTaskPath) {
        throw "The managed task must be '$($script:LhmManagedTaskPath)'."
    }

    if (-not $NonLiveTestMode) {
        if (-not (Test-LhmPathEqual -Left $resolvedInstallRoot -Right $script:LhmProductionInstallRoot)) {
            throw "Production installation is fixed at '$($script:LhmProductionInstallRoot)'."
        }

        if (-not (Test-LhmPathEqual -Left $resolvedDataRoot -Right $script:LhmProductionDataRoot)) {
            throw "Production data is fixed at '$($script:LhmProductionDataRoot)'."
        }

        return [pscustomobject]@{
            InstallRoot = $resolvedInstallRoot
            DataRoot = $resolvedDataRoot
            IsTest = $false
        }
    }

    $systemTempRoot = Resolve-LhmFullPath -Path ([System.IO.Path]::GetTempPath())
    if (-not (Test-LhmPathWithin -Path $resolvedInstallRoot -Root $systemTempRoot) -or
        -not (Test-LhmPathWithin -Path $resolvedDataRoot -Root $systemTempRoot)) {
        throw 'NonLiveTestMode is restricted to install and data roots beneath the OS temporary directory.'
    }

    if (Test-LhmPathEqual -Left $resolvedInstallRoot -Right $resolvedDataRoot) {
        throw 'The test install and data roots must be separate directories.'
    }

    return [pscustomobject]@{
        InstallRoot = $resolvedInstallRoot
        DataRoot = $resolvedDataRoot
        IsTest = $true
    }
}

function Assert-LhmVerifiedMachineIdentity {
    [CmdletBinding()]
    param()

    if (-not (Test-Path -LiteralPath $script:LhmIdentityVerifierPath -PathType Leaf)) {
        throw "Machine identity verifier not found: '$($script:LhmIdentityVerifierPath)'."
    }

    $identity = & $script:LhmIdentityVerifierPath
    if ($null -eq $identity) {
        throw 'Machine identity verifier returned no result.'
    }

    if ([string]$identity.status -cne 'VERIFIED') {
        throw "Machine identity status is '$($identity.status)', not 'VERIFIED'."
    }

    if ([string]$identity.machineId -cne $script:LhmExpectedMachineId) {
        throw "Machine identity is '$($identity.machineId)', not '$($script:LhmExpectedMachineId)'."
    }

    return $identity
}

function Assert-LhmDesktopRuntime {
    [CmdletBinding()]
    param()

    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
    $runtimeLines = @(& $dotnet.Source --list-runtimes)
    if ($LASTEXITCODE -ne 0) {
        throw "'dotnet --list-runtimes' failed with exit code $LASTEXITCODE."
    }

    if (-not ($runtimeLines -match '^Microsoft\.WindowsDesktop\.App 10\.')) {
        throw 'Microsoft.WindowsDesktop.App 10.x is required for this framework-dependent release.'
    }
}

function Get-LhmFileSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-LhmReleasePayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Directory,

        [switch] $RequireCandidateShape
    )

    $resolvedDirectory = Assert-LhmNormalDirectoryTree `
        -Path $Directory `
        -Label 'Release payload directory'

    $executablePath = Join-Path $resolvedDirectory $script:LhmExecutableName
    $manifestPath = Join-Path $resolvedDirectory $script:LhmReleaseManifestName
    $executablePath = Assert-LhmNormalFile `
        -Path $executablePath `
        -Label 'Release executable'
    $manifestPath = Assert-LhmNormalFile `
        -Path $manifestPath `
        -Label 'Release manifest'

    if ($RequireCandidateShape) {
        $entries = @(Get-ChildItem -LiteralPath $resolvedDirectory -Force)
        if ($entries.Count -ne 2 -or
            @($entries | Where-Object { $_.PSIsContainer }).Count -ne 0 -or
            @($entries | Where-Object { $_.Extension -ieq '.exe' }).Count -ne 1) {
            throw 'A release candidate must contain exactly one EXE and release.json.'
        }
    }

    $manifestInfo = Get-Item -LiteralPath $manifestPath
    if ($manifestInfo.Length -gt 65536) {
        throw "Release manifest is unexpectedly large: $($manifestInfo.Length) bytes."
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Release manifest is not valid JSON: $($_.Exception.Message)"
    }

    if ([string]$manifest.schema -cne $script:LhmReleaseSchema) {
        throw "Unsupported release schema '$($manifest.schema)'."
    }

    if ([string]::IsNullOrWhiteSpace([string]$manifest.releaseId)) {
        throw 'Release manifest releaseId is required.'
    }

    if ([string]$manifest.commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Release manifest commit must be a full Git SHA.'
    }

    if ([string]::IsNullOrWhiteSpace([string]$manifest.version)) {
        throw 'Release manifest version is required.'
    }

    if ([string]$manifest.framework -cne 'net10.0-windows') {
        throw "Release framework '$($manifest.framework)' is not supported."
    }

    if ([string]$manifest.runtime -cne 'win-x64') {
        throw "Release runtime '$($manifest.runtime)' is not supported."
    }

    if ([bool]$manifest.selfContained) {
        throw 'The local release must be framework-dependent.'
    }

    $timestamp = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        [string]$manifest.timestamp,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$timestamp)) {
        throw 'Release manifest timestamp is missing or invalid.'
    }

    if ([string]$manifest.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'Release manifest sha256 must be a 64-character hexadecimal value.'
    }

    $actualHash = Get-LhmFileSha256 -Path $executablePath
    if ($actualHash -cne ([string]$manifest.sha256).ToLowerInvariant()) {
        throw "Release executable hash mismatch in '$resolvedDirectory'."
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
    $actualVersion = [string]$versionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($actualVersion)) {
        $actualVersion = [string]$versionInfo.FileVersion
    }
    if ([string]::IsNullOrWhiteSpace($actualVersion) -or
        $actualVersion -cne [string]$manifest.version) {
        throw "Release manifest version '$($manifest.version)' does not match executable version '$actualVersion'."
    }

    $revisionMatch = [regex]::Match(
        $actualVersion,
        '\+(?<revision>[0-9a-fA-F]{7,40})(?:[-.]|$)',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $revisionMatch.Success -or
        -not ([string]$manifest.commit).StartsWith(
            $revisionMatch.Groups['revision'].Value,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release executable version does not contain the manifest commit revision.'
    }

    return [pscustomobject]@{
        Directory = $resolvedDirectory
        ExecutablePath = $executablePath
        ManifestPath = $manifestPath
        Manifest = $manifest
        Sha256 = $actualHash
    }
}

function Get-LhmInstalledPayloadState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $InstallRoot
    )

    $executablePath = Join-Path $InstallRoot $script:LhmExecutableName
    $manifestPath = Join-Path $InstallRoot $script:LhmReleaseManifestName
    $hasExecutable = Test-Path -LiteralPath $executablePath -PathType Leaf
    $hasManifest = Test-Path -LiteralPath $manifestPath -PathType Leaf

    if ($hasExecutable -xor $hasManifest) {
        throw 'The installed release is incomplete: the EXE and release.json must exist as a pair.'
    }

    if (-not $hasExecutable) {
        return $null
    }

    return Read-LhmReleasePayload -Directory $InstallRoot
}

function Assert-LhmInstalledRootInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $InstallRoot,

        [switch] $AllowTransaction
    )

    if (-not (Test-Path -LiteralPath $InstallRoot -PathType Container)) {
        return
    }
    $InstallRoot = Assert-LhmNormalDirectoryTree `
        -Path $InstallRoot `
        -Label 'Installed runtime root'

    $allowedFiles = @(
        $script:LhmExecutableName,
        $script:LhmReleaseManifestName,
        $script:LhmRuntimeConfigName
    )
    $allowedDirectories = @('rollback')
    if ($AllowTransaction) {
        $allowedDirectories += '.release-transaction'
    }
    $allowed = @($allowedFiles) + @($allowedDirectories)
    $entries = @(Get-ChildItem -LiteralPath $InstallRoot -Force)
    $unexpected = @(Get-ChildItem -LiteralPath $InstallRoot -Force | Where-Object {
        $_.Name -cnotin $allowed
    })
    if ($unexpected.Count -gt 0) {
        throw "Installed runtime contains unexpected entries: $($unexpected.Name -join ', ')."
    }
    $unsafe = @($entries | Where-Object {
        ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or
        ($_.Name -cin $allowedFiles -and $_.PSIsContainer) -or
        ($_.Name -cin $allowedDirectories -and -not $_.PSIsContainer)
    })
    if ($unsafe.Count -gt 0) {
        throw "Installed runtime contains unsafe entries: $($unsafe.Name -join ', ')."
    }

    $rollbackRoot = Join-Path $InstallRoot 'rollback'
    if (Test-Path -LiteralPath $rollbackRoot -PathType Container) {
        $entries = @(Get-ChildItem -LiteralPath $rollbackRoot -Force)
        if ($entries.Count -gt 0) {
            $null = Read-LhmReleasePayload -Directory $rollbackRoot -RequireCandidateShape
        }
    }
}

function Read-LhmRuntimeConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $ExpectedDataRoot,

        [Parameter(Mandatory)]
        [string] $ExpectedManagedTaskPath
    )

    $Path = Assert-LhmNormalFile -Path $Path -Label 'Runtime config'
    $runtimeConfigInfo = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($runtimeConfigInfo.Length -gt 65536) {
        throw "Runtime config is unexpectedly large: $($runtimeConfigInfo.Length) bytes."
    }

    try {
        $runtimeConfig = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Runtime config is not valid JSON: $($_.Exception.Message)"
    }

    if ([string]$runtimeConfig.schema -cne $script:LhmRuntimeSchema) {
        throw "Unsupported runtime config schema '$($runtimeConfig.schema)'."
    }

    if ([string]$runtimeConfig.dataRoot -notmatch '^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+(?:\\|$))') {
        throw 'Runtime config dataRoot must be an absolute path.'
    }

    if (-not (Test-LhmPathEqual -Left ([string]$runtimeConfig.dataRoot) -Right $ExpectedDataRoot)) {
        throw "Runtime config dataRoot does not match '$ExpectedDataRoot'."
    }

    if ([string]$runtimeConfig.managedStartupTaskPath -cne $ExpectedManagedTaskPath) {
        throw "Runtime config managedStartupTaskPath does not match '$ExpectedManagedTaskPath'."
    }

    $actualProperties = @($runtimeConfig.PSObject.Properties.Name | Sort-Object)
    $expectedProperties = @(@('dataRoot', 'managedStartupTaskPath', 'schema') | Sort-Object)
    if (($actualProperties -join "`n") -cne ($expectedProperties -join "`n")) {
        throw 'Runtime config contains unsupported or missing fields.'
    }

    return $runtimeConfig
}

function Read-LhmSafeTaskXml {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = $null
    try {
        $reader = [System.Xml.XmlReader]::Create($Path, $settings)
        $document = [System.Xml.XmlDocument]::new()
        $document.PreserveWhitespace = $true
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    catch {
        throw "$Label is invalid or unsafe XML: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }
}

function Assert-LhmPreStableManagedTaskBackupContract {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $taskXml = Read-LhmSafeTaskXml -Path $Path -Label 'Pre-stable managed-task backup'
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
    $executionNode =
        $taskXml.SelectSingleNode('/t:Task/t:Settings/t:ExecutionTimeLimit', $namespace)
    $disallowBatteryNode =
        $taskXml.SelectSingleNode('/t:Task/t:Settings/t:DisallowStartIfOnBatteries', $namespace)
    $stopBatteryNode =
        $taskXml.SelectSingleNode('/t:Task/t:Settings/t:StopIfGoingOnBatteries', $namespace)
    $expectedWorkingDirectory =
        Split-Path -Parent $script:LhmPreStableManagedExecutablePath
    $expectedSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value

    if ($actionNodes.Count -ne 1 -or
        $actionNodes[0].LocalName -cne 'Exec' -or
        $triggerNodes.Count -ne 0 -or
        $principalNodes.Count -ne 1 -or
        $null -eq $commandNode -or
        -not (Test-LhmPathEqual `
            -Left $commandNode.InnerText `
            -Right $script:LhmPreStableManagedExecutablePath) -or
        ($null -ne $argumentsNode -and
            -not [string]::IsNullOrWhiteSpace($argumentsNode.InnerText)) -or
        $null -eq $workingNode -or
        -not (Test-LhmPathEqual `
            -Left $workingNode.InnerText `
            -Right $expectedWorkingDirectory) -or
        $null -eq $userNode -or
        $userNode.InnerText -cne $expectedSid -or
        $null -eq $logonNode -or
        $logonNode.InnerText -cne 'InteractiveToken' -or
        $null -eq $runLevelNode -or
        $runLevelNode.InnerText -cne 'HighestAvailable' -or
        $null -eq $uriNode -or
        $uriNode.InnerText -cne $script:LhmManagedTaskPath -or
        $null -eq $multipleNode -or
        $multipleNode.InnerText -cne 'IgnoreNew' -or
        $null -eq $startAvailableNode -or
        $startAvailableNode.InnerText -cne 'true' -or
        $null -eq $hardTerminateNode -or
        $hardTerminateNode.InnerText -cne 'false' -or
        $null -eq $executionNode -or
        $executionNode.InnerText -cne 'PT0S' -or
        $null -eq $disallowBatteryNode -or
        $disallowBatteryNode.InnerText -cne 'false' -or
        $null -eq $stopBatteryNode -or
        $stopBatteryNode.InnerText -cne 'false') {
        throw 'Pre-stable managed-task backup does not match the exact discovered task contract.'
    }
}

function Read-LhmPreStableRecoveryPacket {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RecoveryRoot,

        [Parameter(Mandatory)]
        [string] $ExpectedLauncherTargetPath,

        [Parameter(Mandatory)]
        [string] $ExpectedManagedTaskPath,

        [Parameter(Mandatory)]
        [string] $ExpectedPublicShimPath,

        [Parameter(Mandatory)]
        [string] $ExpectedPublicShimSha256,

        [AllowNull()]
        [string] $ExpectedLauncherBackupSha256,

        [AllowNull()]
        [string] $ExpectedManagedTaskBackupSha256,

        [switch] $NonLiveTestMode,

        [AllowNull()]
        [string] $TestExternalStateRoot,

        [switch] $RequireExternalStateMatch
    )

    Assert-LhmNoReparsePathAncestry `
        -Path $RecoveryRoot `
        -Label 'Pre-stable recovery'
    $manifestPath = Join-Path $RecoveryRoot 'recovery.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        (Get-Item -LiteralPath $manifestPath).Length -gt 65536) {
        throw "Pre-stable startup recovery manifest is missing or oversized: '$manifestPath'."
    }

    try {
        $manifest =
            Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "Pre-stable startup recovery manifest is invalid JSON: $($_.Exception.Message)"
    }

    $expectedProperties = @(@(
        'createdAt',
        'launcherBackup',
        'launcherExisted',
        'launcherSha256',
        'launcherTargetPath',
        'managedTaskBackup',
        'managedTaskExisted',
        'managedTaskPath',
        'managedTaskSha256',
        'publicShimPath',
        'publicShimSha256',
        'schema'
    ) | Sort-Object)
    $actualProperties = @($manifest.PSObject.Properties.Name | Sort-Object)
    if (($expectedProperties -join "`n") -cne ($actualProperties -join "`n")) {
        throw 'Pre-stable startup recovery manifest contains unsupported or missing fields.'
    }

    if ([string]$manifest.schema -cne 'sq.librehw.pre-stable-startup-recovery.v1' -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.launcherTargetPath) `
            -Right $ExpectedLauncherTargetPath) -or
        [string]$manifest.managedTaskPath -cne $ExpectedManagedTaskPath -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.publicShimPath) `
            -Right $ExpectedPublicShimPath) -or
        [string]$manifest.publicShimSha256 -cne $ExpectedPublicShimSha256) {
        throw 'Pre-stable startup recovery manifest is incompatible with this release operation.'
    }
    foreach ($propertyName in @('launcherExisted', 'managedTaskExisted')) {
        if ($manifest.$propertyName -isnot [bool]) {
            throw "Pre-stable startup recovery '$propertyName' must be a JSON Boolean."
        }
    }
    $createdAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        [string]$manifest.createdAt,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$createdAt)) {
        throw 'Pre-stable startup recovery createdAt is invalid.'
    }

    $allowedNames = @('recovery.json')
    if ([bool]$manifest.launcherExisted) {
        $allowedNames += 'launcher-backup.ps1'
    }
    if ([bool]$manifest.managedTaskExisted) {
        $allowedNames += if ($NonLiveTestMode) { 'managed-task.test.json' } else { 'managed-task.xml' }
    }
    $rootInfo = Get-Item -LiteralPath $RecoveryRoot -Force -ErrorAction Stop
    if (-not $rootInfo.PSIsContainer -or
        ($rootInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw 'Pre-stable recovery root must be a normal directory, not a reparse point.'
    }
    $entries = @(Get-ChildItem -LiteralPath $RecoveryRoot -Force)
    if ($entries.Count -ne $allowedNames.Count -or
        @($entries | Where-Object {
            $_.PSIsContainer -or
            ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or
            $_.Name -cnotin $allowedNames
        }).Count -gt 0) {
        throw 'Pre-stable recovery directory contains unexpected or unsafe entries.'
    }

    $launcherBackupPath = Join-Path $RecoveryRoot 'launcher-backup.ps1'
    if ([bool]$manifest.launcherExisted) {
        $trustedLauncherSha256 = if ($NonLiveTestMode) {
            $ExpectedLauncherBackupSha256
        }
        else {
            $script:LhmPreStableLauncherSha256
        }
        if ([string]$manifest.launcherBackup -cne 'launcher-backup.ps1' -or
            [string]$manifest.launcherSha256 -notmatch '^[0-9a-f]{64}$' -or
            [string]$trustedLauncherSha256 -notmatch '^[0-9a-f]{64}$' -or
            [string]$manifest.launcherSha256 -cne [string]$trustedLauncherSha256 -or
            -not (Test-Path -LiteralPath $launcherBackupPath -PathType Leaf) -or
            (Get-Item -LiteralPath $launcherBackupPath).Length -gt 1048576 -or
            (Get-LhmFileSha256 -Path $launcherBackupPath) -cne [string]$manifest.launcherSha256) {
            throw 'Pre-stable launcher backup is missing, corrupt, or does not match trusted content.'
        }
    }
    elseif ($null -ne $manifest.launcherBackup -or
        $null -ne $manifest.launcherSha256 -or
        (Test-Path -LiteralPath $launcherBackupPath)) {
        throw 'Pre-stable launcher absence state is inconsistent with its backup fields.'
    }

    $expectedTaskBackupName = if ($NonLiveTestMode) {
        'managed-task.test.json'
    }
    else {
        'managed-task.xml'
    }
    $taskBackupPath = Join-Path $RecoveryRoot $expectedTaskBackupName
    if ([bool]$manifest.managedTaskExisted) {
        $trustedTaskSha256 = if ($NonLiveTestMode) {
            $ExpectedManagedTaskBackupSha256
        }
        else {
            $null
        }
        if ([string]$manifest.managedTaskBackup -cne $expectedTaskBackupName -or
            [string]$manifest.managedTaskSha256 -notmatch '^[0-9a-f]{64}$' -or
            ($NonLiveTestMode -and
                ([string]$trustedTaskSha256 -notmatch '^[0-9a-f]{64}$' -or
                    [string]$manifest.managedTaskSha256 -cne [string]$trustedTaskSha256)) -or
            -not (Test-Path -LiteralPath $taskBackupPath -PathType Leaf) -or
            (Get-Item -LiteralPath $taskBackupPath).Length -gt
                $(if ($NonLiveTestMode) { 65536 } else { 1048576 }) -or
            (Get-LhmFileSha256 -Path $taskBackupPath) -cne [string]$manifest.managedTaskSha256) {
            throw 'Pre-stable managed-task backup is missing, corrupt, or does not match trusted content.'
        }
        if (-not $NonLiveTestMode) {
            Assert-LhmPreStableManagedTaskBackupContract -Path $taskBackupPath
        }
    }
    elseif ($null -ne $manifest.managedTaskBackup -or
        $null -ne $manifest.managedTaskSha256 -or
        (Test-Path -LiteralPath $taskBackupPath)) {
        throw 'Pre-stable managed-task absence state is inconsistent with its backup fields.'
    }

    if (-not $NonLiveTestMode -and
        (-not [bool]$manifest.launcherExisted -or
            -not [bool]$manifest.managedTaskExisted)) {
        throw 'Production pre-stable recovery must preserve both discovered startup owners.'
    }

    if ((Get-LhmFileSha256 -Path $ExpectedPublicShimPath) -cne $ExpectedPublicShimSha256) {
        throw 'Public librehw.cmd does not match the pre-stable recovery packet.'
    }

    if ($RequireExternalStateMatch) {
        $launcherExists = Test-Path -LiteralPath $ExpectedLauncherTargetPath -PathType Leaf
        if ($launcherExists -ne [bool]$manifest.launcherExisted -or
            ($launcherExists -and
                (Get-LhmFileSha256 -Path $ExpectedLauncherTargetPath) -cne
                    [string]$manifest.launcherSha256)) {
            throw 'Current launcher state does not match the reusable pre-stable recovery packet.'
        }

        if ($NonLiveTestMode) {
            if ([string]::IsNullOrWhiteSpace($TestExternalStateRoot)) {
                throw 'TestExternalStateRoot is required to validate non-live recovery state.'
            }
            $currentTaskStatePath = Join-Path $TestExternalStateRoot 'managed-task.json'
            $currentTaskExists = Test-Path -LiteralPath $currentTaskStatePath -PathType Leaf
            if ($currentTaskExists -ne [bool]$manifest.managedTaskExisted -or
                ($currentTaskExists -and
                    (Get-LhmFileSha256 -Path $currentTaskStatePath) -cne
                        [string]$manifest.managedTaskSha256)) {
                throw 'Current test task state does not match the reusable pre-stable recovery packet.'
            }
        }
        else {
            $taskParts = Split-LhmManagedTaskPath -ManagedStartupTaskPath $ExpectedManagedTaskPath
            $currentTask = Get-ScheduledTask `
                -TaskPath $taskParts.TaskPath `
                -TaskName $taskParts.TaskName `
                -ErrorAction SilentlyContinue
            if (($null -ne $currentTask) -ne [bool]$manifest.managedTaskExisted) {
                throw 'Current managed-task presence does not match the reusable pre-stable recovery packet.'
            }
            if ($null -ne $currentTask) {
                $currentTaskXml =
                    Export-ScheduledTask -TaskPath $taskParts.TaskPath -TaskName $taskParts.TaskName
                $backupTaskXml =
                    Get-Content -LiteralPath $taskBackupPath -Raw -Encoding Unicode
                if ($currentTaskXml.Trim() -cne $backupTaskXml.Trim()) {
                    throw 'Current managed-task definition does not match the reusable pre-stable recovery packet.'
                }
            }
        }
    }

    return [pscustomobject]@{
        Root = Resolve-LhmFullPath -Path $RecoveryRoot
        ManifestPath = $manifestPath
        Manifest = $manifest
        LauncherBackupPath = $launcherBackupPath
        ManagedTaskBackupPath = $taskBackupPath
    }
}

function Read-LhmLegacyRecoveryPacket {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RecoveryRoot,

        [Parameter(Mandatory)]
        [string] $ExpectedPublicShimPath,

        [Parameter(Mandatory)]
        [string] $ExpectedPublicShimSha256,

        [switch] $NonLiveTestMode
    )

    Assert-LhmNoReparsePathAncestry `
        -Path $RecoveryRoot `
        -Label 'Legacy recovery'
    $manifestPath = Join-Path $RecoveryRoot 'recovery.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        (Get-Item -LiteralPath $manifestPath).Length -gt 65536) {
        throw "Legacy recovery manifest is missing or oversized: '$manifestPath'."
    }
    try {
        $manifest =
            Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "Legacy recovery manifest is invalid JSON: $($_.Exception.Message)"
    }

    $expectedProperties = @(@(
        'createdAt',
        'legacyTaskPath',
        'legacyTaskXml',
        'legacyTaskXmlSha256',
        'publicShimPath',
        'publicShimSha256',
        'schema',
        'shortcuts'
    ) | Sort-Object)
    $actualProperties = @($manifest.PSObject.Properties.Name | Sort-Object)
    if (($expectedProperties -join "`n") -cne ($actualProperties -join "`n")) {
        throw 'Legacy recovery manifest contains unsupported or missing fields.'
    }

    if ([string]$manifest.schema -cne 'sq.librehw.legacy-cutover-recovery.v1' -or
        [string]$manifest.legacyTaskPath -cne '\LibreHardwareMonitor' -or
        [string]$manifest.legacyTaskXml -cne 'legacy-task.xml' -or
        [string]$manifest.legacyTaskXmlSha256 -notmatch '^[0-9a-f]{64}$' -or
        -not (Test-LhmPathEqual `
            -Left ([string]$manifest.publicShimPath) `
            -Right $ExpectedPublicShimPath) -or
        [string]$manifest.publicShimSha256 -cne $ExpectedPublicShimSha256) {
        throw 'Legacy recovery manifest is incompatible with this release system.'
    }
    $createdAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        [string]$manifest.createdAt,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$createdAt)) {
        throw 'Legacy recovery createdAt is invalid.'
    }

    $expectedShortcutPaths = @(
        'C:\Users\Sev\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LibreHardwareMonitor.Windows.Forms.lnk',
        'C:\Users\Sev\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LibreHW-No-UAC.lnk'
    )
    $shortcutRecords = @($manifest.shortcuts)
    if ($shortcutRecords.Count -ne 2) {
        throw 'Legacy recovery must contain exactly two ordered shortcut records.'
    }

    $allowedNames = @('recovery.json', 'legacy-task.xml')
    for ($index = 0; $index -lt 2; $index++) {
        $record = $shortcutRecords[$index]
        $recordProperties = @($record.PSObject.Properties.Name | Sort-Object)
        $expectedRecordProperties = @(@('backup', 'existed', 'path', 'sha256') | Sort-Object)
        if (($recordProperties -join "`n") -cne ($expectedRecordProperties -join "`n") -or
            $record.existed -isnot [bool] -or
            [string]$record.path -cne $expectedShortcutPaths[$index]) {
            throw "Legacy recovery shortcut record $index is incompatible."
        }

        $expectedBackupName = "$index.lnk"
        if ([bool]$record.existed) {
            if ([string]$record.backup -cne $expectedBackupName -or
                [string]$record.sha256 -notmatch '^[0-9a-f]{64}$') {
                throw "Legacy recovery shortcut record $index has invalid backup fields."
            }
            $allowedNames += $expectedBackupName
        }
        elseif ($null -ne $record.backup -or $null -ne $record.sha256) {
            throw "Legacy recovery shortcut record $index has inconsistent absence fields."
        }
    }
    if (-not $NonLiveTestMode -and
        @($shortcutRecords | Where-Object { -not [bool]$_.existed }).Count -gt 0) {
        throw 'Production legacy recovery must preserve both discovered Start Menu shortcuts.'
    }

    $rootInfo = Get-Item -LiteralPath $RecoveryRoot -Force -ErrorAction Stop
    if (-not $rootInfo.PSIsContainer -or
        ($rootInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw 'Legacy recovery root must be a normal directory, not a reparse point.'
    }
    $entries = @(Get-ChildItem -LiteralPath $RecoveryRoot -Force)
    if ($entries.Count -ne $allowedNames.Count -or
        @($entries | Where-Object {
            $_.PSIsContainer -or
            ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or
            $_.Name -cnotin $allowedNames
        }).Count -gt 0) {
        throw 'Legacy recovery directory contains unexpected or unsafe entries.'
    }

    $taskXmlPath = Join-Path $RecoveryRoot 'legacy-task.xml'
    if ((Get-Item -LiteralPath $taskXmlPath).Length -gt 1048576 -or
        (Get-LhmFileSha256 -Path $taskXmlPath) -cne [string]$manifest.legacyTaskXmlSha256) {
        throw 'Legacy recovery task XML is oversized or has a hash mismatch.'
    }
    for ($index = 0; $index -lt 2; $index++) {
        if ([bool]$shortcutRecords[$index].existed) {
            $backupPath = Join-Path $RecoveryRoot "$index.lnk"
            if ((Get-Item -LiteralPath $backupPath).Length -gt 1048576 -or
                (Get-LhmFileSha256 -Path $backupPath) -cne
                    [string]$shortcutRecords[$index].sha256) {
                throw "Legacy recovery shortcut backup $index is oversized or has a hash mismatch."
            }
            Assert-LhmLegacyShortcutContract `
                -Path $backupPath `
                -Index $index `
                -SkipPinnedHash:$NonLiveTestMode
        }
    }
    if ((Get-LhmFileSha256 -Path $ExpectedPublicShimPath) -cne $ExpectedPublicShimSha256) {
        throw 'Public librehw.cmd does not match the legacy recovery packet.'
    }

    $taskXml = Read-LhmSafeTaskXml -Path $taskXmlPath -Label 'Legacy task backup'
    $namespace = [System.Xml.XmlNamespaceManager]::new($taskXml.NameTable)
    $namespace.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    $commandNode = $taskXml.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:Command', $namespace)
    $workingNode = $taskXml.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:WorkingDirectory', $namespace)
    $userNode = $taskXml.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:UserId', $namespace)
    $logonNode = $taskXml.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:LogonType', $namespace)
    $runLevelNode = $taskXml.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:RunLevel', $namespace)
    $uriNode = $taskXml.SelectSingleNode('/t:Task/t:RegistrationInfo/t:URI', $namespace)
    $multipleNode = $taskXml.SelectSingleNode('/t:Task/t:Settings/t:MultipleInstancesPolicy', $namespace)
    $startAvailableNode = $taskXml.SelectSingleNode('/t:Task/t:Settings/t:StartWhenAvailable', $namespace)
    $hardTerminateNode = $taskXml.SelectSingleNode('/t:Task/t:Settings/t:AllowHardTerminate', $namespace)
    $executionNode = $taskXml.SelectSingleNode('/t:Task/t:Settings/t:ExecutionTimeLimit', $namespace)
    $actions = @($taskXml.SelectNodes('/t:Task/t:Actions/t:Exec', $namespace))
    $triggers = @($taskXml.SelectNodes('/t:Task/t:Triggers/*', $namespace))
    $principals = @($taskXml.SelectNodes('/t:Task/t:Principals/t:Principal', $namespace))
    $expectedLegacyExecutable =
        'E:\SQ_HQ\Monitoring\sq-librehwdev\sq-librehw\bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe'
    if ($actions.Count -ne 1 -or
        $triggers.Count -ne 1 -or
        $triggers[0].LocalName -cne 'LogonTrigger' -or
        $principals.Count -ne 1 -or
        $null -eq $commandNode -or
        -not (Test-LhmPathEqual -Left $commandNode.InnerText -Right $expectedLegacyExecutable) -or
        $null -eq $workingNode -or
        -not (Test-LhmPathEqual `
            -Left $workingNode.InnerText `
            -Right (Split-Path -Parent $expectedLegacyExecutable)) -or
        $null -eq $userNode -or
        $userNode.InnerText -cne [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or
        $null -eq $logonNode -or
        $logonNode.InnerText -cne 'InteractiveToken' -or
        $null -eq $runLevelNode -or
        $runLevelNode.InnerText -cne 'HighestAvailable' -or
        $null -eq $uriNode -or
        $uriNode.InnerText -cne '\LibreHardwareMonitor' -or
        $null -eq $multipleNode -or
        $multipleNode.InnerText -cne 'IgnoreNew' -or
        $null -eq $startAvailableNode -or
        $startAvailableNode.InnerText -cne 'true' -or
        $null -eq $hardTerminateNode -or
        $hardTerminateNode.InnerText -cne 'false' -or
        $null -eq $executionNode -or
        $executionNode.InnerText -cne 'PT0S') {
        throw 'Legacy task backup does not match the exact discovered legacy task contract.'
    }

    return [pscustomobject]@{
        Root = Resolve-LhmFullPath -Path $RecoveryRoot
        ManifestPath = $manifestPath
        Manifest = $manifest
        TaskXmlPath = $taskXmlPath
        TaskXmlText = Get-Content -LiteralPath $taskXmlPath -Raw -Encoding Unicode
        ShortcutPaths = $expectedShortcutPaths
        ShortcutRecords = $shortcutRecords
        ShortcutBackupPaths = @(
            (Join-Path $RecoveryRoot '0.lnk'),
            (Join-Path $RecoveryRoot '1.lnk')
        )
    }
}

function Assert-LhmLegacyShortcutContract {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [ValidateRange(0, 1)]
        [int] $Index,

        [switch] $SkipPinnedHash
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Legacy shortcut $Index is missing: '$Path'."
    }
    $shortcutInfo = Get-Item -LiteralPath $Path -Force
    if (($shortcutInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or
        $shortcutInfo.Length -gt 1048576) {
        throw "Legacy shortcut $Index is unsafe or oversized."
    }
    if (-not $SkipPinnedHash -and
        (Get-LhmFileSha256 -Path $Path) -cne $script:LhmLegacyShortcutSha256[$Index]) {
        throw "Legacy shortcut $Index does not match its source-pinned discovered hash."
    }

    $shell = $null
    $shortcut = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Resolve-LhmFullPath -Path $Path))
        if (-not (Test-LhmPathEqual `
                -Left ([string]$shortcut.TargetPath) `
                -Right $script:LhmLegacyShortcutTargetPath[$Index]) -or
            [string]$shortcut.Arguments -cne $script:LhmLegacyShortcutArguments[$Index] -or
            -not (Test-LhmPathEqual `
                -Left ([string]$shortcut.WorkingDirectory) `
                -Right $script:LhmLegacyShortcutWorkingDirectory)) {
            throw "Legacy shortcut $Index does not match the exact discovered target contract."
        }
    }
    finally {
        if ($null -ne $shortcut -and [System.Runtime.InteropServices.Marshal]::IsComObject($shortcut)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }
        if ($null -ne $shell -and [System.Runtime.InteropServices.Marshal]::IsComObject($shell)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }
}

function Ensure-LhmRuntimeConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $InstallRoot,

        [Parameter(Mandatory)]
        [string] $DataRoot,

        [Parameter(Mandatory)]
        [string] $ManagedStartupTaskPath
    )

    $runtimeConfigPath = Join-Path $InstallRoot $script:LhmRuntimeConfigName
    if (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf) {
        $null = Read-LhmRuntimeConfig `
            -Path $runtimeConfigPath `
            -ExpectedDataRoot $DataRoot `
            -ExpectedManagedTaskPath $ManagedStartupTaskPath
        return $runtimeConfigPath
    }

    $runtimeConfig = [ordered]@{
        schema = $script:LhmRuntimeSchema
        dataRoot = (Resolve-LhmFullPath -Path $DataRoot)
        managedStartupTaskPath = $ManagedStartupTaskPath
    }

    $temporaryPath = Join-Path $InstallRoot ".$($script:LhmRuntimeConfigName).$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $runtimeConfig |
            ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $runtimeConfigPath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }

    return $runtimeConfigPath
}

function Initialize-LhmDataRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $DataRoot,

        [AllowNull()]
        [string] $InitialConfigSource
    )

    [System.IO.Directory]::CreateDirectory($DataRoot) | Out-Null
    $DataRoot = Assert-LhmNormalDirectoryTree `
        -Path $DataRoot `
        -Label 'LibreHW data root'
    $logsPath = Join-Path $DataRoot 'logs'
    [System.IO.Directory]::CreateDirectory($logsPath) | Out-Null
    $null = Assert-LhmNormalDirectoryTree `
        -Path $logsPath `
        -Label 'LibreHW log root'
    $null = Assert-LhmNormalDirectoryTree `
        -Path $DataRoot `
        -Label 'LibreHW data root'

    $settingsPath = Assert-LhmSafeFileDestination `
        -Path (Join-Path $DataRoot $script:LhmSettingsFileName) `
        -Label 'LibreHW settings'
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        $null = Assert-LhmNormalFile -Path $settingsPath -Label 'LibreHW settings'
        $backupPath = "$settingsPath.backup"
        if (Test-Path -LiteralPath $backupPath) {
            $null = Assert-LhmNormalFile `
                -Path $backupPath `
                -Label 'LibreHW settings backup'
        }
        return $settingsPath
    }

    if ([string]::IsNullOrWhiteSpace($InitialConfigSource)) {
        throw "First installation requires -InitialConfigSource for '$($script:LhmSettingsFileName)'."
    }

    $resolvedSource = Assert-LhmNormalFile `
        -Path $InitialConfigSource `
        -Label 'Initial config source'

    $sourceInfo = Get-Item -LiteralPath $resolvedSource
    if ($sourceInfo.Length -eq 0) {
        throw 'Initial config source is empty.'
    }

    $temporaryPath = Join-Path $DataRoot ".$($script:LhmSettingsFileName).$([guid]::NewGuid().ToString('N')).tmp"
    try {
        Copy-Item -LiteralPath $resolvedSource -Destination $temporaryPath
        $temporaryInfo = Get-Item -LiteralPath $temporaryPath
        if ($temporaryInfo.Length -ne $sourceInfo.Length -or
            (Get-LhmFileSha256 -Path $temporaryPath) -cne (Get-LhmFileSha256 -Path $resolvedSource)) {
            throw 'Initial config verification failed after copying it to the data root.'
        }

        Move-Item -LiteralPath $temporaryPath -Destination $settingsPath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }

    return $settingsPath
}

function Get-LhmProcesses {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ExpectedExecutablePath
    )

    $expectedPath = Resolve-LhmFullPath -Path $ExpectedExecutablePath
    $results = foreach ($process in @(Get-Process -Name $script:LhmProcessName -ErrorAction SilentlyContinue)) {
        $path = $null
        $inspectionError = $null
        try {
            $path = $process.Path
        }
        catch {
            $inspectionError = $_.Exception.Message
        }

        [pscustomobject]@{
            Process = $process
            Path = $path
            IsExact = $path -and (Test-LhmPathEqual -Left $path -Right $expectedPath)
            InspectionError = $inspectionError
        }
    }

    return @($results)
}

function Stop-LhmExactProcessForRelease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ExpectedExecutablePath,

        [switch] $AllowStopExactProcess
    )

    $processes = @(Get-LhmProcesses -ExpectedExecutablePath $ExpectedExecutablePath)
    $unknown = @($processes | Where-Object { -not $_.Path })
    if ($unknown.Count -gt 0) {
        throw 'Libre Hardware Monitor is running, but at least one process path cannot be inspected.'
    }

    $mismatched = @($processes | Where-Object { -not $_.IsExact })
    if ($mismatched.Count -gt 0) {
        $details = ($mismatched | ForEach-Object { "PID $($_.Process.Id): $($_.Path)" }) -join '; '
        throw "Refusing to touch path-mismatched Libre Hardware Monitor process(es): $details"
    }

    $exact = @($processes | Where-Object IsExact)
    if ($exact.Count -eq 0) {
        return
    }

    if (-not $AllowStopExactProcess) {
        $ids = ($exact.Process.Id -join ', ')
        throw "Libre Hardware Monitor is running from the installed path (PID(s) $ids). Close it or use -AllowStopExactProcess."
    }

    foreach ($entry in $exact) {
        Stop-Process -Id $entry.Process.Id -ErrorAction Stop
        $entry.Process.WaitForExit(10000)
        if (-not $entry.Process.HasExited) {
            throw "Timed out stopping exact Libre Hardware Monitor PID $($entry.Process.Id)."
        }
    }
}

function Assert-LhmProcessStateForRelease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ExpectedExecutablePath,

        [switch] $AllowStopExactProcess
    )

    $processes = @(Get-LhmProcesses -ExpectedExecutablePath $ExpectedExecutablePath)
    $unknown = @($processes | Where-Object { -not $_.Path })
    if ($unknown.Count -gt 0) {
        throw 'Libre Hardware Monitor is running, but at least one process path cannot be inspected.'
    }

    $mismatched = @($processes | Where-Object { -not $_.IsExact })
    if ($mismatched.Count -gt 0) {
        $details = ($mismatched | ForEach-Object { "PID $($_.Process.Id): $($_.Path)" }) -join '; '
        throw "Refusing to touch path-mismatched Libre Hardware Monitor process(es): $details"
    }

    $exact = @($processes | Where-Object IsExact)
    if ($exact.Count -gt 0 -and -not $AllowStopExactProcess) {
        $ids = ($exact.Process.Id -join ', ')
        throw "Libre Hardware Monitor is running from the installed path (PID(s) $ids). Close it or use -AllowStopExactProcess."
    }

    return $exact
}

function Split-LhmManagedTaskPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ManagedStartupTaskPath
    )

    if ($ManagedStartupTaskPath -cne $script:LhmManagedTaskPath) {
        throw "Unsupported managed task path '$ManagedStartupTaskPath'."
    }

    return [pscustomobject]@{
        TaskPath = '\SevGrp\AdminTask\'
        TaskName = 'LibreHW-No-UAC'
    }
}

function Register-LhmManagedTask {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ExecutablePath,

        [Parameter(Mandatory)]
        [string] $InstallRoot,

        [Parameter(Mandatory)]
        [string] $ManagedStartupTaskPath
    )

    $taskParts = Split-LhmManagedTaskPath -ManagedStartupTaskPath $ManagedStartupTaskPath
    $qualifiedUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction `
        -Execute $ExecutablePath `
        -WorkingDirectory $InstallRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $qualifiedUser
    $principal = New-ScheduledTaskPrincipal `
        -UserId $qualifiedUser `
        -LogonType Interactive `
        -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet `
        -MultipleInstances IgnoreNew `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -DisallowHardTerminate `
        -ExecutionTimeLimit ([TimeSpan]::Zero)

    Register-ScheduledTask `
        -TaskPath $taskParts.TaskPath `
        -TaskName $taskParts.TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description 'Managed Libre Hardware Monitor logon and on-demand launcher.' `
        -Force | Out-Null
}

function Start-LhmManagedTaskAndWait {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ManagedStartupTaskPath,

        [Parameter(Mandatory)]
        [string] $ExpectedExecutablePath,

        [int] $TimeoutSeconds = 30
    )

    $taskParts = Split-LhmManagedTaskPath -ManagedStartupTaskPath $ManagedStartupTaskPath
    Start-ScheduledTask -TaskPath $taskParts.TaskPath -TaskName $taskParts.TaskName

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $processes = @(Get-LhmProcesses -ExpectedExecutablePath $ExpectedExecutablePath)
        $unknown = @($processes | Where-Object { -not $_.Path })
        $mismatched = @($processes | Where-Object { $_.Path -and -not $_.IsExact })
        if ($unknown.Count -gt 0 -or $mismatched.Count -gt 0) {
            throw 'A Libre Hardware Monitor process appeared, but its path is not the exact installed executable.'
        }

        $exact = @($processes | Where-Object IsExact)
        if ($exact.Count -eq 1) {
            return $exact[0].Process
        }

        if ($exact.Count -gt 1) {
            throw 'The managed task started more than one exact Libre Hardware Monitor process.'
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Timed out waiting for '$ExpectedExecutablePath' to start."
}

function Assert-LhmHttpHealth {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [uri] $HealthUri,

        [int] $TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            $response = Invoke-WebRequest -Uri $HealthUri -TimeoutSec 3
            if ($response.StatusCode -eq 200 -and -not [string]::IsNullOrWhiteSpace($response.Content)) {
                $payload = $response.Content | ConvertFrom-Json
                if ([string]$payload.Text -ceq 'Sensor' -and
                    -not [string]::IsNullOrWhiteSpace([string]$payload.Version) -and
                    $null -ne $payload.PSObject.Properties['Children'] -and
                    $payload.Children -is [array]) {
                    return
                }
                $lastError = 'HTTP response was JSON but not the Libre Hardware Monitor data.json envelope.'
            }
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Libre Hardware Monitor health check '$HealthUri' failed: $lastError"
}

function Remove-LhmOwnedDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $ExpectedParent,

        [Parameter(Mandatory)]
        [string] $RequiredLeafPrefix
    )

    $resolvedPath = Resolve-LhmFullPath -Path $Path
    $resolvedParent = Resolve-LhmFullPath -Path $ExpectedParent
    Assert-LhmNoReparsePathAncestry `
        -Path $resolvedParent `
        -Label 'Owned directory parent'
    if (-not (Test-LhmPathEqual -Left (Split-Path -Parent $resolvedPath) -Right $resolvedParent)) {
        throw "Refusing to remove directory outside '$resolvedParent': '$resolvedPath'."
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        $null = Assert-LhmNormalDirectoryTree `
            -Path $resolvedPath `
            -Label 'Owned directory'
    }

    if (-not (Test-LhmOwnedDirectoryLeafName `
        -LeafName (Split-Path -Leaf $resolvedPath) `
        -RequiredLeafPrefix $RequiredLeafPrefix)) {
        throw "Refusing to remove directory outside the owned naming contract '$RequiredLeafPrefix': '$resolvedPath'."
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Copy-LhmPayloadPair {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $SourceDirectory,

        [Parameter(Mandatory)]
        [string] $DestinationDirectory,

        [switch] $DestinationMayContainOtherEntries
    )

    $null = Read-LhmReleasePayload -Directory $SourceDirectory
    $null = Assert-LhmNearestExistingPathAncestry `
        -Path $DestinationDirectory `
        -Label 'Release payload destination'
    [System.IO.Directory]::CreateDirectory($DestinationDirectory) | Out-Null
    $null = Assert-LhmNormalDirectoryTree `
        -Path $DestinationDirectory `
        -Label 'Release payload destination'
    foreach ($name in @($script:LhmExecutableName, $script:LhmReleaseManifestName)) {
        $destinationPath = Assert-LhmSafeFileDestination `
            -Path (Join-Path $DestinationDirectory $name) `
            -Label 'Release payload destination'
        if (Test-Path -LiteralPath $destinationPath) {
            $null = Assert-LhmNormalFile `
                -Path $destinationPath `
                -Label 'Existing release payload destination'
        }
        Copy-Item `
            -LiteralPath (Join-Path $SourceDirectory $name) `
            -Destination $destinationPath
    }
    return Read-LhmReleasePayload `
        -Directory $DestinationDirectory `
        -RequireCandidateShape:(-not $DestinationMayContainOtherEntries)
}

function Clear-LhmPayloadPair {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Directory
    )

    if (-not (Test-Path -LiteralPath $Directory)) {
        return
    }
    $Directory = Assert-LhmNormalDirectoryTree `
        -Path $Directory `
        -Label 'Release payload cleanup directory'
    foreach ($name in @($script:LhmExecutableName, $script:LhmReleaseManifestName)) {
        $path = Join-Path $Directory $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $path = Assert-LhmNormalFile `
                -Path $path `
                -Label 'Release payload cleanup file'
            Remove-Item -LiteralPath $path -Force
        }
    }
}

function Write-LhmTransactionState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $TransactionRoot,

        [Parameter(Mandatory)]
        [hashtable] $State
    )

    $TransactionRoot = Assert-LhmNormalDirectoryTree `
        -Path $TransactionRoot `
        -Label 'Release transaction'
    $State.schema = $script:LhmTransactionSchema
    $statePath = Join-Path $TransactionRoot 'state.json'
    $temporaryPath = Join-Path $TransactionRoot ".state.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = $State | ConvertTo-Json -Depth 6
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json)
        $stream = [System.IO.FileStream]::new(
            $temporaryPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None,
            4096,
            [System.IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }
        Move-Item -LiteralPath $temporaryPath -Destination $statePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Read-LhmTransactionState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $TransactionRoot
    )

    $TransactionRoot = Assert-LhmNormalDirectoryTree `
        -Path $TransactionRoot `
        -Label 'Release transaction'
    $statePath = Assert-LhmNormalFile `
        -Path (Join-Path $TransactionRoot 'state.json') `
        -Label 'Release transaction state'
    if ((Get-Item -LiteralPath $statePath).Length -gt 65536) {
        throw 'Release transaction state is unexpectedly large.'
    }

    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$state.schema -cne $script:LhmTransactionSchema) {
        throw "Unsupported release transaction schema '$($state.schema)'."
    }

    $expectedProperties = @(@(
        'createdAt',
        'hadCurrent',
        'hadLauncher',
        'hadRollback',
        'installRoot',
        'launcherTargetPath',
        'managedTaskExisted',
        'operation',
        'phase',
        'publicShimPath',
        'publicShimSha256',
        'schema'
    ) | Sort-Object)
    $actualProperties = @($state.PSObject.Properties.Name | Sort-Object)
    if (($expectedProperties -join "`n") -cne ($actualProperties -join "`n")) {
        throw 'Release transaction state contains unsupported or missing fields.'
    }

    if ([string]$state.operation -notin @('promote', 'rollback') -or
        [string]$state.phase -notin @(
            'prepared',
            'previous-removed',
            'candidate-installed',
            'task-staged',
            'health-accepted',
            'committed')) {
        throw 'Release transaction operation or phase is invalid.'
    }

    foreach ($propertyName in @('hadCurrent', 'hadRollback', 'hadLauncher', 'managedTaskExisted')) {
        if ($state.$propertyName -isnot [bool]) {
            throw "Release transaction '$propertyName' must be a JSON Boolean."
        }
    }

    if ([string]$state.publicShimSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'Release transaction publicShimSha256 is invalid.'
    }
    $createdAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        [string]$state.createdAt,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$createdAt)) {
        throw 'Release transaction createdAt is invalid.'
    }
    return $state
}

function Export-LhmScheduledTaskSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $TaskPath,

        [Parameter(Mandatory)]
        [string] $TaskName,

        [Parameter(Mandatory)]
        [string] $SnapshotPath
    )

    $SnapshotPath = Assert-LhmSafeFileDestination `
        -Path $SnapshotPath `
        -Label 'Scheduled Task snapshot'
    $snapshotParent = Split-Path -Parent $SnapshotPath
    $null = Assert-LhmNormalDirectoryTree `
        -Path $snapshotParent `
        -Label 'Scheduled Task snapshot parent'
    if (Test-Path -LiteralPath $SnapshotPath) {
        $null = Assert-LhmNormalFile `
            -Path $SnapshotPath `
            -Label 'Scheduled Task snapshot'
    }

    $task = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        return $false
    }

    Export-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName |
        Set-Content -LiteralPath $SnapshotPath -Encoding Unicode
    $null = Assert-LhmNormalFile `
        -Path $SnapshotPath `
        -Label 'Scheduled Task snapshot'
    return $true
}

function Restore-LhmScheduledTaskSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $TaskPath,

        [Parameter(Mandatory)]
        [string] $TaskName,

        [Parameter(Mandatory)]
        [bool] $Existed,

        [Parameter(Mandatory)]
        [string] $SnapshotPath
    )

    if ($Existed) {
        $SnapshotPath = Assert-LhmNormalFile `
            -Path $SnapshotPath `
            -Label 'Scheduled Task snapshot'
        $xml = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding Unicode
        Register-ScheduledTask `
            -TaskPath $TaskPath `
            -TaskName $TaskName `
            -Xml $xml `
            -Force | Out-Null
        return
    }

    $task = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -ne $task) {
        Unregister-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Confirm:$false
    }
}

function Repair-LhmInterruptedTransaction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $InstallRoot,

        [Parameter(Mandatory)]
        [string] $LauncherTargetPath,

        [Parameter(Mandatory)]
        [string] $PublicShimPath,

        [switch] $NonLiveTestMode,

        [AllowNull()]
        [string] $TestExternalStateRoot
    )

    $LauncherTargetPath = Assert-LhmSafeFileDestination `
        -Path $LauncherTargetPath `
        -Label 'LibreHW launcher'
    $PublicShimPath = Assert-LhmNormalFile `
        -Path $PublicShimPath `
        -Label 'Public librehw shim'

    $transactionRoot = Join-Path $InstallRoot '.release-transaction'
    if (-not (Test-Path -LiteralPath $transactionRoot -PathType Container)) {
        return $false
    }
    $transactionRoot = Assert-LhmNormalDirectoryTree `
        -Path $transactionRoot `
        -Label 'Release transaction'

    if (-not (Test-Path -LiteralPath (Join-Path $transactionRoot 'state.json') -PathType Leaf)) {
        # The journal is created before snapshots, but product/external state is
        # not touched until state.json is durable.
        Remove-LhmOwnedDirectory `
            -Path $transactionRoot `
            -ExpectedParent $InstallRoot `
            -RequiredLeafPrefix '.release-transaction'
        return $true
    }

    $state = Read-LhmTransactionState -TransactionRoot $transactionRoot
    if (-not (Test-LhmPathEqual -Left ([string]$state.installRoot) -Right $InstallRoot) -or
        -not (Test-LhmPathEqual -Left ([string]$state.launcherTargetPath) -Right $LauncherTargetPath) -or
        -not (Test-LhmPathEqual -Left ([string]$state.publicShimPath) -Right $PublicShimPath)) {
        throw 'Release transaction paths do not match the requested repair scope.'
    }

    if ((Get-LhmFileSha256 -Path $PublicShimPath) -cne [string]$state.publicShimSha256) {
        throw 'Public librehw.cmd changed during release transaction; repair is blocked before mutation.'
    }

    $previousCurrent = Join-Path $transactionRoot 'previous-current'
    $previousRollback = Join-Path $transactionRoot 'previous-rollback'
    $launcherBackup = Join-Path $transactionRoot 'launcher-backup.ps1'
    if ([bool]$state.hadCurrent) {
        $null = Read-LhmReleasePayload -Directory $previousCurrent -RequireCandidateShape
    }
    elseif (Test-Path -LiteralPath $previousCurrent) {
        throw 'Release transaction has an unexpected previous-current snapshot.'
    }
    if ([bool]$state.hadRollback) {
        $null = Read-LhmReleasePayload -Directory $previousRollback -RequireCandidateShape
    }
    elseif (Test-Path -LiteralPath $previousRollback) {
        throw 'Release transaction has an unexpected previous-rollback snapshot.'
    }
    if ([bool]$state.hadLauncher -ne (Test-Path -LiteralPath $launcherBackup -PathType Leaf)) {
        throw 'Release transaction launcher snapshot does not match its state.'
    }
    if ($NonLiveTestMode) {
        $managedBackup = Join-Path $transactionRoot 'managed-task.test.json'
    }
    else {
        $managedBackup = Join-Path $transactionRoot 'managed-task.xml'
    }
    if ([bool]$state.managedTaskExisted -ne (Test-Path -LiteralPath $managedBackup -PathType Leaf)) {
        throw 'Release transaction managed-task snapshot does not match its state.'
    }

    if ([string]$state.phase -cne 'committed') {
        Clear-LhmPayloadPair -Directory $InstallRoot
        if ([bool]$state.hadCurrent) {
            $null = Copy-LhmPayloadPair `
                -SourceDirectory $previousCurrent `
                -DestinationDirectory $InstallRoot `
                -DestinationMayContainOtherEntries
        }

        $rollbackRoot = Join-Path $InstallRoot 'rollback'
        [System.IO.Directory]::CreateDirectory($rollbackRoot) | Out-Null
        Clear-LhmPayloadPair -Directory $rollbackRoot
        if ([bool]$state.hadRollback) {
            $null = Copy-LhmPayloadPair `
                -SourceDirectory $previousRollback `
                -DestinationDirectory $rollbackRoot
        }

        $LauncherTargetPath = Assert-LhmSafeFileDestination `
            -Path $LauncherTargetPath `
            -Label 'LibreHW launcher'
        if ([bool]$state.hadLauncher) {
            Copy-Item -LiteralPath $launcherBackup -Destination $LauncherTargetPath -Force
        }
        elseif (Test-Path -LiteralPath $LauncherTargetPath -PathType Leaf) {
            Remove-Item -LiteralPath $LauncherTargetPath -Force
        }

        if ($NonLiveTestMode) {
            if (-not [string]::IsNullOrWhiteSpace($TestExternalStateRoot)) {
                $managedStatePath = Join-Path $TestExternalStateRoot 'managed-task.json'
                $managedBackup = Join-Path $transactionRoot 'managed-task.test.json'
                if ([bool]$state.managedTaskExisted) {
                    Copy-Item -LiteralPath $managedBackup -Destination $managedStatePath -Force
                }
                elseif (Test-Path -LiteralPath $managedStatePath -PathType Leaf) {
                    Remove-Item -LiteralPath $managedStatePath -Force
                }
            }
        }
        else {
            Restore-LhmScheduledTaskSnapshot `
                -TaskPath '\SevGrp\AdminTask\' `
                -TaskName 'LibreHW-No-UAC' `
                -Existed ([bool]$state.managedTaskExisted) `
                -SnapshotPath (Join-Path $transactionRoot 'managed-task.xml')
        }
    }

    Remove-LhmOwnedDirectory `
        -Path $transactionRoot `
        -ExpectedParent $InstallRoot `
        -RequiredLeafPrefix '.release-transaction'
    return $true
}

function Remove-LhmAbandonedPreparationDirectories {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $InstallRoot
    )

    if (-not (Test-Path -LiteralPath $InstallRoot -PathType Container)) {
        return 0
    }
    $InstallRoot = Assert-LhmNormalDirectoryTree `
        -Path $InstallRoot `
        -Label 'Installed runtime root'
    if (Test-Path -LiteralPath (Join-Path $InstallRoot '.release-transaction')) {
        throw 'Cannot remove preparation directories while an active release transaction exists.'
    }

    $directories = @(Get-ChildItem -LiteralPath $InstallRoot -Force -Directory |
        Where-Object {
            Test-LhmOwnedDirectoryLeafName `
                -LeafName $_.Name `
                -RequiredLeafPrefix '.release-preparing-'
        })
    foreach ($directory in $directories) {
        Remove-LhmOwnedDirectory `
            -Path $directory.FullName `
            -ExpectedParent $InstallRoot `
            -RequiredLeafPrefix '.release-preparing-'
    }
    return $directories.Count
}

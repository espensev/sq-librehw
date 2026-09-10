#Requires -Version 5.1

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidateSet('Plan', 'Apply', 'Validate')]
    [string] $Mode = 'Plan',

    [string] $SoleXDataRoot,

    [string] $SoleXBinRoot,

    [string] $RecoveryRoot,

    [switch] $NonLiveTestMode,

    [string] $TestIdentityVerifierPath,

    [ValidateSet('None', 'AfterGeneratedFiles', 'AfterShimRemoval')]
    [string] $TestFailurePoint = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')

$script:IntegrationTarget = 'librehw-solex'
$script:IntegrationAlias = 'librehw-ui'
$script:RetirementSchema = 'sq.librehw.solex-retirement.v1'
$script:ExpectedExtensionsProduct = 'SoleX.Sq.Extensions'
$script:ExpectedExtensionsSchema = 1
$script:RetirementMutexName = 'Local\LibreHardwareMonitor.SoleXRetirement.v1'

function Resolve-RetirementPaths {
    [CmdletBinding()]
    param()

    if ($NonLiveTestMode) {
        foreach ($entry in @(
            @{ Name = 'SoleXDataRoot'; Value = $SoleXDataRoot },
            @{ Name = 'SoleXBinRoot'; Value = $SoleXBinRoot },
            @{ Name = 'RecoveryRoot'; Value = $RecoveryRoot },
            @{ Name = 'TestIdentityVerifierPath'; Value = $TestIdentityVerifierPath }
        )) {
            if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
                throw "NonLiveTestMode requires -$($entry.Name)."
            }
        }
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($TestIdentityVerifierPath) -or
            $TestFailurePoint -cne 'None') {
            throw 'Test-only retirement parameters require NonLiveTestMode.'
        }
        $dataBase = Get-LhmRequiredEnvironmentRoot -Name 'SEV_LOCAL_DATA'
        $binBase = Get-LhmRequiredEnvironmentRoot -Name 'SEV_LOCAL_BIN'
        $expectedData = Join-Path $dataBase 'SoleX'
        $expectedRecovery = Join-Path $dataBase 'LibreHardwareMonitor\solex-retirement'
        if ([string]::IsNullOrWhiteSpace($SoleXDataRoot)) { $SoleXDataRoot = $expectedData }
        if ([string]::IsNullOrWhiteSpace($SoleXBinRoot)) { $SoleXBinRoot = $binBase }
        if ([string]::IsNullOrWhiteSpace($RecoveryRoot)) { $RecoveryRoot = $expectedRecovery }
        if (-not (Test-LhmPathEqual -Left $SoleXDataRoot -Right $expectedData) -or
            -not (Test-LhmPathEqual -Left $SoleXBinRoot -Right $binBase) -or
            -not (Test-LhmPathEqual -Left $RecoveryRoot -Right $expectedRecovery)) {
            throw 'Production retirement paths must resolve from SEV_LOCAL_DATA and SEV_LOCAL_BIN.'
        }
    }

    $resolved = [ordered]@{
        SoleXDataRoot = Resolve-LhmFullPath -Path $SoleXDataRoot
        SoleXBinRoot = Resolve-LhmFullPath -Path $SoleXBinRoot
        RecoveryRoot = Resolve-LhmFullPath -Path $RecoveryRoot
    }
    if ($NonLiveTestMode) {
        $temporaryRoot = Resolve-LhmFullPath -Path ([System.IO.Path]::GetTempPath())
        foreach ($entry in @($resolved.GetEnumerator())) {
            if (-not (Test-LhmPathWithin -Path $entry.Value -Root $temporaryRoot)) {
                throw "NonLiveTestMode path '$($entry.Key)' must be beneath the OS temporary directory."
            }
        }
        if (-not (Test-LhmPathWithin -Path $TestIdentityVerifierPath -Root $temporaryRoot)) {
            throw 'NonLiveTestMode identity verifier must be beneath the OS temporary directory.'
        }
    }

    if (-not (Test-Path -LiteralPath $resolved.SoleXDataRoot -PathType Container) -or
        -not (Test-Path -LiteralPath $resolved.SoleXBinRoot -PathType Container)) {
        throw 'SoleX data and bin roots must already exist.'
    }
    Assert-LhmNoReparsePathAncestry -Path $resolved.SoleXDataRoot -Label 'SoleX data root'
    Assert-LhmNoReparsePathAncestry -Path $resolved.SoleXBinRoot -Label 'SoleX bin root'

    [pscustomobject]@{
        SoleXDataRoot = $resolved.SoleXDataRoot
        SoleXBinRoot = $resolved.SoleXBinRoot
        RecoveryRoot = $resolved.RecoveryRoot
        EffectiveCatalogPath = Join-Path $resolved.SoleXDataRoot 'generated\solex.effective.json'
        EffectiveManifestPath = Join-Path $resolved.SoleXDataRoot 'generated\solex.commands.json'
        ExtensionsReceiptPath = Join-Path $resolved.SoleXDataRoot '.solex-extensions.json'
        ExtensionPackPath = Join-Path $resolved.SoleXDataRoot 'extensions\librehw'
        IntegrationShimPath = Join-Path $resolved.SoleXBinRoot ($script:IntegrationTarget + '.cmd')
    }
}

function Read-RetirementJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Label
    )

    $null = Assert-LhmNormalFile -Path $Path -Label $Label
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "$Label is not valid JSON: '$Path'."
    }
}

function Get-JsonPropertyValue {
    [CmdletBinding()]
    param(
        [AllowNull()][object] $Object,
        [Parameter(Mandatory)][string] $Name
    )

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-RetirementState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object] $Paths)

    if (Test-Path -LiteralPath $Paths.ExtensionPackPath) {
        throw "LibreHW SoleX extension pack still exists: '$($Paths.ExtensionPackPath)'."
    }

    $catalog = Read-RetirementJson -Path $Paths.EffectiveCatalogPath -Label 'SoleX effective catalog'
    $manifest = Read-RetirementJson -Path $Paths.EffectiveManifestPath -Label 'SoleX effective command manifest'
    $receipt = Read-RetirementJson -Path $Paths.ExtensionsReceiptPath -Label 'SoleX extensions receipt'

    if ([string](Get-JsonPropertyValue -Object $receipt -Name 'product') -cne $script:ExpectedExtensionsProduct -or
        [int](Get-JsonPropertyValue -Object $receipt -Name 'schemaVersion') -ne $script:ExpectedExtensionsSchema) {
        throw 'SoleX extensions receipt product or schema is not supported.'
    }
    if (-not (Test-LhmPathEqual -Left ([string](Get-JsonPropertyValue -Object $receipt -Name 'dataDirectory')) -Right $Paths.SoleXDataRoot) -or
        -not (Test-LhmPathEqual -Left ([string](Get-JsonPropertyValue -Object $receipt -Name 'binDirectory')) -Right $Paths.SoleXBinRoot)) {
        throw 'SoleX extensions receipt roots do not match the selected retirement roots.'
    }

    $targets = @((Get-JsonPropertyValue -Object $catalog -Name 'targets'))
    $commands = @((Get-JsonPropertyValue -Object $manifest -Name 'commands'))
    $shortcuts = @((Get-JsonPropertyValue -Object $manifest -Name 'shortcuts'))
    $files = @((Get-JsonPropertyValue -Object $receipt -Name 'files'))

    $integrationTargets = @($targets | Where-Object {
        [string](Get-JsonPropertyValue -Object $_ -Name 'key') -ieq $script:IntegrationTarget
    })
    $integrationCommands = @($commands | Where-Object { [string]$_ -ieq $script:IntegrationTarget })
    $integrationShortcuts = @($shortcuts | Where-Object {
        [string](Get-JsonPropertyValue -Object $_ -Name 'id') -ieq $script:IntegrationTarget -or
        [string](Get-JsonPropertyValue -Object $_ -Name 'target') -ieq $script:IntegrationTarget -or
        [string](Get-JsonPropertyValue -Object $_ -Name 'target') -ieq $script:IntegrationAlias
    })
    if ($integrationTargets.Count -gt 1 -or $integrationCommands.Count -gt 1 -or
        $integrationShortcuts.Count -gt 1) {
        throw 'SoleX generated state contains duplicate LibreHW integration entries.'
    }

    $receiptByPath = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $files) {
        $recordPath = [string](Get-JsonPropertyValue -Object $record -Name 'path')
        if ([string]::IsNullOrWhiteSpace($recordPath) -or $receiptByPath.ContainsKey($recordPath)) {
            throw 'SoleX extensions receipt contains a blank or duplicate file path.'
        }
        $receiptByPath[$recordPath] = $record
    }
    foreach ($ownedPath in @($Paths.EffectiveCatalogPath, $Paths.EffectiveManifestPath)) {
        if (-not $receiptByPath.ContainsKey($ownedPath)) {
            throw "SoleX extensions receipt does not own '$ownedPath'."
        }
        $expectedHash = [string](Get-JsonPropertyValue -Object $receiptByPath[$ownedPath] -Name 'sha256')
        if ((Get-LhmFileSha256 -Path $ownedPath) -ine $expectedHash) {
            throw "Receipt-owned generated file is modified: '$ownedPath'."
        }
    }

    $shimRecords = @($files | Where-Object {
        Test-LhmPathEqual `
            -Left ([string](Get-JsonPropertyValue -Object $_ -Name 'path')) `
            -Right $Paths.IntegrationShimPath
    })
    if ($shimRecords.Count -gt 1) {
        throw 'SoleX extensions receipt contains duplicate LibreHW shim records.'
    }
    $shimExists = Test-Path -LiteralPath $Paths.IntegrationShimPath
    if ($shimExists) {
        if (-not (Test-Path -LiteralPath $Paths.IntegrationShimPath -PathType Leaf) -or
            $shimRecords.Count -ne 1) {
            throw 'LibreHW SoleX shim exists without one safe receipt record.'
        }
        $expectedShimHash = [string](Get-JsonPropertyValue -Object $shimRecords[0] -Name 'sha256')
        if ((Get-LhmFileSha256 -Path $Paths.IntegrationShimPath) -ine $expectedShimHash) {
            throw 'Refusing to remove a modified LibreHW SoleX shim.'
        }
    }

    $isRetired = $integrationTargets.Count -eq 0 -and
        $integrationCommands.Count -eq 0 -and
        $integrationShortcuts.Count -eq 0 -and
        $shimRecords.Count -eq 0 -and
        -not $shimExists

    [pscustomobject]@{
        Catalog = $catalog
        Manifest = $manifest
        Receipt = $receipt
        Targets = $targets
        Commands = $commands
        Shortcuts = $shortcuts
        Files = $files
        TargetCount = $integrationTargets.Count
        CommandCount = $integrationCommands.Count
        ShortcutCount = $integrationShortcuts.Count
        ShimRecordCount = $shimRecords.Count
        ShimExists = $shimExists
        IsRetired = $isRetired
        EffectiveCatalogHash = Get-LhmFileSha256 -Path $Paths.EffectiveCatalogPath
        EffectiveManifestHash = Get-LhmFileSha256 -Path $Paths.EffectiveManifestPath
        ExtensionsReceiptHash = Get-LhmFileSha256 -Path $Paths.ExtensionsReceiptPath
        ShimHash = if ($shimExists) { Get-LhmFileSha256 -Path $Paths.IntegrationShimPath } else { $null }
    }
}

function ConvertTo-RetirementJsonBytes {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object] $Value)

    $json = ($Value | ConvertTo-Json -Depth 32) + [Environment]::NewLine
    return [Text.UTF8Encoding]::new($false).GetBytes($json)
}

function Get-ByteSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]] $Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function New-RetirementPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object] $Paths,
        [Parameter(Mandatory)][object] $State
    )

    $remainingTargets = @($State.Targets | Where-Object {
        [string](Get-JsonPropertyValue -Object $_ -Name 'key') -ine $script:IntegrationTarget
    })
    $remainingCommands = @($State.Commands | Where-Object {
        [string]$_ -ine $script:IntegrationTarget
    })
    $remainingShortcuts = @($State.Shortcuts | Where-Object {
        [string](Get-JsonPropertyValue -Object $_ -Name 'id') -ine $script:IntegrationTarget -and
        [string](Get-JsonPropertyValue -Object $_ -Name 'target') -ine $script:IntegrationTarget -and
        [string](Get-JsonPropertyValue -Object $_ -Name 'target') -ine $script:IntegrationAlias
    })
    $remainingFiles = @($State.Files | Where-Object {
        -not (Test-LhmPathEqual `
            -Left ([string](Get-JsonPropertyValue -Object $_ -Name 'path')) `
            -Right $Paths.IntegrationShimPath)
    })

    $State.Catalog.targets = $remainingTargets
    $State.Manifest.commands = $remainingCommands
    if ($null -ne $State.Manifest.PSObject.Properties['shortcuts']) {
        $State.Manifest.shortcuts = $remainingShortcuts
    }

    $catalogBytes = ConvertTo-RetirementJsonBytes -Value $State.Catalog
    $manifestBytes = ConvertTo-RetirementJsonBytes -Value $State.Manifest
    $catalogHash = Get-ByteSha256 -Bytes $catalogBytes
    $manifestHash = Get-ByteSha256 -Bytes $manifestBytes

    foreach ($record in $remainingFiles) {
        $recordPath = [string](Get-JsonPropertyValue -Object $record -Name 'path')
        if (Test-LhmPathEqual -Left $recordPath -Right $Paths.EffectiveCatalogPath) {
            $record.sha256 = $catalogHash.ToUpperInvariant()
        }
        elseif (Test-LhmPathEqual -Left $recordPath -Right $Paths.EffectiveManifestPath) {
            $record.sha256 = $manifestHash.ToUpperInvariant()
        }
    }
    $State.Receipt.files = $remainingFiles
    $receiptBytes = ConvertTo-RetirementJsonBytes -Value $State.Receipt

    [pscustomobject]@{
        CatalogBytes = $catalogBytes
        ManifestBytes = $manifestBytes
        ReceiptBytes = $receiptBytes
        CatalogHash = $catalogHash
        ManifestHash = $manifestHash
        ReceiptHash = Get-ByteSha256 -Bytes $receiptBytes
    }
}

function Write-NewFileBytes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][byte[]] $Bytes
    )

    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None,
        4096,
        [IO.FileOptions]::WriteThrough)
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }
}

function Write-AtomicJsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][object] $Value
    )

    $bytes = ConvertTo-RetirementJsonBytes -Value $Value
    $temporaryPath = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $replaceBackupPath = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.replace-backup'
    try {
        Write-NewFileBytes -Path $temporaryPath -Bytes $bytes
        if (Test-Path -LiteralPath $Path) {
            [IO.File]::Replace($temporaryPath, $Path, $replaceBackupPath, $true)
            Remove-Item -LiteralPath $replaceBackupPath -Force
        }
        else {
            [IO.File]::Move($temporaryPath, $Path)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $replaceBackupPath) {
            Remove-Item -LiteralPath $replaceBackupPath -Force
        }
    }
}

function Replace-ReceiptOwnedFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ExpectedHash,
        [Parameter(Mandatory)][byte[]] $Bytes,
        [Parameter(Mandatory)][string] $NewHash
    )

    if ((Get-LhmFileSha256 -Path $Path) -ine $ExpectedHash) {
        throw "Receipt-owned file changed after planning: '$Path'."
    }
    $temporaryPath = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $replaceBackupPath = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.replace-backup'
    try {
        Write-NewFileBytes -Path $temporaryPath -Bytes $Bytes
        if ((Get-LhmFileSha256 -Path $temporaryPath) -ine $NewHash) {
            throw "Staged retirement file hash mismatch: '$Path'."
        }
        [IO.File]::Replace($temporaryPath, $Path, $replaceBackupPath, $true)
        if ((Get-LhmFileSha256 -Path $Path) -ine $NewHash) {
            throw "Retirement file hash mismatch after replacement: '$Path'."
        }
        Remove-Item -LiteralPath $replaceBackupPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $replaceBackupPath) {
            Remove-Item -LiteralPath $replaceBackupPath -Force
        }
    }
}

function Assert-RetirementIdentity {
    [CmdletBinding()]
    param()

    if (-not $NonLiveTestMode) {
        Assert-LhmVerifiedMachineIdentity | Out-Null
        return
    }
    $identityPath = Assert-LhmNormalFile `
        -Path $TestIdentityVerifierPath `
        -Label 'Test identity verifier'
    $identity = @(& $identityPath)
    if ($identity.Count -ne 1 -or
        [string]$identity[0].status -cne 'VERIFIED' -or
        [string]$identity[0].machineId -cne $script:LhmExpectedMachineId -or
        [string]$identity[0].instanceId -cne $script:LhmExpectedInstanceId) {
        throw 'Retirement identity verification failed.'
    }
}

function Restore-RetirementBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object] $Paths,
        [Parameter(Mandatory)][object] $State,
        [Parameter(Mandatory)][string] $PacketRoot
    )

    $restoreMap = @(
        @{ Path = $Paths.EffectiveCatalogPath; Backup = Join-Path $PacketRoot 'solex.effective.json'; Hash = $State.EffectiveCatalogHash },
        @{ Path = $Paths.EffectiveManifestPath; Backup = Join-Path $PacketRoot 'solex.commands.json'; Hash = $State.EffectiveManifestHash },
        @{ Path = $Paths.ExtensionsReceiptPath; Backup = Join-Path $PacketRoot '.solex-extensions.json'; Hash = $State.ExtensionsReceiptHash }
    )
    foreach ($entry in $restoreMap) {
        if ((Get-LhmFileSha256 -Path $entry.Backup) -ine $entry.Hash) {
            throw "Retirement rollback backup hash mismatch: '$($entry.Backup)'."
        }
        $bytes = [IO.File]::ReadAllBytes($entry.Backup)
        $currentHash = Get-LhmFileSha256 -Path $entry.Path
        Replace-ReceiptOwnedFile `
            -Path $entry.Path `
            -ExpectedHash $currentHash `
            -Bytes $bytes `
            -NewHash $entry.Hash
    }
    if ($State.ShimExists) {
        $shimBackup = Join-Path $PacketRoot ($script:IntegrationTarget + '.cmd')
        if (Test-Path -LiteralPath $Paths.IntegrationShimPath) {
            if ((Get-LhmFileSha256 -Path $Paths.IntegrationShimPath) -ine $State.ShimHash) {
                throw 'Retirement rollback refused to overwrite a changed LibreHW SoleX shim.'
            }
        }
        else {
            Copy-Item -LiteralPath $shimBackup -Destination $Paths.IntegrationShimPath
        }
    }
}

function Invoke-RetirementApply {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object] $Paths,
        [Parameter(Mandatory)][object] $State
    )

    $payload = New-RetirementPayload -Paths $Paths -State $State
    [IO.Directory]::CreateDirectory($Paths.RecoveryRoot) | Out-Null
    Assert-LhmNoReparsePathAncestry -Path $Paths.RecoveryRoot -Label 'SoleX retirement recovery root'
    $packetRoot = Join-Path $Paths.RecoveryRoot (
        'retirement-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
    [IO.Directory]::CreateDirectory($packetRoot) | Out-Null
    Assert-LhmNoReparsePathAncestry -Path $packetRoot -Label 'SoleX retirement recovery packet'

    $backups = @(
        @{ Source = $Paths.EffectiveCatalogPath; Name = 'solex.effective.json'; Hash = $State.EffectiveCatalogHash },
        @{ Source = $Paths.EffectiveManifestPath; Name = 'solex.commands.json'; Hash = $State.EffectiveManifestHash },
        @{ Source = $Paths.ExtensionsReceiptPath; Name = '.solex-extensions.json'; Hash = $State.ExtensionsReceiptHash }
    )
    if ($State.ShimExists) {
        $backups += @{ Source = $Paths.IntegrationShimPath; Name = $script:IntegrationTarget + '.cmd'; Hash = $State.ShimHash }
    }
    foreach ($backup in $backups) {
        $destination = Join-Path $packetRoot $backup.Name
        Copy-Item -LiteralPath $backup.Source -Destination $destination
        if ((Get-LhmFileSha256 -Path $destination) -ine $backup.Hash) {
            throw "Retirement recovery backup hash mismatch: '$destination'."
        }
    }

    $packet = [ordered]@{
        schema = $script:RetirementSchema
        status = 'Prepared'
        createdUtc = [DateTime]::UtcNow.ToString('o')
        target = $script:IntegrationTarget
        original = [ordered]@{
            effectiveCatalogSha256 = $State.EffectiveCatalogHash
            effectiveManifestSha256 = $State.EffectiveManifestHash
            extensionsReceiptSha256 = $State.ExtensionsReceiptHash
            shimExisted = $State.ShimExists
            shimSha256 = $State.ShimHash
        }
        retired = [ordered]@{
            effectiveCatalogSha256 = $payload.CatalogHash
            effectiveManifestSha256 = $payload.ManifestHash
            extensionsReceiptSha256 = $payload.ReceiptHash
        }
    }
    $packetPath = Join-Path $packetRoot 'recovery.json'
    Write-AtomicJsonFile -Path $packetPath -Value $packet

    try {
        Replace-ReceiptOwnedFile `
            -Path $Paths.EffectiveCatalogPath `
            -ExpectedHash $State.EffectiveCatalogHash `
            -Bytes $payload.CatalogBytes `
            -NewHash $payload.CatalogHash
        Replace-ReceiptOwnedFile `
            -Path $Paths.EffectiveManifestPath `
            -ExpectedHash $State.EffectiveManifestHash `
            -Bytes $payload.ManifestBytes `
            -NewHash $payload.ManifestHash
        if ($TestFailurePoint -ceq 'AfterGeneratedFiles') {
            throw 'Injected retirement failure after generated files.'
        }
        if ($State.ShimExists) {
            if ((Get-LhmFileSha256 -Path $Paths.IntegrationShimPath) -ine $State.ShimHash) {
                throw 'LibreHW SoleX shim changed after planning.'
            }
            Remove-Item -LiteralPath $Paths.IntegrationShimPath -Force
        }
        if ($TestFailurePoint -ceq 'AfterShimRemoval') {
            throw 'Injected retirement failure after shim removal.'
        }
        Replace-ReceiptOwnedFile `
            -Path $Paths.ExtensionsReceiptPath `
            -ExpectedHash $State.ExtensionsReceiptHash `
            -Bytes $payload.ReceiptBytes `
            -NewHash $payload.ReceiptHash

        $packet.status = 'Committed'
        $packet.committedUtc = [DateTime]::UtcNow.ToString('o')
        Write-AtomicJsonFile -Path $packetPath -Value $packet
    }
    catch {
        $applyError = $_
        try {
            Restore-RetirementBackup -Paths $Paths -State $State -PacketRoot $packetRoot
            $packet.status = 'RolledBack'
            $packet.rolledBackUtc = [DateTime]::UtcNow.ToString('o')
            Write-AtomicJsonFile -Path $packetPath -Value $packet
        }
        catch {
            throw "SoleX retirement failed: $($applyError.Exception.Message) Rollback also failed: $($_.Exception.Message) Recovery: '$packetRoot'."
        }
        throw $applyError
    }

    return $packetRoot
}

$paths = Resolve-RetirementPaths
$mutex = [Threading.Mutex]::new($false, $script:RetirementMutexName)
$mutexAcquired = $false
try {
    $mutexAcquired = $mutex.WaitOne([TimeSpan]::FromSeconds(10))
    if (-not $mutexAcquired) { throw 'Timed out waiting for the SoleX retirement lock.' }
    $state = Get-RetirementState -Paths $paths

    if ($Mode -ceq 'Validate') {
        if (-not $state.IsRetired) {
            throw 'LibreHW SoleX integration is not fully retired.'
        }
        [pscustomobject]@{ Status = 'Validated'; Target = $script:IntegrationTarget; Retired = $true }
        return
    }

    if ($Mode -ceq 'Plan') {
        [pscustomobject]@{
            Status = if ($state.IsRetired) { 'AlreadyRetired' } else { 'Planned' }
            Target = $script:IntegrationTarget
            TargetEntries = $state.TargetCount
            CommandEntries = $state.CommandCount
            ShortcutEntries = $state.ShortcutCount
            ShimRecords = $state.ShimRecordCount
            ShimExists = $state.ShimExists
            MutationRequired = -not $state.IsRetired
        }
        return
    }

    Assert-RetirementIdentity
    if ($state.IsRetired) {
        [pscustomobject]@{ Status = 'AlreadyRetired'; Target = $script:IntegrationTarget; MutationPerformed = $false }
        return
    }
    if ($PSCmdlet.ShouldProcess($paths.SoleXDataRoot, 'Retire LibreHW SoleX generated integration and receipt record')) {
        $packetRoot = Invoke-RetirementApply -Paths $paths -State $state
        $validated = Get-RetirementState -Paths $paths
        if (-not $validated.IsRetired) { throw 'Post-Apply LibreHW SoleX retirement validation failed.' }
        [pscustomobject]@{
            Status = 'Applied'
            Target = $script:IntegrationTarget
            MutationPerformed = $true
            RecoveryPacket = $packetRoot
        }
    }
}
finally {
    if ($mutexAcquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

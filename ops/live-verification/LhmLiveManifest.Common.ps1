Set-StrictMode -Version Latest

function Read-LhmJsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not [System.IO.File]::Exists($Path)) {
        throw "Manifest does not exist: $Path"
    }

    return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
}

function Get-LhmManifestValue {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string[]]$PropertyPath
    )

    $current = $Object
    foreach ($segment in $PropertyPath) {
        $propertyInfo = $current.PSObject.Properties[$segment]
        if ($null -eq $propertyInfo -or $null -eq $propertyInfo.Value) {
            throw "Manifest value is missing: $($PropertyPath -join '.')"
        }
        $current = $propertyInfo.Value
    }

    return $current
}

function Get-LhmNormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    while ($fullPath.Length -gt $pathRoot.Length -and
           ($fullPath.EndsWith([string][System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::Ordinal) -or
            $fullPath.EndsWith([string][System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::Ordinal))) {
        $fullPath = $fullPath.Substring(0, $fullPath.Length - 1)
    }

    return $fullPath
}

function Test-LhmPathEqual {
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )

    return [string]::Equals((Get-LhmNormalizedPath -Path $Left),
                            (Get-LhmNormalizedPath -Path $Right),
                            [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-LhmPathWithin {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $fullPath = Get-LhmNormalizedPath -Path $Path
    $fullParent = Get-LhmNormalizedPath -Path $Parent
    if ($fullPath.Equals($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $prefix = $fullParent + [System.IO.Path]::DirectorySeparatorChar
    return $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-LhmJsonContract {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$DocumentName,
        [Parameter(Mandatory)][string]$ExpectedSchema
    )

    $schema = [string](Get-LhmManifestValue -Object $Object -PropertyPath @('schema'))
    $version = Get-LhmManifestValue -Object $Object -PropertyPath @('version')
    if ($schema -cne $ExpectedSchema) {
        throw "$DocumentName schema is '$schema'; expected '$ExpectedSchema'."
    }
    if ([string]$version -cne '1') {
        throw "$DocumentName version is '$version'; expected '1'."
    }
}

function Get-LhmLiveManifestContract {
    param(
        [Parameter(Mandatory)][string]$StackRoot,
        [switch]$RequireCurrentComputer
    )

    $stackRootPath = Get-LhmNormalizedPath -Path $StackRoot
    $layout = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\layout.json')
    $consumers = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\consumers.json')
    $live = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\channels\live.json')

    Assert-LhmJsonContract -Object $layout -DocumentName 'layout.json' -ExpectedSchema 'sq.librehw-layout'
    Assert-LhmJsonContract -Object $consumers -DocumentName 'consumers.json' -ExpectedSchema 'sq.librehw-consumers'
    Assert-LhmJsonContract -Object $live -DocumentName 'channels\live.json' -ExpectedSchema 'sq.librehw-live-channel'

    $layoutMachineId = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('machineId'))
    $consumerMachineId = [string](Get-LhmManifestValue -Object $consumers -PropertyPath @('machineId'))
    $liveMachineId = [string](Get-LhmManifestValue -Object $live -PropertyPath @('machineId'))
    if ([string]::IsNullOrWhiteSpace($layoutMachineId) -or
        -not [string]::Equals($layoutMachineId, $consumerMachineId, [System.StringComparison]::Ordinal) -or
        -not [string]::Equals($layoutMachineId, $liveMachineId, [System.StringComparison]::Ordinal)) {
        throw "Manifest machineId values do not agree: layout='$layoutMachineId', consumers='$consumerMachineId', live='$liveMachineId'."
    }

    $declaredComputer = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('computerName'))
    if ($RequireCurrentComputer -and
        -not [string]::Equals($declaredComputer, $env:COMPUTERNAME, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest declares computer '$declaredComputer' but this machine is '$env:COMPUTERNAME'."
    }

    $declaredStackRoot = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'root'))
    if (-not (Test-LhmPathEqual -Left $declaredStackRoot -Right $stackRootPath)) {
        throw "layout.json operations.root '$declaredStackRoot' does not match StackRoot '$stackRootPath'."
    }

    $declaredLiveRoot = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'live'))
    $declaredDeploymentRoot = [string](Get-LhmManifestValue -Object $live -PropertyPath @('deploymentRoot'))
    if (-not (Test-LhmPathEqual -Left $declaredDeploymentRoot -Right $declaredLiveRoot)) {
        throw "channels\live.json deploymentRoot '$declaredDeploymentRoot' does not match layout.json operations.live '$declaredLiveRoot'."
    }

    $declaredEntryPoint = [string](Get-LhmManifestValue -Object $live -PropertyPath @('entryPoint'))
    if ([string]::IsNullOrWhiteSpace($declaredEntryPoint) -or
        [System.IO.Path]::IsPathRooted($declaredEntryPoint)) {
        throw 'channels\live.json entryPoint must be a non-empty relative path.'
    }
    $declaredEntryTarget = Get-LhmNormalizedPath -Path (Join-Path $declaredLiveRoot $declaredEntryPoint)
    if (-not (Test-LhmPathWithin -Path $declaredEntryTarget -Parent $declaredLiveRoot)) {
        throw "channels\live.json entryPoint escapes the declared live root: '$declaredEntryPoint'."
    }

    $declaredLogManagementRoot = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'logManagement'))
    $declaredLogTaskTarget = Get-LhmNormalizedPath -Path (
        Join-Path $declaredLogManagementRoot 'Invoke-LhmLogManagement.ps1')
    $scheduledTaskBindings = @((Get-LhmManifestValue -Object $consumers -PropertyPath @('bindings')) |
        Where-Object { $_.kind -eq 'scheduledTask' })
    $liveTargetCount = 0
    $logTargetCount = 0
    $observedTaskTargets = @()
    foreach ($binding in $scheduledTaskBindings) {
        $bindingTarget = Get-LhmNormalizedPath -Path ([string](
            Get-LhmManifestValue -Object $binding -PropertyPath @('target')))
        $observedTaskTargets += $bindingTarget
        if (Test-LhmPathEqual -Left $bindingTarget -Right $declaredEntryTarget) {
            $liveTargetCount++
        }
        if (Test-LhmPathEqual -Left $bindingTarget -Right $declaredLogTaskTarget) {
            $logTargetCount++
        }
    }
    if ($scheduledTaskBindings.Count -ne 2 -or
        $liveTargetCount -ne 1 -or
        $logTargetCount -ne 1) {
        throw ("consumers.json scheduledTask bindings must contain exactly the declared live target '$declaredEntryTarget' " +
               "and log-management target '$declaredLogTaskTarget'; found $($scheduledTaskBindings.Count) binding(s): " +
               ($observedTaskTargets -join ', '))
    }

    return [pscustomobject][ordered]@{
        StackRoot = $stackRootPath
        Layout = $layout
        Consumers = $consumers
        Live = $live
        MachineId = $layoutMachineId
        ComputerName = $declaredComputer
        LiveRoot = Get-LhmNormalizedPath -Path $declaredLiveRoot
        EntryPoint = $declaredEntryPoint
        EntryTarget = $declaredEntryTarget
        LogManagementRoot = Get-LhmNormalizedPath -Path $declaredLogManagementRoot
        LogTaskTarget = $declaredLogTaskTarget
        ScheduledTaskBindings = [object[]]@($scheduledTaskBindings)
    }
}

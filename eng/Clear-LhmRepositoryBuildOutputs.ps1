[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\ops\candidate\LhmRelease.Common.ps1')

if (-not $PSBoundParameters.ContainsKey('RepositoryRoot')) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}
elseif ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    throw 'RepositoryRoot must not be empty.'
}

$repositoryPath = Resolve-LhmReleaseFullPath -Path $RepositoryRoot
$volumeRoot = [System.IO.Path]::GetPathRoot($repositoryPath)
if ([string]::Equals(
    $repositoryPath,
    $volumeRoot,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RepositoryRoot must not be a filesystem root: '$repositoryPath'."
}
if (-not (Test-Path -LiteralPath $repositoryPath -PathType Container)) {
    throw "Repository root does not exist: '$repositoryPath'."
}

$repositoryItem = Get-Item -LiteralPath $repositoryPath -Force
if ($repositoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
    throw "Repository root must not be a reparse point: '$repositoryPath'."
}

$repositoryMarkers = @(
    'LibreHardwareMonitor.sln',
    'Directory.Build.props',
    'Directory.Packages.props'
)
foreach ($marker in $repositoryMarkers) {
    $markerPath = Join-Path $repositoryPath $marker
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw (
            "RepositoryRoot does not match the LibreHardwareMonitor source " +
            "layout; missing '$markerPath'."
        )
    }
}

$outputPaths = @(Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $repositoryPath)
$repositoryPrefix = Get-LhmReleasePathPrefix -Path $repositoryPath
foreach ($outputPath in @($outputPaths | Where-Object { [System.IO.Directory]::Exists($_) })) {
    $resolvedOutput = Assert-LhmRepositoryBuildOutputPath `
        -RepositoryRoot $repositoryPath `
        -Path $outputPath
    Assert-LhmDirectoryTreeHasNoReparsePoints -Path $resolvedOutput
    $runtimeState = @(Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse -Force | Where-Object {
        $_.Name -like 'LibreHardwareMonitorLog-*.csv' -or
        $_.Name -ieq 'LibreHardwareMonitor.Windows.Forms.config' -or
        $_.Name -ieq 'LibreHardwareMonitor.Windows.Forms.config.backup'
    })
    if ($runtimeState.Count -ne 0) {
        $examples = ($runtimeState | Select-Object -First 3 -ExpandProperty FullName) -join '; '
        throw (
            "Refusing to remove '$resolvedOutput': found $($runtimeState.Count) " +
            "runtime-state file(s). Preserve them outside the repository first. " +
            "Examples: $examples"
        )
    }
}

$cleanupParameters = @{
    RepositoryRoot = $repositoryPath
    WhatIf = [bool]$WhatIfPreference
}
if ($PSBoundParameters.ContainsKey('Confirm')) {
    $cleanupParameters.Confirm = [bool]$PSBoundParameters['Confirm']
}
$summaries = @(Clear-LhmRepositoryBuildOutput @cleanupParameters)
$summaryByPath = @{}
foreach ($summary in $summaries) {
    $summaryByPath[[string]$summary.Path] = $summary
}

$inventory = foreach ($outputPath in $outputPaths) {
    $relativePath = $outputPath.Substring($repositoryPrefix.Length)
    if ($summaryByPath.ContainsKey($outputPath)) {
        $summary = $summaryByPath[$outputPath]
        [pscustomobject]@{
            Path = $relativePath
            Files = [int]$summary.Files
            Bytes = [long]$summary.Bytes
            Exes = [int]$summary.Exes
            Status = if ($WhatIfPreference) { 'Planned' } else { 'Removed' }
        }
    }
    else {
        [pscustomobject]@{
            Path = $relativePath
            Files = 0
            Bytes = [long]0
            Exes = 0
            Status = 'Absent'
        }
    }
}

$inventory

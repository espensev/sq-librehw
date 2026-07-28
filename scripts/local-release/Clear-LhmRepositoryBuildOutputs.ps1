[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-LhmCleanupPath {
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Path
    )

    if ($Path -notmatch '^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+(?:\\|$))') {
        throw "Cleanup paths must be absolute: '$Path'."
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $volumeRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals(
        $fullPath,
        $volumeRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath
    }

    return $fullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-LhmCleanupChildPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $prefix = $Parent + [System.IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-LhmCleanupNormalAncestry {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $current = $Path
    while (-not [string]::Equals(
        $current,
        $Parent,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        if (-not (Test-LhmCleanupChildPath -Path $current -Parent $Parent)) {
            throw "Cleanup ancestry escaped the repository: '$current'."
        }

        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "Cleanup ancestry contains a reparse point: '$current'."
            }
        }

        $parentInfo = [System.IO.Directory]::GetParent($current)
        if ($null -eq $parentInfo) {
            throw "Unable to resolve cleanup ancestry for '$Path'."
        }
        $current = Resolve-LhmCleanupPath -Path $parentInfo.FullName
    }
}

function Get-LhmCleanupNormalTreeFiles {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    $pending = [System.Collections.Generic.Queue[string]]::new()
    $files = [System.Collections.Generic.List[object]]::new()
    $pending.Enqueue($Root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "Cleanup target contains a reparse point: '$($entry.FullName)'."
            }
            if ($entry.PSIsContainer) {
                $pending.Enqueue($entry.FullName)
            }
            else {
                $files.Add($entry)
            }
        }
    }

    return $files
}

if (-not $PSBoundParameters.ContainsKey('RepositoryRoot')) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..\..'
}
elseif ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    throw 'RepositoryRoot must not be empty.'
}

$repositoryPath = Resolve-LhmCleanupPath -Path $RepositoryRoot
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

# Deliberately explicit. New projects do not inherit destructive cleanup merely
# because they happen to create a directory named bin or obj.
$relativeOutputRoots = @(
    'bin',
    'Aga.Controls\bin',
    'Aga.Controls\obj',
    'LibreHardwareMonitor.Tests\bin',
    'LibreHardwareMonitor.Tests\obj',
    'LibreHardwareMonitor.Windows.Forms\bin',
    'LibreHardwareMonitor.Windows.Forms\obj',
    'LibreHardwareMonitor\bin',
    'LibreHardwareMonitor\obj',
    'LibreHardwareMonitorLib\bin',
    'LibreHardwareMonitorLib\obj'
)

$inventory = [System.Collections.Generic.List[object]]::new()
foreach ($relativePath in $relativeOutputRoots) {
    $outputPath = Resolve-LhmCleanupPath -Path (Join-Path $repositoryPath $relativePath)
    if (-not (Test-LhmCleanupChildPath -Path $outputPath -Parent $repositoryPath)) {
        throw "Refusing output path outside the repository: '$outputPath'."
    }
    Assert-LhmCleanupNormalAncestry `
        -Path $outputPath `
        -Parent $repositoryPath

    $leaf = Split-Path -Leaf $outputPath
    if ($leaf -notin @('bin', 'obj')) {
        throw "Cleanup target is not an exact bin/obj directory: '$outputPath'."
    }

    if (-not (Test-Path -LiteralPath $outputPath)) {
        $inventory.Add([pscustomobject]@{
            Path = $relativePath
            Files = 0
            Bytes = [long]0
            Exes = 0
            Status = 'Absent'
        })
        continue
    }
    if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) {
        throw "Cleanup target is not a directory: '$outputPath'."
    }

    $outputItem = Get-Item -LiteralPath $outputPath -Force
    if ($outputItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw "Cleanup target must not be a reparse point: '$outputPath'."
    }

    # Walk one directory at a time so Windows PowerShell 5.1 never follows a
    # child junction before the reparse-point check can reject it.
    $files = @(Get-LhmCleanupNormalTreeFiles -Root $outputPath)
    $runtimeState = @($files | Where-Object {
        $_.Name -like 'LibreHardwareMonitorLog-*.csv' -or
        $_.Name -ieq 'LibreHardwareMonitor.Windows.Forms.config' -or
        $_.Name -ieq 'LibreHardwareMonitor.Windows.Forms.config.backup'
    })
    if ($runtimeState.Count -ne 0) {
        $examples = ($runtimeState | Select-Object -First 3 -ExpandProperty FullName) -join '; '
        throw (
            "Refusing to remove '$outputPath': found $($runtimeState.Count) " +
            "runtime-state file(s). Preserve them outside the repository first. " +
            "Examples: $examples"
        )
    }

    $fileBytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
    $exeCount = @($files | Where-Object Extension -ieq '.exe').Count
    $status = 'Planned'
    if ($PSCmdlet.ShouldProcess($outputPath, 'Remove disposable repository build output')) {
        Remove-Item -LiteralPath $outputPath -Recurse -Force
        if (Test-Path -LiteralPath $outputPath) {
            throw "Cleanup target still exists after removal: '$outputPath'."
        }
        $status = 'Removed'
    }

    $inventory.Add([pscustomobject]@{
        Path = $relativePath
        Files = $files.Count
        Bytes = $fileBytes
        Exes = $exeCount
        Status = $status
    })
}

$inventory

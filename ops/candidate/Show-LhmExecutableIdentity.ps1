[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Path,

    [string]$ReleaseRoot,

    [string]$StackRoot,

    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmRelease.Common.ps1')

function Test-LhmIdentityPathWithin {
    param(
        [Parameter(Mandatory)][string]$Candidate,
        [Parameter(Mandatory)][string]$Parent
    )

    $candidatePath = Resolve-LhmReleaseFullPath -Path $Candidate
    $parentPath = Resolve-LhmReleaseFullPath -Path $Parent
    return $candidatePath.Equals(
        $parentPath,
        [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-LhmReleasePathWithin -Path $candidatePath -Parent $parentPath)
}

function Get-LhmIdentityCheckout {
    param([Parameter(Mandatory)][string]$ExecutablePath)

    $directory = [System.IO.Path]::GetDirectoryName($ExecutablePath)
    $previousErrorPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 promotes native stderr to a terminating error
        # under Stop. A path outside Git is an expected classification result.
        $ErrorActionPreference = 'Continue'
        $rootOutput = @(& git -C $directory rev-parse --show-toplevel 2>$null)
        $rootExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }
    if ($rootExitCode -ne 0 -or $rootOutput.Count -ne 1) {
        return $null
    }

    $root = Resolve-LhmReleaseFullPath -Path ([string]$rootOutput[0])
    $commitOutput = @(& git -C $root rev-parse HEAD 2>$null)
    $branchOutput = @(& git -C $root rev-parse --abbrev-ref HEAD 2>$null)
    $changes = @(& git -C $root status --porcelain=v1 --untracked-files=no 2>$null)
    if ($LASTEXITCODE -ne 0 -or
        $commitOutput.Count -ne 1 -or
        $branchOutput.Count -ne 1) {
        throw "Unable to inspect the Git checkout containing '$ExecutablePath'."
    }

    return [ordered]@{
        root = $root
        commit = ([string]$commitOutput[0]).Trim()
        branch = ([string]$branchOutput[0]).Trim()
        trackedDirty = $changes.Count -gt 0
    }
}

function Get-LhmIdentityCandidateMatches {
    param(
        [Parameter(Mandatory)][string]$ExecutableSha256,
        [string]$CandidateReleaseRoot
    )

    $matches = [System.Collections.Generic.List[object]]::new()
    if ([string]::IsNullOrWhiteSpace($CandidateReleaseRoot)) {
        return @()
    }

    $root = Resolve-LhmReleaseFullPath -Path $CandidateReleaseRoot
    Assert-LhmReleasePathHasNoReparseAncestor -Path $root
    $candidateRoot = Join-Path $root 'candidates'
    if (-not [System.IO.Directory]::Exists($candidateRoot)) {
        return @()
    }

    foreach ($directory in @(Get-ChildItem -LiteralPath $candidateRoot -Directory -Force)) {
        Assert-LhmReleasePathHasNoReparseAncestor -Path $directory.FullName
        $manifestPath = Join-Path $directory.FullName 'release-manifest.json'
        if (-not [System.IO.File]::Exists($manifestPath)) {
            continue
        }
        $manifestItem = Get-Item -LiteralPath $manifestPath -Force
        if ($manifestItem.Length -gt 4MB) {
            continue
        }

        $manifest = ConvertFrom-LhmReleaseJson -Json (
            Get-Content -Raw -LiteralPath $manifestPath)
        foreach ($package in @($manifest.packages)) {
            $entryPoint = [string]$package.entryPoint
            $entryRecords = @($package.files | Where-Object {
                [string]$_.path -ceq $entryPoint
            })
            if ($entryRecords.Count -ne 1 -or
                ([string]$entryRecords[0].sha256).ToLowerInvariant() -cne $ExecutableSha256) {
                continue
            }

            $validationStatus = 'INVALID'
            $validationError = $null
            try {
                $validation = Test-LhmReleaseCandidate -CandidatePath $directory.FullName
                $validationStatus = [string]$validation.Status
            }
            catch {
                $validationError = $_.Exception.Message
            }

            $matches.Add([ordered]@{
                releaseId = [string]$manifest.releaseId
                targetFramework = [string]$package.targetFramework
                sourceCommit = [string]$manifest.source.commit
                promotable = [bool]$manifest.promotable
                archive = [string]$package.archive
                validationStatus = $validationStatus
                validationError = $validationError
            })
        }
    }

    return @($matches)
}

$executablePath = Resolve-LhmReleaseFullPath -Path $Path
Assert-LhmReleasePathHasNoReparseAncestor -Path $executablePath
if (-not [System.IO.File]::Exists($executablePath)) {
    throw "Executable does not exist: $executablePath"
}
if ([System.IO.Path]::GetFileName($executablePath) -cne
    'LibreHardwareMonitor.Windows.Forms.exe') {
    throw "Expected LibreHardwareMonitor.Windows.Forms.exe, got: $executablePath"
}

if ([string]::IsNullOrWhiteSpace($ReleaseRoot) -and
    -not [string]::IsNullOrWhiteSpace($StackRoot)) {
    $ReleaseRoot = Join-Path $StackRoot 'releases'
}

$sha256 = Get-LhmReleaseFileSha256 -Path $executablePath
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
$fileVersion = [string]$versionInfo.FileVersion
$productVersion = [string]$versionInfo.ProductVersion
$checkout = Get-LhmIdentityCheckout -ExecutablePath $executablePath
$candidateMatches = @(Get-LhmIdentityCandidateMatches `
    -ExecutableSha256 $sha256 `
    -CandidateReleaseRoot $ReleaseRoot)

$buildCommit = $null
$buildDirty = $null
$versionMatch = [System.Text.RegularExpressions.Regex]::Match(
    $productVersion,
    '\+(?<commit>[0-9a-fA-F]{7}|nogit)(?<dirty>-dirty)?(?:\.|$)')
if ($versionMatch.Success) {
    $buildCommit = $versionMatch.Groups['commit'].Value.ToLowerInvariant()
    $buildDirty = $versionMatch.Groups['dirty'].Success
}

$matchesCheckoutCommit = $null
if ($null -ne $checkout -and
    -not [string]::IsNullOrWhiteSpace($buildCommit) -and
    $buildCommit -cne 'nogit') {
    $matchesCheckoutCommit = ([string]$checkout.commit).StartsWith(
        $buildCommit,
        [System.StringComparison]::OrdinalIgnoreCase)
}

$role = 'unknown'
$authority = 'none'
$verifiedCandidateMatches = @($candidateMatches | Where-Object {
    $_.validationStatus -ceq 'VERIFIED'
})
if ($verifiedCandidateMatches.Count -gt 0) {
    $role = 'candidate-payload'
    $authority = 'candidate-manifest'
}
elseif ($candidateMatches.Count -gt 0) {
    $role = 'candidate-payload-invalid'
    $authority = 'candidate-manifest-invalid'
}

if ($null -ne $checkout) {
    $relative = $executablePath.Substring(([string]$checkout.root).Length).TrimStart(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
    if ($relative -match '^LibreHardwareMonitor\.Tests/.+/(?:bin|obj)/') {
        $role = 'test-copy'
        $authority = 'none'
    }
    elseif ($relative -match '(^|/)obj/') {
        $role = 'intermediate-output'
        $authority = 'none'
    }
    elseif ($relative -match '^bin/') {
        $role = 'development-build'
        $authority = 'none'
    }
    elseif ($role -eq 'unknown') {
        $role = 'checkout-copy'
    }
}

$liveManifestMatch = $false
$sourceCandidate = $null
if (-not [string]::IsNullOrWhiteSpace($StackRoot)) {
    $stack = Resolve-LhmReleaseFullPath -Path $StackRoot
    Assert-LhmReleasePathHasNoReparseAncestor -Path $stack
    $liveManifestPath = Join-Path $stack 'manifests\channels\live.json'
    if ([System.IO.File]::Exists($liveManifestPath)) {
        $live = ConvertFrom-LhmReleaseJson -Json (
            Get-Content -Raw -LiteralPath $liveManifestPath)
        $livePath = Resolve-LhmReleaseFullPath -Path (
            Join-Path ([string]$live.deploymentRoot) ([string]$live.entryPoint))
        if ($executablePath.Equals(
            $livePath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            $sourceCandidate = [string]$live.sourceCandidate
            $liveManifestMatch = $sha256 -ceq ([string]$live.sha256).ToLowerInvariant() -and
                $productVersion -ceq [string]$live.productVersion
            $role = if ($liveManifestMatch) { 'live' } else { 'live-mismatch' }
            $authority = 'live-manifest'
        }
    }

    if ($role -notin @('live', 'live-mismatch')) {
        $rollbackRoot = Join-Path $stack 'deployments\rollback'
        if ([System.IO.Directory]::Exists($rollbackRoot) -and
            (Test-LhmIdentityPathWithin -Candidate $executablePath -Parent $rollbackRoot)) {
            $role = 'rollback-payload'
            $authority = 'rollback-packet'
        }
    }
}

$processIds = @()
try {
    $processIds = @(
        Get-CimInstance Win32_Process -Property ProcessId, ExecutablePath -ErrorAction Stop |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
                (Resolve-LhmReleaseFullPath -Path ([string]$_.ExecutablePath)).Equals(
                    $executablePath,
                    [System.StringComparison]::OrdinalIgnoreCase)
            } |
            ForEach-Object { [int]$_.ProcessId }
    )
}
catch {
    $processIds = @()
}

$result = [ordered]@{
    schema = 'sq.lhm-executable-identity'
    version = 1
    status = 'IDENTIFIED'
    path = $executablePath
    role = $role
    authority = $authority
    sha256 = $sha256
    fileVersion = $fileVersion
    productVersion = $productVersion
    buildCommit = $buildCommit
    buildDirty = $buildDirty
    checkout = $checkout
    matchesCheckoutCommit = $matchesCheckoutCommit
    candidateMatches = [object[]]@($candidateMatches)
    liveManifestMatch = $liveManifestMatch
    sourceCandidate = $sourceCandidate
    runningProcessIds = [int[]]@($processIds)
}

if ($Json) {
    $result | ConvertTo-Json -Depth 10
}
else {
    [pscustomobject]$result
}

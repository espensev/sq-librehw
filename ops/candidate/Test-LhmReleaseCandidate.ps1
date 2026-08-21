[CmdletBinding(DefaultParameterSetName = 'Path')]
param(
    [Parameter(ParameterSetName = 'Path')]
    [string]$CandidatePath,

    [Parameter(ParameterSetName = 'Latest')]
    [switch]$Latest,

    [string]$ReleaseRoot,

    [switch]$RequirePromotable,

    [switch]$RequireCurrentSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmRelease.Common.ps1')

$repositoryRoot = Resolve-LhmReleaseFullPath -Path (Join-Path $PSScriptRoot '..\..')
if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:LHM_RELEASE_ROOT)) {
        $ReleaseRoot = $env:LHM_RELEASE_ROOT
    }
    else {
        $ReleaseRoot = Get-LhmDefaultReleaseRoot -RepositoryRoot $repositoryRoot
    }
}
$releaseRootPath = Assert-LhmDisjointReleaseRoot -RepositoryRoot $repositoryRoot -ReleaseRoot $ReleaseRoot

if ($Latest -or [string]::IsNullOrWhiteSpace($CandidatePath)) {
    $candidateRoot = Join-Path $releaseRootPath 'candidates'
    if (-not [System.IO.Directory]::Exists($candidateRoot)) {
        throw "Release candidate root does not exist: $candidateRoot"
    }

    $candidateSummaries = @(
        Get-ChildItem -LiteralPath $candidateRoot -Directory -Force |
            Where-Object {
                [System.IO.File]::Exists((Join-Path $_.FullName 'release-manifest.json'))
            } |
            ForEach-Object {
                Assert-LhmReleasePathHasNoReparseAncestor -Path $_.FullName
                $manifestPath = Join-Path $_.FullName 'release-manifest.json'
                $manifestItem = Get-Item -LiteralPath $manifestPath -Force
                if ($manifestItem.Length -gt 4MB) {
                    throw "Release manifest exceeds the 4 MiB selection limit: $manifestPath"
                }
                $manifest = ConvertFrom-LhmReleaseJson -Json (
                    Get-Content -Raw -LiteralPath $manifestPath)
                Assert-LhmReleaseObjectProperty -Object $manifest -Name 'createdUtc'
                Assert-LhmReleaseJsonString -Value $manifest.createdUtc -Name 'createdUtc'
                $createdUtc = [System.DateTimeOffset]::MinValue
                if (-not [System.DateTimeOffset]::TryParse(
                    [string]$manifest.createdUtc,
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    [System.Globalization.DateTimeStyles]::RoundtripKind,
                    [ref]$createdUtc) -or
                    $createdUtc.Offset -ne [System.TimeSpan]::Zero) {
                    throw "Release manifest has an invalid UTC creation time: $manifestPath"
                }
                [pscustomobject]@{
                    Directory = $_
                    CreatedUtc = $createdUtc.UtcDateTime
                }
            }
    )
    $candidate = $candidateSummaries |
        Sort-Object -Property @(
            @{ Expression = 'CreatedUtc'; Descending = $true },
            @{ Expression = { $_.Directory.Name }; Descending = $true }) |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "No complete release candidate exists under: $candidateRoot"
    }
    $CandidatePath = $candidate.Directory.FullName
}

$resolvedCandidate = Resolve-LhmReleaseFullPath -Path $CandidatePath
$candidateRootPath = Join-Path $releaseRootPath 'candidates'
if (-not (Test-LhmReleasePathWithin -Path $resolvedCandidate -Parent $candidateRootPath)) {
    throw "Candidate must be inside the configured external candidate root: $resolvedCandidate"
}

$verification = Test-LhmReleaseCandidate -CandidatePath $resolvedCandidate
if ($RequirePromotable -and -not $verification.Promotable) {
    throw "Release candidate is verified but not promotable: $resolvedCandidate"
}

if ($RequireCurrentSource) {
    $gitTopLevelOutput = @(& git -C $repositoryRoot rev-parse --show-toplevel)
    if ($LASTEXITCODE -ne 0 -or $gitTopLevelOutput.Count -ne 1) {
        throw 'Unable to resolve the current repository root.'
    }
    $gitTopLevel = Resolve-LhmReleaseFullPath -Path ([string]$gitTopLevelOutput[0])
    if (-not $gitTopLevel.Equals($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Candidate verifier root is not the Git top level: $repositoryRoot"
    }

    $currentCommit = @(& git -C $repositoryRoot rev-parse HEAD)
    $currentBranch = @(& git -C $repositoryRoot rev-parse --abbrev-ref HEAD)
    $currentChanges = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or
        $currentCommit.Count -ne 1 -or
        $currentBranch.Count -ne 1) {
        throw 'Unable to resolve current repository source state.'
    }
    $currentFingerprint = Get-LhmGitSourceFingerprint -RepositoryRoot $repositoryRoot
    if (([string]$currentCommit[0]).Trim() -cne $verification.SourceCommit -or
        ([string]$currentBranch[0]).Trim() -cne $verification.SourceBranch -or
        [string]::Join("`0", $currentChanges) -cne
            [string]::Join("`0", [string[]]$verification.SourceChanges) -or
        $currentFingerprint.Sha256 -cne $verification.SourceFingerprintSha256 -or
        $currentFingerprint.FileCount -ne $verification.SourceFileCount) {
        throw 'Release candidate does not match the current repository source state.'
    }
}

Write-Output $verification

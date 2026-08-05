[CmdletBinding()]
param(
    [string]$ReleaseRoot,
    [switch]$AllowDirty,
    [switch]$SkipVerification,
    [string]$ReleaseId,
    [string]$DotnetPath = 'dotnet',
    [string]$NodePath = 'node'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmRelease.Common.ps1')

$repositoryRoot = Resolve-LhmReleaseFullPath -Path (Join-Path $PSScriptRoot '..\..')
if (-not [System.IO.Directory]::Exists((Join-Path $repositoryRoot '.git')) -and
    -not [System.IO.File]::Exists((Join-Path $repositoryRoot '.git'))) {
    throw "Release script is not running from a Git checkout: $repositoryRoot"
}

if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:LHM_RELEASE_ROOT)) {
        $ReleaseRoot = $env:LHM_RELEASE_ROOT
    }
    else {
        $ReleaseRoot = Get-LhmDefaultReleaseRoot -RepositoryRoot $repositoryRoot
    }
}
$releaseRootPath = Assert-LhmDisjointReleaseRoot -RepositoryRoot $repositoryRoot -ReleaseRoot $ReleaseRoot

function Get-GitScalar {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = @(& git -C $repositoryRoot @Arguments)
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
        throw "Git command did not return one value: git $($Arguments -join ' ')"
    }
    return ([string]$output[0]).Trim()
}

function Assert-ReleaseSourceUnchanged {
    $currentCommit = Get-GitScalar -Arguments @('rev-parse', 'HEAD')
    $currentBranch = Get-GitScalar -Arguments @('rev-parse', '--abbrev-ref', 'HEAD')
    $currentChanges = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to recheck repository dirty state before candidate publication.'
    }
    $currentFingerprint = Get-LhmGitSourceFingerprint -RepositoryRoot $repositoryRoot
    if ($currentCommit -cne $sourceCommit -or
        $currentBranch -cne $sourceBranch -or
        [string]::Join("`0", $currentChanges) -cne [string]::Join("`0", $sourceChanges) -or
        $currentFingerprint.Sha256 -cne $sourceFingerprint.Sha256 -or
        $currentFingerprint.FileCount -ne $sourceFingerprint.FileCount) {
        throw 'Repository source changed during the release run; no candidate was published.'
    }
}

$gitTopLevel = Resolve-LhmReleaseFullPath -Path (Get-GitScalar -Arguments @('rev-parse', '--show-toplevel'))
if (-not $gitTopLevel.Equals($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release script root is not the Git top level. Expected '$repositoryRoot', got '$gitTopLevel'."
}

$sourceCommit = Get-GitScalar -Arguments @('rev-parse', 'HEAD')
$sourceShortCommit = Get-GitScalar -Arguments @('rev-parse', '--short=7', 'HEAD')
$sourceBranch = Get-GitScalar -Arguments @('rev-parse', '--abbrev-ref', 'HEAD')
$sourceChanges = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to determine repository dirty state.'
}
$sourceFingerprint = Get-LhmGitSourceFingerprint -RepositoryRoot $repositoryRoot
$originOutput = @(& git -C $repositoryRoot config --get remote.origin.url)
$originIdentifier = if ($LASTEXITCODE -eq 0 -and $originOutput.Count -eq 1) {
    ConvertTo-LhmSafeRepositoryIdentifier -RemoteUrl ([string]$originOutput[0])
}
else {
    $null
}
$isDirty = $sourceChanges.Count -gt 0
if ($isDirty -and -not $AllowDirty) {
    throw 'The repository is dirty. Commit/stash it for a promotable release, or pass -AllowDirty for a non-promotable development candidate.'
}
if ((-not $isDirty) -and
    (-not $SkipVerification) -and
    [string]::IsNullOrWhiteSpace([string]$originIdentifier)) {
    throw 'A promotable release requires a credential-safe remote.origin.url identifier.'
}

[xml]$buildProperties = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props')
$versionNode = $buildProperties.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'Directory.Build.props does not contain one product Version.'
}
$baseVersion = $versionNode.InnerText.Trim()
$createdUtc = [datetime]::UtcNow
if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $dirtySuffix = if ($isDirty) { '-dirty' } else { '' }
    $ReleaseId = '{0}-{1}-{2}{3}' -f
        $baseVersion,
        $createdUtc.ToString('yyyyMMdd-HHmmssfff', [System.Globalization.CultureInfo]::InvariantCulture),
        $sourceShortCommit,
        $dirtySuffix
}
Assert-LhmSafeReleaseId -ReleaseId $ReleaseId
$releaseIdHasDirtySuffix = $ReleaseId.EndsWith(
    '-dirty',
    [System.StringComparison]::Ordinal)
if ($isDirty -ne $releaseIdHasDirtySuffix) {
    throw 'ReleaseId must end in -dirty exactly when the source checkout is dirty.'
}

$globalJsonPath = Join-Path $repositoryRoot 'global.json'
if (-not [System.IO.File]::Exists($globalJsonPath)) {
    throw "Release SDK policy is missing: $globalJsonPath"
}
$globalJson = ConvertFrom-LhmReleaseJson -Json (
    Get-Content -Raw -LiteralPath $globalJsonPath)
if ($null -eq $globalJson.sdk -or
    [string]::IsNullOrWhiteSpace([string]$globalJson.sdk.version)) {
    throw 'global.json does not pin a release SDK version.'
}
$pinnedSdkVersion = [string]$globalJson.sdk.version

$candidateRoot = Join-Path $releaseRootPath 'candidates'
$stagingParent = Join-Path $releaseRootPath '.staging'
$finalCandidate = Resolve-LhmReleaseFullPath -Path (Join-Path $candidateRoot $ReleaseId)
Assert-LhmReleasePathHasNoReparseAncestor -Path $candidateRoot
Assert-LhmReleasePathHasNoReparseAncestor -Path $stagingParent
Assert-LhmReleasePathHasNoReparseAncestor -Path $finalCandidate
if ([System.IO.Directory]::Exists($finalCandidate) -or [System.IO.File]::Exists($finalCandidate)) {
    throw "Release candidate already exists and is immutable: $finalCandidate"
}

[System.IO.Directory]::CreateDirectory($candidateRoot) | Out-Null
[System.IO.Directory]::CreateDirectory($stagingParent) | Out-Null
Assert-LhmReleasePathHasNoReparseAncestor -Path $candidateRoot
Assert-LhmReleasePathHasNoReparseAncestor -Path $stagingParent
$runRoot = Resolve-LhmReleaseFullPath -Path (
    Join-Path $stagingParent ($ReleaseId + '-' + [guid]::NewGuid().ToString('N')))
if (-not (Test-LhmReleasePathWithin -Path $runRoot -Parent $stagingParent)) {
    throw "Generated staging path escaped its parent: $runRoot"
}

$partialCandidate = Join-Path $runRoot $ReleaseId
$packageDirectory = Join-Path $partialCandidate 'packages'
$artifactDirectory = Join-Path $runRoot 'artifacts'
$readyCandidate = Resolve-LhmReleaseFullPath -Path (Join-Path $stagingParent $ReleaseId)
if (-not (Test-LhmReleasePathWithin -Path $readyCandidate -Parent $stagingParent)) {
    throw "Generated ready-candidate path escaped its parent: $readyCandidate"
}
Assert-LhmReleasePathHasNoReparseAncestor -Path $readyCandidate
if ([System.IO.Directory]::Exists($readyCandidate) -or
    [System.IO.File]::Exists($readyCandidate)) {
    throw "A ready-candidate staging path already exists: $readyCandidate"
}

$published = $false
$readyOwned = $false
$verificationResult = $null
try {
    [System.IO.Directory]::CreateDirectory($runRoot) | Out-Null
    Assert-LhmReleasePathHasNoReparseAncestor -Path $runRoot
    [System.IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($artifactDirectory) | Out-Null

    $removed = @(Clear-LhmRepositoryBuildOutput -RepositoryRoot $repositoryRoot)
    if ($removed.Count -gt 0) {
        $removedFiles = [long](($removed | Measure-Object -Property Files -Sum).Sum)
        $removedBytes = [long](($removed | Measure-Object -Property Bytes -Sum).Sum)
        Write-Host "Removed $removedFiles generated files ($removedBytes bytes) from exact repository bin/obj roots."
    }
    Assert-LhmRepositoryBuildOutputEmpty -RepositoryRoot $repositoryRoot

    Push-Location -LiteralPath $repositoryRoot
    try {
        $sdkOutput = @(& $DotnetPath --version)
        if ($LASTEXITCODE -ne 0 -or $sdkOutput.Count -ne 1) {
            throw 'Unable to resolve the pinned .NET SDK version.'
        }
    }
    finally {
        Pop-Location
    }
    $sdkVersion = ([string]$sdkOutput[0]).Trim()
    if ($sdkVersion.Contains('-') -or $sdkVersion -cne $pinnedSdkVersion) {
        throw "Release candidates require pinned stable SDK '$pinnedSdkVersion'; resolved '$sdkVersion'."
    }

    if (-not $SkipVerification) {
        Invoke-LhmReleaseCommand -FilePath 'git' `
            -ArgumentList @('-C', $repositoryRoot, 'diff', '--check') `
            -WorkingDirectory $repositoryRoot `
            -Description 'Git diff hygiene'
        Invoke-LhmReleaseCommand -FilePath $NodePath `
            -ArgumentList @('--check', 'LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js') `
            -WorkingDirectory $repositoryRoot `
            -Description 'console.js syntax'
        Invoke-LhmReleaseCommand -FilePath $NodePath `
            -ArgumentList @('--check', 'LibreHardwareMonitor.Windows.Forms\Resources\Web\workspace.js') `
            -WorkingDirectory $repositoryRoot `
            -Description 'workspace.js syntax'
        Invoke-LhmReleaseCommand -FilePath $NodePath `
            -ArgumentList @('webtests\selftest.node.js') `
            -WorkingDirectory $repositoryRoot `
            -Description 'dashboard self-test'
        Invoke-LhmReleaseCommand -FilePath $NodePath `
            -ArgumentList @('--test', 'webtests\console.tests.js', 'webtests\workspace.tests.js') `
            -WorkingDirectory $repositoryRoot `
            -Description 'focused web tests'

        $testArtifacts = Join-Path $artifactDirectory 'tests'
        Invoke-LhmReleaseCommand -FilePath $DotnetPath `
            -ArgumentList @(
                'test',
                'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf',
                '-p:Platform=x64',
                '--artifacts-path',
                $testArtifacts) `
            -WorkingDirectory $repositoryRoot `
            -Description '.NET test suite'
        Assert-LhmRepositoryBuildOutputEmpty -RepositoryRoot $repositoryRoot
    }

    $packageRecords = @()
    foreach ($framework in @('net10.0-windows', 'net472')) {
        $frameworkArtifacts = Join-Path $artifactDirectory $framework
        Invoke-LhmReleaseCommand -FilePath $DotnetPath `
            -ArgumentList @(
                'build',
                'LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj',
                '-c',
                'Release',
                '-f',
                $framework,
                '-r',
                'win-x64',
                '--self-contained',
                'false',
                '-p:Platform=x64',
                '-p:GeneratePackageOnBuild=false',
                '--artifacts-path',
                $frameworkArtifacts) `
            -WorkingDirectory $repositoryRoot `
            -Description "Release build $framework/win-x64 framework-dependent"
        Assert-LhmRepositoryBuildOutputEmpty -RepositoryRoot $repositoryRoot

        $executables = @(
            Get-ChildItem -LiteralPath $frameworkArtifacts `
                -Filter 'LibreHardwareMonitor.Windows.Forms.exe' `
                -File `
                -Recurse |
                Where-Object {
                    $_.FullName -match '[\\/]bin[\\/]LibreHardwareMonitor\.Windows\.Forms[\\/]'
                }
        )
        if ($executables.Count -ne 1) {
            throw "Expected exactly one $framework application executable under external artifacts; found $($executables.Count)."
        }

        $payloadRoot = $executables[0].DirectoryName
        $entryPoint = ConvertTo-LhmReleaseRelativePath -Root $payloadRoot -Path $executables[0].FullName
        $archiveName = "LibreHardwareMonitor-$ReleaseId-win-x64-$framework.zip"
        $archivePath = Join-Path $packageDirectory $archiveName
        $payloadFiles = @(
            New-LhmReleaseZip `
                -PayloadRoot $payloadRoot `
                -Destination $archivePath `
                -TimestampUtc $createdUtc
        )
        $archiveItem = Get-Item -LiteralPath $archivePath
        $versionArchive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            $versionEntries = @(
                $versionArchive.Entries |
                    Where-Object {
                        $_.FullName.Replace('\', '/') -ceq $entryPoint
                    }
            )
            if ($versionEntries.Count -ne 1) {
                throw "Expected one entry point in the completed archive; found $($versionEntries.Count)."
            }
            $versionInfo = Get-LhmZipEntryVersionInfo -Entry $versionEntries[0]
        }
        finally {
            $versionArchive.Dispose()
        }

        $packageRecords += [ordered]@{
            targetFramework = $framework
            platform = 'x64'
            runtimeIdentifier = 'win-x64'
            selfContained = $false
            entryPoint = $entryPoint
            fileVersion = [string]$versionInfo.FileVersion
            productVersion = [string]$versionInfo.ProductVersion
            archive = 'packages/' + $archiveName
            length = [long]$archiveItem.Length
            sha256 = Get-LhmReleaseFileSha256 -Path $archivePath
            files = $payloadFiles
        }
    }

    Assert-ReleaseSourceUnchanged

    $verificationCommands = [object[]]@()
    if (-not $SkipVerification) {
        $verificationCommands = [object[]]@(Get-LhmReleaseVerificationCommands)
    }
    $manifest = [ordered]@{
        schema = 'sq.lhm-release'
        version = 1
        releaseId = $ReleaseId
        createdUtc = $createdUtc.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
        baseVersion = $baseVersion
        sdkVersion = $sdkVersion
        configuration = 'Release'
        platform = 'x64'
        source = [ordered]@{
            commit = $sourceCommit
            branch = $sourceBranch
            repository = $originIdentifier
            clean = -not $isDirty
            changes = @($sourceChanges)
            fingerprintSha256 = $sourceFingerprint.Sha256
            fileCount = $sourceFingerprint.FileCount
        }
        verification = [ordered]@{
            completed = -not $SkipVerification
            commands = $verificationCommands
        }
        promotable = (-not $isDirty) -and (-not $SkipVerification)
        packages = $packageRecords
    }

    Write-LhmReleaseJsonAtomically `
        -Value $manifest `
        -Path (Join-Path $partialCandidate 'release-manifest.json')
    Test-LhmReleaseCandidate -CandidatePath $partialCandidate | Out-Null
    $candidateIdentity = Get-LhmCandidateContentIdentity -CandidatePath $partialCandidate
    Assert-LhmRepositoryBuildOutputEmpty -RepositoryRoot $repositoryRoot
    Assert-LhmReleasePathHasNoReparseAncestor -Path $partialCandidate
    Assert-LhmReleasePathHasNoReparseAncestor -Path $readyCandidate
    Assert-LhmReleasePathHasNoReparseAncestor -Path $candidateRoot
    Assert-LhmReleasePathHasNoReparseAncestor -Path $finalCandidate

    [System.IO.Directory]::Move($partialCandidate, $readyCandidate)
    $readyOwned = $true

    $resolvedRunRoot = Resolve-LhmReleaseFullPath -Path $runRoot
    if (-not (Test-LhmReleasePathWithin -Path $resolvedRunRoot -Parent $stagingParent)) {
        throw "Refusing to finalize staging outside its parent: $resolvedRunRoot"
    }
    Assert-LhmReleasePathHasNoReparseAncestor -Path $stagingParent
    Assert-LhmReleasePathHasNoReparseAncestor -Path $resolvedRunRoot
    Remove-LhmOwnedReleaseDirectory -Path $resolvedRunRoot -Parent $stagingParent

    $remainingBuildOutput = @(
        Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $repositoryRoot |
            Where-Object { [System.IO.Directory]::Exists($_) }
    )
    if ($remainingBuildOutput.Count -gt 0) {
        Clear-LhmRepositoryBuildOutput -RepositoryRoot $repositoryRoot | Out-Null
    }
    Assert-LhmRepositoryBuildOutputEmpty -RepositoryRoot $repositoryRoot

    # The candidate was fully verified before the same-volume rename. Recheck the manifest and
    # every archive by path, length, and SHA-256 before final publication.
    Assert-ReleaseSourceUnchanged
    Assert-LhmCandidateContentUnchanged -CandidatePath $readyCandidate -Expected $candidateIdentity

    [System.IO.Directory]::Move($readyCandidate, $finalCandidate)
    $readyOwned = $false
    $published = $true
    $verificationResult = Test-LhmReleaseCandidate -CandidatePath $finalCandidate
    Assert-ReleaseSourceUnchanged
}
catch {
    if ($published -and [System.IO.Directory]::Exists($finalCandidate)) {
        if (-not (Test-LhmReleasePathWithin -Path $finalCandidate -Parent $candidateRoot)) {
            throw "Refusing to remove failed candidate outside the candidate root: $finalCandidate"
        }
        Assert-LhmReleasePathHasNoReparseAncestor -Path $candidateRoot
        Assert-LhmReleasePathHasNoReparseAncestor -Path $finalCandidate
        Remove-LhmOwnedReleaseDirectory -Path $finalCandidate -Parent $candidateRoot
        $published = $false
    }
    throw
}
finally {
    $finalizationFailure = $null
    try {
        if ($readyOwned -and [System.IO.Directory]::Exists($readyCandidate)) {
            if (-not (Test-LhmReleasePathWithin -Path $readyCandidate -Parent $stagingParent)) {
                throw "Refusing to clean ready candidate outside staging: $readyCandidate"
            }
            Assert-LhmReleasePathHasNoReparseAncestor -Path $stagingParent
            Assert-LhmReleasePathHasNoReparseAncestor -Path $readyCandidate
            Remove-LhmOwnedReleaseDirectory -Path $readyCandidate -Parent $stagingParent
        }

        if ([System.IO.Directory]::Exists($runRoot)) {
            $resolvedRunRoot = Resolve-LhmReleaseFullPath -Path $runRoot
            if (-not (Test-LhmReleasePathWithin -Path $resolvedRunRoot -Parent $stagingParent)) {
                throw "Refusing to clean staging outside its parent: $resolvedRunRoot"
            }
            Assert-LhmReleasePathHasNoReparseAncestor -Path $stagingParent
            Assert-LhmReleasePathHasNoReparseAncestor -Path $resolvedRunRoot
            Remove-LhmOwnedReleaseDirectory -Path $resolvedRunRoot -Parent $stagingParent
        }

        $remainingBuildOutput = @(
            Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $repositoryRoot |
                Where-Object { [System.IO.Directory]::Exists($_) }
        )
        if ($remainingBuildOutput.Count -gt 0) {
            Clear-LhmRepositoryBuildOutput -RepositoryRoot $repositoryRoot | Out-Null
        }
        Assert-LhmRepositoryBuildOutputEmpty -RepositoryRoot $repositoryRoot
    }
    catch {
        $finalizationFailure = $_
    }

    if ($null -ne $finalizationFailure) {
        if ($published -and [System.IO.Directory]::Exists($finalCandidate)) {
            if (-not (Test-LhmReleasePathWithin -Path $finalCandidate -Parent $candidateRoot)) {
                throw "Refusing to roll back candidate outside its root: $finalCandidate"
            }
            Assert-LhmReleasePathHasNoReparseAncestor -Path $candidateRoot
            Assert-LhmReleasePathHasNoReparseAncestor -Path $finalCandidate
            Remove-LhmOwnedReleaseDirectory -Path $finalCandidate -Parent $candidateRoot
            $published = $false
        }
        throw $finalizationFailure
    }
}

Write-Output $verificationResult

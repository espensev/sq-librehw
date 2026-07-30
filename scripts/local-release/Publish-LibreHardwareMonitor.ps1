[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $CandidateDirectory,

    [switch] $AllowDirty
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$commonScript = Join-Path $repositoryRoot 'ops\local-release\LhmLocalRelease.Common.ps1'
. $commonScript

function Invoke-CheckedCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $Description
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$resolvedCandidateDirectory = Resolve-LhmFullPath -Path $CandidateDirectory
if (Test-Path -LiteralPath $resolvedCandidateDirectory) {
    throw "Candidate directory already exists: '$resolvedCandidateDirectory'."
}

$candidateParent = Split-Path -Parent $resolvedCandidateDirectory
if ([string]::IsNullOrWhiteSpace($candidateParent)) {
    throw 'CandidateDirectory must have a parent directory.'
}

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
$git = Get-Command git -CommandType Application -ErrorAction Stop

$dirtyLines = @(& $git.Source -C $repositoryRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw "'git status' failed with exit code $LASTEXITCODE."
}

if ($dirtyLines.Count -gt 0 -and -not $AllowDirty) {
    throw 'The source tree is dirty. Commit/stash the intended release or use -AllowDirty explicitly.'
}

$commit = [string](& $git.Source -C $repositoryRoot rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Unable to resolve the full source commit.'
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "sq-librehw-publish-$([guid]::NewGuid().ToString('N'))"
$verificationRoot = Join-Path $temporaryRoot 'verification'
$artifactsRoot = Join-Path $temporaryRoot 'artifacts'
$publishRoot = Join-Path $temporaryRoot 'publish'
$stagingDirectory = Join-Path $candidateParent ".lhm-candidate-$([guid]::NewGuid().ToString('N'))"

try {
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($candidateParent) | Out-Null

    $testProject = Join-Path $repositoryRoot 'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj'
    $applicationProject =
        Join-Path $repositoryRoot 'LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj'

    Invoke-CheckedCommand `
        -FilePath $dotnet.Source `
        -Description 'Release test gate' `
        -ArgumentList @(
            'test',
            $testProject,
            '-c', 'Release',
            '-p:Platform=x64',
            "-p:OutputPath=$(Join-Path $verificationRoot 'tests')",
            '--artifacts-path', (Join-Path $artifactsRoot 'tests')
        )

    foreach ($framework in @('net10.0-windows', 'net472')) {
        Invoke-CheckedCommand `
            -FilePath $dotnet.Source `
            -Description "$framework Release build gate" `
            -ArgumentList @(
                'build',
                $applicationProject,
                '-c', 'Release',
                '-f', $framework,
                '-p:Platform=x64',
                "-p:OutputPath=$(Join-Path $verificationRoot $framework)",
                '--artifacts-path', (Join-Path $artifactsRoot $framework)
            )
    }

    Invoke-CheckedCommand `
        -FilePath $dotnet.Source `
        -Description 'Single-file release publish' `
        -ArgumentList @(
            'publish',
            $applicationProject,
            '-c', 'Release',
            '-f', 'net10.0-windows',
            '-r', 'win-x64',
            '-p:Platform=x64',
            '--self-contained', 'false',
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:AutoGenerateBindingRedirects=false',
            '-p:GenerateDocumentationFile=false',
            '-p:PublishTrimmed=false',
            '-p:DebugType=None',
            '-p:DebugSymbols=false',
            "-p:OutputPath=$(Join-Path $verificationRoot 'publish-build')",
            '--artifacts-path', (Join-Path $artifactsRoot 'publish'),
            '-o', $publishRoot
        )

    $publishedEntries = @(Get-ChildItem -LiteralPath $publishRoot -Force)
    $publishedExecutables = @($publishedEntries | Where-Object {
        -not $_.PSIsContainer -and $_.Extension -ieq '.exe'
    })
    if ($publishedEntries.Count -ne 1 -or
        $publishedExecutables.Count -ne 1 -or
        $publishedExecutables[0].Name -cne $script:LhmExecutableName) {
        throw 'Publish output must contain exactly LibreHardwareMonitor.Windows.Forms.exe.'
    }

    $publishedExecutable = $publishedExecutables[0].FullName
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($publishedExecutable)
    $version = [string]$versionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = [string]$versionInfo.FileVersion
    }
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'The published executable has no product or file version.'
    }

    $timestamp = [DateTimeOffset]::UtcNow
    $releaseId = '{0}-{1}-{2}' -f
        ($version -replace '[^0-9A-Za-z._-]', '_'),
        $commit.Substring(0, 12).ToLowerInvariant(),
        $timestamp.ToString('yyyyMMddTHHmmssZ')

    [System.IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
    $stagedExecutable = Join-Path $stagingDirectory $script:LhmExecutableName
    Copy-Item -LiteralPath $publishedExecutable -Destination $stagedExecutable

    $manifest = [ordered]@{
        schema = $script:LhmReleaseSchema
        releaseId = $releaseId
        commit = $commit.ToLowerInvariant()
        version = $version
        framework = 'net10.0-windows'
        runtime = 'win-x64'
        selfContained = $false
        timestamp = $timestamp.ToString('o')
        sha256 = Get-LhmFileSha256 -Path $stagedExecutable
    }
    $manifest |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $stagingDirectory $script:LhmReleaseManifestName) -Encoding UTF8

    $null = Read-LhmReleasePayload -Directory $stagingDirectory -RequireCandidateShape
    Move-Item -LiteralPath $stagingDirectory -Destination $resolvedCandidateDirectory

    $candidate = Read-LhmReleasePayload `
        -Directory $resolvedCandidateDirectory `
        -RequireCandidateShape
    [pscustomobject]@{
        CandidateDirectory = $candidate.Directory
        ReleaseId = $candidate.Manifest.releaseId
        Version = $candidate.Manifest.version
        Commit = $candidate.Manifest.commit
        Sha256 = $candidate.Sha256
        ExecutableBytes = (Get-Item -LiteralPath $candidate.ExecutablePath).Length
    }
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-LhmOwnedDirectory `
            -Path $stagingDirectory `
            -ExpectedParent $candidateParent `
            -RequiredLeafPrefix '.lhm-candidate-'
    }

    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-LhmOwnedDirectory `
            -Path $temporaryRoot `
            -ExpectedParent ([System.IO.Path]::GetTempPath()) `
            -RequiredLeafPrefix 'sq-librehw-publish-'
    }
}

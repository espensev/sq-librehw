[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmRelease.Common.ps1')

$script:assertionCount = 0

function Assert-LhmReleaseTest {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
    $script:assertionCount++
}

function Assert-LhmReleaseThrows {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][string]$Message,
        [string]$Like
    )

    $caught = $null
    try {
        & $Action
    }
    catch {
        $caught = $_
    }

    Assert-LhmReleaseTest ($null -ne $caught) $Message
    if (-not [string]::IsNullOrWhiteSpace($Like)) {
        Assert-LhmReleaseTest ($caught.Exception.Message -like $Like) "$Message (actual: $($caught.Exception.Message))"
    }
}

$tempBase = Resolve-LhmReleaseFullPath -Path ([System.IO.Path]::GetTempPath())
$testRoot = Resolve-LhmReleaseFullPath -Path (
    Join-Path $tempBase ('sq-lhm-release-test-' + [guid]::NewGuid().ToString('N')))
$tempPrefix = Get-LhmReleasePathPrefix -Path $tempBase
if (-not $testRoot.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use test root outside system temp: $testRoot"
}

$repositoryRoot = Resolve-LhmReleaseFullPath -Path (Join-Path $PSScriptRoot '..\..')

try {
    [System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $fakeRepository = Join-Path $testRoot 'repository'
    $externalRoot = Join-Path $testRoot 'external'
    [System.IO.Directory]::CreateDirectory($fakeRepository) | Out-Null
    [System.IO.Directory]::CreateDirectory($externalRoot) | Out-Null

    Assert-LhmReleaseThrows {
        Assert-LhmDisjointReleaseRoot -RepositoryRoot $fakeRepository -ReleaseRoot $fakeRepository
    } 'equal release/repository roots must be rejected' '*must be disjoint*'
    Assert-LhmReleaseThrows {
        Assert-LhmDisjointReleaseRoot -RepositoryRoot $fakeRepository -ReleaseRoot (Join-Path $fakeRepository 'release')
    } 'release roots inside the repository must be rejected' '*must be disjoint*'
    Assert-LhmReleaseThrows {
        Assert-LhmDisjointReleaseRoot -RepositoryRoot $fakeRepository -ReleaseRoot $testRoot
    } 'release roots containing the repository must be rejected' '*must be disjoint*'
    $resolvedExternal = Assert-LhmDisjointReleaseRoot -RepositoryRoot $fakeRepository -ReleaseRoot $externalRoot
    Assert-LhmReleaseTest ($resolvedExternal -eq (Resolve-LhmReleaseFullPath -Path $externalRoot)) 'disjoint external root should resolve'

    $junctionTarget = Join-Path $testRoot 'junction-target'
    $junctionPath = Join-Path $testRoot 'release-junction'
    $projectJunction = Join-Path $fakeRepository 'JunctionProject'
    [System.IO.Directory]::CreateDirectory($junctionTarget) | Out-Null
    try {
        New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget | Out-Null
        New-Item -ItemType Junction -Path $projectJunction -Target $junctionTarget | Out-Null
        Assert-LhmReleaseThrows {
            Assert-LhmDisjointReleaseRoot -RepositoryRoot $fakeRepository -ReleaseRoot $junctionPath
        } 'release-root junctions must be rejected' '*reparse*'
        Assert-LhmReleaseThrows {
            Assert-LhmRepositoryBuildOutputPath `
                -RepositoryRoot $fakeRepository `
                -Path (Join-Path $projectJunction 'bin')
        } 'cleanup paths traversing junctions must be rejected' '*reparse*'
    }
    finally {
        if ([System.IO.Directory]::Exists($projectJunction)) {
            Remove-Item -LiteralPath $projectJunction -Force
        }
        if ([System.IO.Directory]::Exists($junctionPath)) {
            Remove-Item -LiteralPath $junctionPath -Force
        }
    }

    [System.IO.File]::WriteAllText((Join-Path $fakeRepository 'Root.csproj'), '<Project />')
    $subProject = Join-Path $fakeRepository 'SubProject'
    [System.IO.Directory]::CreateDirectory($subProject) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $subProject 'SubProject.csproj'), '<Project />')
    [System.IO.File]::WriteAllText(
        (Join-Path $fakeRepository '.gitignore'),
        "[Bb]in/`r`n[Oo]bj/`r`n")
    & git -C $fakeRepository init -q -b main
    if ($LASTEXITCODE -ne 0) { throw 'Cleanup fixture Git initialization failed.' }
    & git -C $fakeRepository config user.name 'Release Fixture'
    & git -C $fakeRepository config user.email 'release-fixture@example.invalid'
    & git -C $fakeRepository config core.autocrlf false
    & git -C $fakeRepository add -- .
    & git -C $fakeRepository commit -q -m 'cleanup fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Cleanup fixture Git commit failed.' }
    foreach ($generatedPath in @(
        (Join-Path $fakeRepository 'bin'),
        (Join-Path $fakeRepository 'obj'),
        (Join-Path $subProject 'bin'),
        (Join-Path $subProject 'obj'))) {
        [System.IO.Directory]::CreateDirectory($generatedPath) | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $generatedPath 'stale.bin'), 'stale')
    }
    $unrelated = Join-Path $fakeRepository 'keep.txt'
    [System.IO.File]::WriteAllText($unrelated, 'keep')

    $nestedJunctionTarget = Join-Path $testRoot 'nested-cleanup-junction-target'
    $nestedJunctionMarker = Join-Path $nestedJunctionTarget 'must-survive.txt'
    $nestedJunction = Join-Path $fakeRepository 'bin\nested-junction'
    [System.IO.Directory]::CreateDirectory($nestedJunctionTarget) | Out-Null
    [System.IO.File]::WriteAllText($nestedJunctionMarker, 'must survive')
    try {
        New-Item `
            -ItemType Junction `
            -Path $nestedJunction `
            -Target $nestedJunctionTarget | Out-Null
        Assert-LhmReleaseThrows {
            Clear-LhmRepositoryBuildOutput -RepositoryRoot $fakeRepository | Out-Null
        } 'cleanup must reject a descendant junction inside an exact bin target' '*reparse*'
        Assert-LhmReleaseTest (
            [System.IO.File]::Exists($nestedJunctionMarker)
        ) 'rejected descendant junction cleanup must leave its target untouched'
        Assert-LhmReleaseTest (
            [System.IO.File]::Exists((Join-Path $fakeRepository 'bin\stale.bin'))
        ) 'rejected descendant junction cleanup must leave the owning bin tree untouched'
    }
    finally {
        if ([System.IO.Directory]::Exists($nestedJunction)) {
            [System.IO.Directory]::Delete($nestedJunction)
        }
    }

    $cleaned = @(Clear-LhmRepositoryBuildOutput -RepositoryRoot $fakeRepository)
    Assert-LhmReleaseTest ($cleaned.Count -eq 4) 'cleanup should target each exact generated root once'
    Assert-LhmReleaseTest ([System.IO.File]::Exists($unrelated)) 'cleanup must preserve unrelated repository files'
    Assert-LhmReleaseTest (-not [System.IO.Directory]::Exists((Join-Path $fakeRepository 'bin'))) 'root bin should be removed'
    Assert-LhmReleaseTest (-not [System.IO.Directory]::Exists((Join-Path $subProject 'obj'))) 'project obj should be removed'

    $trackedOutputRepository = Join-Path $testRoot 'tracked-output-repository'
    $trackedOutputPath = Join-Path $trackedOutputRepository 'Bin\tracked.keep'
    [System.IO.Directory]::CreateDirectory(
        (Join-Path $trackedOutputRepository 'Bin')) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $trackedOutputRepository 'TrackedOutput.csproj'),
        '<Project />')
    [System.IO.File]::WriteAllText(
        (Join-Path $trackedOutputRepository '.gitignore'),
        "[Bb]in/`r`n[Oo]bj/`r`n")
    [System.IO.File]::WriteAllText($trackedOutputPath, 'tracked output must survive')
    & git -C $trackedOutputRepository init -q -b main
    if ($LASTEXITCODE -ne 0) { throw 'Tracked-output fixture Git initialization failed.' }
    & git -C $trackedOutputRepository config user.name 'Release Fixture'
    & git -C $trackedOutputRepository config user.email 'release-fixture@example.invalid'
    & git -C $trackedOutputRepository config core.autocrlf false
    & git -C $trackedOutputRepository add -- .gitignore TrackedOutput.csproj
    & git -C $trackedOutputRepository add -f -- Bin/tracked.keep
    & git -C $trackedOutputRepository commit -q -m 'tracked output fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Tracked-output fixture Git commit failed.' }
    Assert-LhmReleaseThrows {
        Clear-LhmRepositoryBuildOutput -RepositoryRoot $trackedOutputRepository | Out-Null
    } 'cleanup must reject case-variant tracked files beneath an otherwise ignored bin target' '*containing tracked files*'
    Assert-LhmReleaseTest (
        [System.IO.File]::Exists($trackedOutputPath)
    ) 'rejected tracked-output cleanup must preserve the tracked file'
    Assert-LhmReleaseTest (
        [System.IO.File]::ReadAllText($trackedOutputPath) -ceq 'tracked output must survive'
    ) 'rejected tracked-output cleanup must preserve tracked file contents'

    $orchestrationRepository = Join-Path $testRoot 'orchestration-repository'
    $orchestrationReleaseRoot = Join-Path $testRoot 'orchestration-releases'
    $orchestrationScripts = Join-Path $orchestrationRepository 'ops\candidate'
    [System.IO.Directory]::CreateDirectory($orchestrationScripts) | Out-Null
    foreach ($scriptName in @(
        'LhmRelease.Common.ps1',
        'New-LhmRelease.ps1',
        'Test-LhmReleaseCandidate.ps1')) {
        Copy-Item `
            -LiteralPath (Join-Path $PSScriptRoot $scriptName) `
            -Destination (Join-Path $orchestrationScripts $scriptName)
    }
    [System.IO.Directory]::CreateDirectory(
        (Join-Path $orchestrationRepository 'LibreHardwareMonitor.Windows.Forms')) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $orchestrationRepository 'LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj'),
        '<Project />')

    $whereExecutable = Join-Path $env:WINDIR 'System32\where.exe'
    $whereVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        $whereExecutable)
    $whereFileVersionMatch = [System.Text.RegularExpressions.Regex]::Match(
        [string]$whereVersionInfo.FileVersion,
        '^(?<base>\d+\.\d+\.\d+)')
    $whereProductVersionMatch = [System.Text.RegularExpressions.Regex]::Match(
        [string]$whereVersionInfo.ProductVersion,
        '^(?<base>\d+\.\d+\.\d+)')
    if (-not $whereFileVersionMatch.Success -or
        -not $whereProductVersionMatch.Success -or
        $whereFileVersionMatch.Groups['base'].Value -cne
            $whereProductVersionMatch.Groups['base'].Value) {
        throw 'Unable to derive one fixture base version from where.exe metadata.'
    }
    $fixtureBaseVersion = $whereFileVersionMatch.Groups['base'].Value
    [System.IO.File]::WriteAllText(
        (Join-Path $orchestrationRepository 'Directory.Build.props'),
        "<Project><PropertyGroup><Version>$fixtureBaseVersion</Version></PropertyGroup></Project>")
    [System.IO.File]::WriteAllText(
        (Join-Path $orchestrationRepository 'global.json'),
        @'
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
'@,
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $orchestrationRepository '.gitignore'),
        "[Bb]in/`r`n[Oo]bj/`r`n")

    $fakeDotnet = Join-Path $orchestrationRepository 'fake-dotnet.ps1'
    $fakeDotnetSource = @'
$arguments = @($args)
if (-not [string]::IsNullOrWhiteSpace($env:FAKE_LHM_INVOCATION_LOG)) {
    [System.IO.File]::AppendAllText(
        $env:FAKE_LHM_INVOCATION_LOG,
        $PWD.Path + "`t" + [string]::Join(' ', [string[]]$arguments) + "`r`n")
}
if ($arguments.Count -eq 1 -and $arguments[0] -eq '--version') {
    if (-not $PWD.Path.Equals(
        $PSScriptRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        exit 92
    }
    Write-Output '10.0.302'
    exit 0
}
if ($arguments.Count -lt 2 -or $arguments[0] -ne 'build') {
    exit 90
}
$frameworkIndex = [array]::IndexOf($arguments, '-f')
$artifactsIndex = [array]::IndexOf($arguments, '--artifacts-path')
$runtimeIndex = [array]::IndexOf($arguments, '-r')
$selfContainedIndex = [array]::IndexOf($arguments, '--self-contained')
if ($frameworkIndex -lt 0 -or
    $artifactsIndex -lt 0 -or
    $runtimeIndex -lt 0 -or
    $selfContainedIndex -lt 0) {
    exit 91
}
if ($runtimeIndex + 1 -ge $arguments.Count -or
    $arguments[$runtimeIndex + 1] -cne 'win-x64' -or
    $selfContainedIndex + 1 -ge $arguments.Count -or
    $arguments[$selfContainedIndex + 1] -cne 'false' -or
    @($arguments | Where-Object { $_ -ceq '-r' }).Count -ne 1 -or
    @($arguments | Where-Object { $_ -ceq '--self-contained' }).Count -ne 1) {
    exit 93
}
$framework = $arguments[$frameworkIndex + 1]
$artifacts = $arguments[$artifactsIndex + 1]
if ($env:FAKE_LHM_FAIL_FRAMEWORK -eq $framework) {
    exit 17
}
$output = Join-Path $artifacts "bin\LibreHardwareMonitor.Windows.Forms\release_$framework"
[System.IO.Directory]::CreateDirectory($output) | Out-Null
[System.IO.File]::Copy(
    (Join-Path $env:WINDIR 'System32\where.exe'),
    (Join-Path $output 'LibreHardwareMonitor.Windows.Forms.exe'))
[System.IO.File]::WriteAllText(
    (Join-Path $output 'LibreHardwareMonitorLib.dll'),
    "fixture-$framework")
if (-not [string]::IsNullOrWhiteSpace($env:FAKE_LHM_ROGUE_REPOSITORY)) {
    $rogue = Join-Path $env:FAKE_LHM_ROGUE_REPOSITORY 'bin'
    [System.IO.Directory]::CreateDirectory($rogue) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $rogue 'rogue.bin'), 'rogue')
}
if (-not [string]::IsNullOrWhiteSpace($env:FAKE_LHM_MUTATE_SOURCE) -and
    $framework -eq 'net472') {
    [System.IO.File]::AppendAllText($env:FAKE_LHM_MUTATE_SOURCE, '<!-- changed during build -->')
}
exit 0
'@
    [System.IO.File]::WriteAllText(
        $fakeDotnet,
        $fakeDotnetSource,
        [System.Text.UTF8Encoding]::new($false))

    & git -C $orchestrationRepository init -q -b main
    if ($LASTEXITCODE -ne 0) { throw 'Fixture Git initialization failed.' }
    & git -C $orchestrationRepository config user.name 'Release Fixture'
    & git -C $orchestrationRepository config user.email 'release-fixture@example.invalid'
    & git -C $orchestrationRepository config core.autocrlf false
    & git -C $orchestrationRepository remote add origin `
        'https://fixture-user:fixture-secret@Example.COM:8443/Org/Repo.git?token=not-safe#fragment'
    & git -C $orchestrationRepository add -- .
    & git -C $orchestrationRepository commit -q -m 'fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Fixture Git commit failed.' }

    $fixtureCreator = Join-Path $orchestrationScripts 'New-LhmRelease.ps1'
    $junctionReleaseRoot = Join-Path $testRoot 'orchestration-junction-releases'
    $junctionCandidateTarget = Join-Path $testRoot 'orchestration-junction-candidate-target'
    $junctionCandidateRoot = Join-Path $junctionReleaseRoot 'candidates'
    [System.IO.Directory]::CreateDirectory($junctionReleaseRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($junctionCandidateTarget) | Out-Null
    try {
        New-Item `
            -ItemType Junction `
            -Path $junctionCandidateRoot `
            -Target $junctionCandidateTarget | Out-Null
        Assert-LhmReleaseThrows {
            & $fixtureCreator `
                -ReleaseRoot $junctionReleaseRoot `
                -ReleaseId 'fixture-junction-root' `
                -SkipVerification `
                -DotnetPath $fakeDotnet | Out-Null
        } 'pre-existing candidate-root junctions must fail before building' '*reparse*'
    }
    finally {
        if ([System.IO.Directory]::Exists($junctionCandidateRoot)) {
            Remove-Item -LiteralPath $junctionCandidateRoot -Force
        }
    }
    Assert-LhmReleaseTest (
        @(Get-ChildItem -LiteralPath $junctionCandidateTarget -Force).Count -eq 0
    ) 'rejected candidate-root junction must not mutate its target'

    $fakeDotnetInvocationLog = Join-Path $testRoot 'fake-dotnet-invocations.log'
    try {
        $env:FAKE_LHM_INVOCATION_LOG = $fakeDotnetInvocationLog
        $fixtureSuccess = @(
            & $fixtureCreator `
                -ReleaseRoot $orchestrationReleaseRoot `
                -ReleaseId 'fixture-success' `
                -SkipVerification `
                -DotnetPath $fakeDotnet
        )
    }
    finally {
        Remove-Item Env:\FAKE_LHM_INVOCATION_LOG -ErrorAction SilentlyContinue
    }
    Assert-LhmReleaseTest ($fixtureSuccess.Status -contains 'VERIFIED') 'fake end-to-end release should publish a verified candidate'
    Assert-LhmReleaseTest (-not ($fixtureSuccess.Promotable -contains $true)) 'skipped-verification fixture must be non-promotable'
    $fixtureCandidate = Join-Path $orchestrationReleaseRoot 'candidates\fixture-success'
    $fixtureManifestPath = Join-Path $fixtureCandidate 'release-manifest.json'
    Assert-LhmReleaseTest ([System.IO.File]::Exists($fixtureManifestPath)) 'fake end-to-end release should publish manifest last'
    Assert-LhmReleaseTest (-not [System.IO.Directory]::Exists((Join-Path $orchestrationRepository 'bin'))) 'fake end-to-end release must leave source bin absent'

    $versionInvocations = @(
        Get-Content -LiteralPath $fakeDotnetInvocationLog |
            Where-Object { $_ -match "`t--version$" }
    )
    Assert-LhmReleaseTest (
        $versionInvocations.Count -eq 1
    ) 'fake release should resolve the SDK exactly once'
    $versionInvocationWorkingDirectory = $versionInvocations[0].Split("`t")[0]
    Assert-LhmReleaseTest (
        $versionInvocationWorkingDirectory.Equals(
            $orchestrationRepository,
            [System.StringComparison]::OrdinalIgnoreCase)
    ) 'dotnet --version must run from the repository containing global.json'
    $buildInvocations = @(
        Get-Content -LiteralPath $fakeDotnetInvocationLog |
            Where-Object { $_ -match "`tbuild " }
    )
    Assert-LhmReleaseTest (
        $buildInvocations.Count -eq 2
    ) 'fake release should invoke exactly two framework builds'
    Assert-LhmReleaseTest (
        @($buildInvocations | Where-Object {
            $_ -notmatch '(?:^| )-r win-x64(?: |$)' -or
            $_ -notmatch '(?:^| )--self-contained false(?: |$)'
        }).Count -eq 0
    ) 'every release build must be framework-dependent win-x64'

    $fixtureManifestJson = Get-Content -Raw -LiteralPath $fixtureManifestPath
    $fixtureManifest = $fixtureManifestJson | ConvertFrom-Json
    Assert-LhmReleaseTest (
        $fixtureManifest.sdkVersion -ceq '10.0.302'
    ) 'fake release manifest should record the pinned SDK selected from global.json'
    Assert-LhmReleaseTest (
        $fixtureManifest.baseVersion -ceq $fixtureBaseVersion
    ) 'fake release base version should match the copied executable version prefix'
    Assert-LhmReleaseTest (
        $fixtureManifest.source.repository -ceq 'example.com:8443/Org/Repo.git'
    ) 'credentialed origin URL must be normalized to a credential-safe manifest identifier'
    Assert-LhmReleaseTest (
        $fixtureManifestJson -notmatch 'fixture-user|fixture-secret|token=not-safe|://'
    ) 'release manifest must not retain origin credentials, query strings, or URL schemes'

    $fixtureVerifier = Join-Path $orchestrationScripts 'Test-LhmReleaseCandidate.ps1'
    Assert-LhmReleaseThrows {
        & $fixtureVerifier `
            -Latest `
            -ReleaseRoot $orchestrationReleaseRoot `
            -RequirePromotable | Out-Null
    } 'wrapper must reject a verified candidate created with skipped verification when promotion is required' '*not promotable*'
    $currentSourceResult = @(
        & $fixtureVerifier `
            -Latest `
            -ReleaseRoot $orchestrationReleaseRoot `
            -RequireCurrentSource
    )
    Assert-LhmReleaseTest (
        $currentSourceResult.Status -contains 'VERIFIED'
    ) 'wrapper current-source gate should accept the unchanged fixture checkout'

    $wrapperDriftPath = Join-Path $orchestrationRepository 'wrapper-drift.txt'
    try {
        [System.IO.File]::WriteAllText($wrapperDriftPath, 'drift after candidate creation')
        Assert-LhmReleaseThrows {
            & $fixtureVerifier `
                -Latest `
                -ReleaseRoot $orchestrationReleaseRoot `
                -RequireCurrentSource | Out-Null
        } 'wrapper current-source gate must reject source drift after candidate creation' '*does not match the current repository source state*'
    }
    finally {
        if ([System.IO.File]::Exists($wrapperDriftPath)) {
            [System.IO.File]::Delete($wrapperDriftPath)
        }
    }

    $dirtyMarkerPath = Join-Path $orchestrationRepository 'dirty-marker.txt'
    $dirtyInvocationLog = Join-Path $testRoot 'dirty-id-dotnet-invocations.log'
    try {
        [System.IO.File]::WriteAllText($dirtyMarkerPath, 'dirty')
        $env:FAKE_LHM_INVOCATION_LOG = $dirtyInvocationLog
        Assert-LhmReleaseThrows {
            & $fixtureCreator `
                -ReleaseRoot $orchestrationReleaseRoot `
                -ReleaseId 'fixture-dirty-custom' `
                -AllowDirty `
                -SkipVerification `
                -DotnetPath $fakeDotnet | Out-Null
        } 'dirty custom ReleaseId without the required suffix must fail before building' '*must end in -dirty*'
    }
    finally {
        Remove-Item Env:\FAKE_LHM_INVOCATION_LOG -ErrorAction SilentlyContinue
        if ([System.IO.File]::Exists($dirtyMarkerPath)) {
            [System.IO.File]::Delete($dirtyMarkerPath)
        }
    }
    Assert-LhmReleaseTest (
        -not [System.IO.File]::Exists($dirtyInvocationLog)
    ) 'dirty ReleaseId mismatch must be rejected before invoking dotnet'
    Assert-LhmReleaseTest (
        -not [System.IO.Directory]::Exists(
            (Join-Path $orchestrationReleaseRoot 'candidates\fixture-dirty-custom'))
    ) 'dirty ReleaseId mismatch must publish no candidate'

    $fixtureManifestHash = Get-LhmReleaseFileSha256 -Path (
        $fixtureManifestPath)
    Assert-LhmReleaseThrows {
        & $fixtureCreator `
            -ReleaseRoot $orchestrationReleaseRoot `
            -ReleaseId 'fixture-success' `
            -SkipVerification `
            -DotnetPath $fakeDotnet | Out-Null
    } 'existing candidate IDs must be immutable' '*already exists*'
    Assert-LhmReleaseTest (
        (Get-LhmReleaseFileSha256 -Path (Join-Path $fixtureCandidate 'release-manifest.json')) -ceq $fixtureManifestHash
    ) 'collision refusal must preserve the existing candidate'

    try {
        $env:FAKE_LHM_FAIL_FRAMEWORK = 'net472'
        Assert-LhmReleaseThrows {
            & $fixtureCreator `
                -ReleaseRoot $orchestrationReleaseRoot `
                -ReleaseId 'fixture-second-build-failure' `
                -SkipVerification `
                -DotnetPath $fakeDotnet | Out-Null
        } 'second framework failure must abort the whole candidate' '*exit code 17*'
    }
    finally {
        Remove-Item Env:\FAKE_LHM_FAIL_FRAMEWORK -ErrorAction SilentlyContinue
    }
    Assert-LhmReleaseTest (
        -not [System.IO.Directory]::Exists(
            (Join-Path $orchestrationReleaseRoot 'candidates\fixture-second-build-failure'))
    ) 'second framework failure must publish no partial candidate'

    try {
        $env:FAKE_LHM_ROGUE_REPOSITORY = $orchestrationRepository
        Assert-LhmReleaseThrows {
            & $fixtureCreator `
                -ReleaseRoot $orchestrationReleaseRoot `
                -ReleaseId 'fixture-source-output-leak' `
                -SkipVerification `
                -DotnetPath $fakeDotnet | Out-Null
        } 'source-output recreation must fail closed' '*was recreated*'
    }
    finally {
        Remove-Item Env:\FAKE_LHM_ROGUE_REPOSITORY -ErrorAction SilentlyContinue
    }
    Assert-LhmReleaseTest (
        -not [System.IO.Directory]::Exists(
            (Join-Path $orchestrationReleaseRoot 'candidates\fixture-source-output-leak'))
    ) 'source-output leak must publish no candidate'
    Assert-LhmReleaseTest (
        -not [System.IO.Directory]::Exists((Join-Path $orchestrationRepository 'bin'))
    ) 'source-output leak cleanup must restore absent bin'

    try {
        $env:FAKE_LHM_MUTATE_SOURCE = Join-Path $orchestrationRepository 'Directory.Build.props'
        Assert-LhmReleaseThrows {
            & $fixtureCreator `
                -ReleaseRoot $orchestrationReleaseRoot `
                -ReleaseId 'fixture-source-drift' `
                -SkipVerification `
                -DotnetPath $fakeDotnet | Out-Null
        } 'source changes during a build must fail closed' '*source changed*'
    }
    finally {
        Remove-Item Env:\FAKE_LHM_MUTATE_SOURCE -ErrorAction SilentlyContinue
    }
    Assert-LhmReleaseTest (
        -not [System.IO.Directory]::Exists(
            (Join-Path $orchestrationReleaseRoot 'candidates\fixture-source-drift'))
    ) 'source drift must publish no candidate'

    $fixtureStaging = Join-Path $orchestrationReleaseRoot '.staging'
    Assert-LhmReleaseTest (
        -not [System.IO.Directory]::Exists($fixtureStaging) -or
        @(Get-ChildItem -LiteralPath $fixtureStaging -Force).Count -eq 0
    ) 'failed fixture runs must leave no staging residue'

    $candidate = Join-Path $externalRoot 'candidates\fixed-candidate'
    $packages = Join-Path $candidate 'packages'
    [System.IO.Directory]::CreateDirectory($packages) | Out-Null
    $packageRecords = @()
    foreach ($framework in @('net10.0-windows', 'net472')) {
        $payload = Join-Path $testRoot ('payload-' + $framework)
        [System.IO.Directory]::CreateDirectory((Join-Path $payload 'nested')) | Out-Null
        [System.IO.File]::Copy(
            $whereExecutable,
            (Join-Path $payload 'LibreHardwareMonitor.Windows.Forms.exe'),
            $false)
        [System.IO.File]::WriteAllText(
            (Join-Path $payload 'nested\dependency.dll'),
            "fake dependency $framework")
        $archiveName = "LibreHardwareMonitor-fixed-win-x64-$framework.zip"
        $archivePath = Join-Path $packages $archiveName
        $files = @(
            New-LhmReleaseZip `
                -PayloadRoot $payload `
                -Destination $archivePath `
                -TimestampUtc ([datetime]'2030-01-01T00:00:00Z')
        )
        $archiveItem = Get-Item -LiteralPath $archivePath
        $packageRecords += [ordered]@{
            targetFramework = $framework
            platform = 'x64'
            runtimeIdentifier = 'win-x64'
            selfContained = $false
            entryPoint = 'LibreHardwareMonitor.Windows.Forms.exe'
            fileVersion = [string]$whereVersionInfo.FileVersion
            productVersion = [string]$whereVersionInfo.ProductVersion
            archive = 'packages/' + $archiveName
            length = [long]$archiveItem.Length
            sha256 = Get-LhmReleaseFileSha256 -Path $archivePath
            files = $files
        }
    }

    $manifest = [ordered]@{
        schema = 'sq.lhm-release'
        version = 1
        releaseId = 'fixed-candidate'
        createdUtc = '2030-01-01T00:00:00.0000000Z'
        baseVersion = $fixtureBaseVersion
        sdkVersion = '10.0.302'
        configuration = 'Release'
        platform = 'x64'
        source = [ordered]@{
            commit = ('a' * 40)
            branch = 'main'
            repository = 'example.invalid/librehardwaremonitor/fixed-candidate.git'
            clean = $true
            changes = @()
            fingerprintSha256 = ('b' * 64)
            fileCount = 4
        }
        verification = [ordered]@{
            completed = $true
            commands = @(Get-LhmReleaseVerificationCommands)
        }
        promotable = $true
        packages = $packageRecords
    }
    Write-LhmReleaseJsonAtomically -Value $manifest -Path (Join-Path $candidate 'release-manifest.json')
    $verified = Test-LhmReleaseCandidate -CandidatePath $candidate
    Assert-LhmReleaseTest ($verified.Status -eq 'VERIFIED') 'complete candidate should verify'
    Assert-LhmReleaseTest ($verified.Packages -eq 2) 'candidate should expose both framework packages'

    $candidateScript = Join-Path $PSScriptRoot 'Test-LhmReleaseCandidate.ps1'
    $wrapperResult = @(
        & $candidateScript -CandidatePath $candidate -ReleaseRoot $externalRoot
    )
    Assert-LhmReleaseTest ($wrapperResult.Status -contains 'VERIFIED') 'candidate wrapper should verify an explicit external candidate'

    $unexpectedRootFile = Join-Path $candidate 'unexpected.txt'
    [System.IO.File]::WriteAllText($unexpectedRootFile, 'unexpected')
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'candidate verification must reject undeclared root files' '*must contain only*'
    [System.IO.File]::Delete($unexpectedRootFile)

    $unexpectedPackage = Join-Path $packages 'unexpected.zip'
    [System.IO.File]::WriteAllText($unexpectedPackage, 'unexpected')
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'candidate verification must reject undeclared package files' '*undeclared or missing*'
    [System.IO.File]::Delete($unexpectedPackage)

    $manifestPath = Join-Path $candidate 'release-manifest.json'
    $originalManifestJson = Get-Content -Raw -LiteralPath $manifestPath

    foreach ($requiredSourceProperty in @(
        'repository',
        'fingerprintSha256',
        'fileCount')) {
        $missingSourceManifest = $originalManifestJson | ConvertFrom-Json
        $missingSourceManifest.source.PSObject.Properties.Remove(
            $requiredSourceProperty)
        [System.IO.File]::WriteAllText(
            $manifestPath,
            ($missingSourceManifest | ConvertTo-Json -Depth 16),
            [System.Text.UTF8Encoding]::new($false))
        Assert-LhmReleaseThrows {
            Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
        } "source.$requiredSourceProperty must be required" "*missing required property '$requiredSourceProperty'*"
    }
    [System.IO.File]::WriteAllText(
        $manifestPath,
        $originalManifestJson,
        [System.Text.UTF8Encoding]::new($false))

    $invalidPromotableBooleanManifest = $originalManifestJson | ConvertFrom-Json
    $invalidPromotableBooleanManifest.promotable = 'true'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($invalidPromotableBooleanManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'promotable must remain a strict JSON Boolean' '*promotable*Boolean*'

    $invalidCleanBooleanManifest = $originalManifestJson | ConvertFrom-Json
    $invalidCleanBooleanManifest.source.clean = 1
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($invalidCleanBooleanManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'source.clean must remain a strict JSON Boolean' '*source.clean*Boolean*'

    $incoherentCleanManifest = $originalManifestJson | ConvertFrom-Json
    $incoherentCleanManifest.source.changes = @('?? drift.txt')
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($incoherentCleanManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'source.clean must agree with whether source.changes is empty' '*clean state does not match*'

    $emptyBaseVersionManifest = $originalManifestJson | ConvertFrom-Json
    $emptyBaseVersionManifest.baseVersion = ''
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($emptyBaseVersionManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'empty release base version metadata must be rejected' '*baseVersion*must be a string*'

    $emptyFileVersionManifest = $originalManifestJson | ConvertFrom-Json
    $emptyFileVersionManifest.packages[0].fileVersion = ''
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($emptyFileVersionManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'empty package file-version metadata must be rejected' '*fileVersion*must be a string*'

    $invalidProductVersionManifest = $originalManifestJson | ConvertFrom-Json
    $invalidProductVersionManifest.packages[0].productVersion = 'not-a-version'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($invalidProductVersionManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'invalid package product-version metadata must be rejected' '*versions are invalid*'

    $invalidRuntimeIdentifierManifest = $originalManifestJson | ConvertFrom-Json
    $invalidRuntimeIdentifierManifest.packages[0].runtimeIdentifier = 'win-x86'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($invalidRuntimeIdentifierManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'release packages must remain win-x64' '*runtimeIdentifier*win-x64*'

    $invalidSelfContainedManifest = $originalManifestJson | ConvertFrom-Json
    $invalidSelfContainedManifest.packages[0].selfContained = 'false'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($invalidSelfContainedManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'selfContained must remain a strict JSON Boolean' '*selfContained*Boolean*'

    $runtimeSubtreeManifest = $originalManifestJson | ConvertFrom-Json
    $runtimeSubtreeManifest.packages[0].files[0].path =
        'runtimes/linux-x64/rogue.dll'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($runtimeSubtreeManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'RID-specific packages must reject runtimes subtrees' '*cannot contain a runtimes subtree*'

    [System.IO.File]::WriteAllText(
        $manifestPath,
        $originalManifestJson,
        [System.Text.UTF8Encoding]::new($false))

    $invalidPromotionManifest = $originalManifestJson | ConvertFrom-Json
    $invalidPromotionManifest.verification.completed = $false
    $invalidPromotionManifest.verification.commands = @()
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($invalidPromotionManifest | ConvertTo-Json -Depth 16),
        [System.Text.UTF8Encoding]::new($false))
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'promotable candidates must prove completed verification' '*completed verification*'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        $originalManifestJson,
        [System.Text.UTF8Encoding]::new($false))

    $directoryEntryArchive = Join-Path $packages 'LibreHardwareMonitor-fixed-win-x64-net472.zip'
    $directoryEntryArchiveBackup = Join-Path $testRoot 'net472-before-directory-entry.zip'
    Copy-Item `
        -LiteralPath $directoryEntryArchive `
        -Destination $directoryEntryArchiveBackup
    try {
        Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        $archiveForUpdate = [System.IO.Compression.ZipFile]::Open(
            $directoryEntryArchive,
            [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            $archiveForUpdate.CreateEntry('unsafe-directory/') | Out-Null
        }
        finally {
            $archiveForUpdate.Dispose()
        }

        $directoryEntryManifest = $originalManifestJson | ConvertFrom-Json
        $directoryEntryPackage = @(
            $directoryEntryManifest.packages |
                Where-Object { $_.targetFramework -ceq 'net472' }
        )[0]
        $directoryEntryArchiveItem = Get-Item -LiteralPath $directoryEntryArchive
        $directoryEntryPackage.length = [long]$directoryEntryArchiveItem.Length
        $directoryEntryPackage.sha256 = Get-LhmReleaseFileSha256 -Path $directoryEntryArchive
        [System.IO.File]::WriteAllText(
            $manifestPath,
            ($directoryEntryManifest | ConvertTo-Json -Depth 16),
            [System.Text.UTF8Encoding]::new($false))

        Assert-LhmReleaseThrows {
            Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
        } 'candidate verification must reject explicit ZIP directory entries even when archive metadata is recalculated' '*directory entries*'
    }
    finally {
        [System.IO.File]::Copy(
            $directoryEntryArchiveBackup,
            $directoryEntryArchive,
            $true)
        [System.IO.File]::WriteAllText(
            $manifestPath,
            $originalManifestJson,
            [System.Text.UTF8Encoding]::new($false))
    }
    Assert-LhmReleaseTest (
        (Test-LhmReleaseCandidate -CandidatePath $candidate).Status -eq 'VERIFIED'
    ) 'candidate must verify again after the directory-entry fixture is restored'

    $tamperedArchive = Join-Path $packages 'LibreHardwareMonitor-fixed-win-x64-net472.zip'
    $appendStream = [System.IO.File]::Open(
        $tamperedArchive,
        [System.IO.FileMode]::Append,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $appendStream.WriteByte(0)
    }
    finally {
        $appendStream.Dispose()
    }
    Assert-LhmReleaseThrows {
        Test-LhmReleaseCandidate -CandidatePath $candidate | Out-Null
    } 'candidate verification must reject modified archive bytes' '*length mismatch*'

    Assert-LhmReleaseThrows {
        New-LhmReleaseZip `
            -PayloadRoot (Join-Path $testRoot 'payload-net472') `
            -Destination $tamperedArchive `
            -TimestampUtc ([datetime]'2030-01-01T00:00:00Z') | Out-Null
    } 'archive creation must refuse overwrite' '*already exists*'

    [xml]$agaProject = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Aga.Controls\Aga.Controls.csproj')
    [xml]$formsProject = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj')
    [xml]$libraryProject = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'LibreHardwareMonitorLib\LibreHardwareMonitorLib.csproj')
    $agaOutput = $agaProject.SelectSingleNode('/Project/PropertyGroup/OutputPath')
    $formsOutput = $formsProject.SelectSingleNode('/Project/PropertyGroup/OutputPath')
    Assert-LhmReleaseTest ($agaOutput.Condition -like '*UseArtifactsOutput*') 'Aga output must yield to SDK artifacts routing'
    Assert-LhmReleaseTest ($formsOutput.Condition -like '*UseArtifactsOutput*') 'WinForms output must yield to SDK artifacts routing'
    $libraryOutputs = @($libraryProject.SelectNodes('/Project/PropertyGroup[OutputPath]'))
    Assert-LhmReleaseTest (
        @($libraryOutputs | Where-Object { $_.Condition -notlike '*UseArtifactsOutput*' }).Count -eq 0
    ) 'library outputs must yield to SDK artifacts routing'

    $globalJson = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'global.json') | ConvertFrom-Json
    Assert-LhmReleaseTest ($globalJson.sdk.version -eq '10.0.302') 'release SDK should be pinned to installed stable 10.0.302'
    Assert-LhmReleaseTest ($globalJson.sdk.rollForward -ceq 'latestPatch') 'release SDK roll-forward policy should stay within the pinned feature band'
    Assert-LhmReleaseTest (-not [bool]$globalJson.sdk.allowPrerelease) 'release SDK must reject prerelease selection'

    $projectConfig = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot '.codex\skills\project.toml')
    $releaseBuildGateMatch = [System.Text.RegularExpressions.Regex]::Match(
        $projectConfig,
        '(?ms)^\[build-gate\.release-candidate\]\s*(?<body>.*?)(?=^\[|\z)')
    Assert-LhmReleaseTest (
        $releaseBuildGateMatch.Success
    ) 'project config should declare the release-candidate build gate'
    $releaseVerifyMatch = [System.Text.RegularExpressions.Regex]::Match(
        $releaseBuildGateMatch.Groups['body'].Value,
        '(?m)^verify\s*=\s*"(?<command>[^"]+)"')
    Assert-LhmReleaseTest (
        $releaseVerifyMatch.Success
    ) 'release-candidate build gate should declare a verification command'
    Assert-LhmReleaseTest (
        $releaseVerifyMatch.Groups['command'].Value.Contains('-RequirePromotable')
    ) 'release-candidate build gate must require promotable output'
    Assert-LhmReleaseTest (
        $releaseVerifyMatch.Groups['command'].Value.Contains('-RequireCurrentSource')
    ) 'release-candidate build gate must require the current source state'

    $fingerprintBefore = Get-LhmGitSourceFingerprint -RepositoryRoot $repositoryRoot
    $fingerprintAfter = Get-LhmGitSourceFingerprint -RepositoryRoot $repositoryRoot
    Assert-LhmReleaseTest ($fingerprintBefore.FileCount -gt 0) 'source fingerprint should cover repository files'
    Assert-LhmReleaseTest ($fingerprintBefore.Sha256 -match '^[0-9a-f]{64}$') 'source fingerprint should be canonical SHA-256'
    Assert-LhmReleaseTest ($fingerprintBefore.Sha256 -ceq $fingerprintAfter.Sha256) 'unchanged source fingerprint should be stable'

    Write-Output "PASS: release-system path, cleanup, immutable ZIP, manifest, tamper, routing, and SDK checks ($script:assertionCount assertions)"
}
finally {
    if ([System.IO.Directory]::Exists($testRoot)) {
        $resolvedCleanup = Resolve-LhmReleaseFullPath -Path $testRoot
        if (-not $resolvedCleanup.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean release test path outside temp: $resolvedCleanup"
        }
        Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force
    }
}

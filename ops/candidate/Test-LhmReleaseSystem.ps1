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
$standaloneCleanupScript = Join-Path $repositoryRoot 'eng\Clear-LhmRepositoryBuildOutputs.ps1'

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
    [System.IO.File]::WriteAllText((Join-Path $fakeRepository 'LibreHardwareMonitor.sln'), '')
    [System.IO.File]::WriteAllText((Join-Path $fakeRepository 'Directory.Build.props'), '<Project />')
    [System.IO.File]::WriteAllText((Join-Path $fakeRepository 'Directory.Packages.props'), '<Project />')
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

    $bareRepository = Join-Path $testRoot 'bare-repository'
    & git init -q --bare $bareRepository
    if ($LASTEXITCODE -ne 0) { throw 'Bare-repository fixture Git initialization failed.' }
    Assert-LhmReleaseThrows {
        Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $bareRepository | Out-Null
    } 'build-output discovery must reject a bare repository' '*valid Git checkout*'

    $generatedPaths = @(
        (Join-Path $fakeRepository 'bin'),
        (Join-Path $fakeRepository 'obj'),
        (Join-Path $subProject 'bin'),
        (Join-Path $subProject 'obj'))
    foreach ($generatedPath in $generatedPaths) {
        [System.IO.Directory]::CreateDirectory($generatedPath) | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $generatedPath 'stale.bin'), 'stale')
    }
    $unrelated = Join-Path $fakeRepository 'keep.txt'
    [System.IO.File]::WriteAllText($unrelated, 'keep')

    # Cleanup must refuse while a process is loaded from a target tree.
    $guardExecutable = Join-Path $fakeRepository 'bin\guardprobe.exe'
    [System.IO.File]::Copy(
        (Join-Path $env:WINDIR 'System32\cmd.exe'),
        $guardExecutable,
        $false)
    $guardProcess = Start-Process `
        -FilePath $guardExecutable `
        -ArgumentList '/d', '/c', 'ping -n 30 127.0.0.1 >nul' `
        -PassThru `
        -WindowStyle Hidden
    try {
        $guardDeadline = [datetime]::UtcNow.AddSeconds(5)
        $guardObservation = $null
        do {
            $guardObservation = Get-CimInstance `
                -ClassName Win32_Process `
                -Filter "ProcessId = $($guardProcess.Id)" `
                -Property ProcessId, ExecutablePath `
                -ErrorAction Stop
            if ($null -ne $guardObservation -and
                -not [string]::IsNullOrWhiteSpace($guardObservation.ExecutablePath)) {
                break
            }
            Start-Sleep -Milliseconds 100
        } while ([datetime]::UtcNow -lt $guardDeadline -and -not $guardProcess.HasExited)

        Assert-LhmReleaseTest (
            $null -ne $guardObservation -and
            (Resolve-LhmReleaseFullPath -Path $guardObservation.ExecutablePath).Equals(
                (Resolve-LhmReleaseFullPath -Path $guardExecutable),
                [System.StringComparison]::OrdinalIgnoreCase)
        ) 'process-guard fixture must run from the exact build-output path'
        Assert-LhmReleaseThrows {
            Clear-LhmRepositoryBuildOutput -RepositoryRoot $fakeRepository | Out-Null
        } 'cleanup must reject build output loaded by a process' '*loaded by a process*'
        Assert-LhmReleaseThrows {
            & $standaloneCleanupScript -RepositoryRoot $fakeRepository -WhatIf | Out-Null
        } 'standalone cleanup must reject build output loaded by a process before deletion' '*loaded by a process*'
        Assert-LhmReleaseTest (
            [System.IO.File]::Exists($guardExecutable)
        ) 'rejected process-loaded cleanup must preserve the build-output tree'
        Assert-LhmReleaseTest (
            @($generatedPaths | Where-Object {
                -not [System.IO.File]::Exists((Join-Path $_ 'stale.bin'))
            }).Count -eq 0
        ) 'rejected process-loaded cleanup must preserve every discovered build-output tree'
    }
    finally {
        if ($null -ne $guardProcess -and -not $guardProcess.HasExited) {
            Stop-Process -Id $guardProcess.Id -Force -ErrorAction Stop
            if (-not $guardProcess.WaitForExit(5000)) {
                throw "Guard fixture process did not exit: $($guardProcess.Id)"
            }
        }
    }

    # Process inventory must fail closed independently of the caller's error preference.
    function Get-CimInstance {
        [CmdletBinding()]
        param(
            [string]$ClassName,
            [string[]]$Property,
            [string]$Filter
        )

        throw 'injected CIM inventory failure'
    }
    try {
        Assert-LhmReleaseThrows {
            Clear-LhmRepositoryBuildOutput -RepositoryRoot $fakeRepository | Out-Null
        } 'cleanup must fail closed when process inventory fails' '*injected CIM inventory failure*'
        Assert-LhmReleaseThrows {
            & $standaloneCleanupScript -RepositoryRoot $fakeRepository -WhatIf | Out-Null
        } 'standalone cleanup must fail closed when process inventory fails' '*injected CIM inventory failure*'
        Assert-LhmReleaseTest (
            @($generatedPaths | Where-Object {
                -not [System.IO.File]::Exists((Join-Path $_ 'stale.bin'))
            }).Count -eq 0
        ) 'failed process inventory must preserve every discovered build-output tree'
    }
    finally {
        Remove-Item -LiteralPath Function:\Get-CimInstance -Force
    }

    $cachedBuildOutputPaths = @(Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $fakeRepository)
    $firstCachedBuildOutputPath = $cachedBuildOutputPaths[0]
    $cachedBuildOutputPaths[0] = Join-Path $testRoot 'caller-mutation-must-not-stick'
    $freshBuildOutputPaths = @(Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $fakeRepository)
    Assert-LhmReleaseTest (
        $freshBuildOutputPaths[0] -ceq $firstCachedBuildOutputPath
    ) 'build-output path cache must return a defensive array copy'

    $lateProject = Join-Path $fakeRepository 'LateProject'
    [System.IO.Directory]::CreateDirectory($lateProject) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $lateProject 'LateProject.csproj'),
        '<Project />')
    & git -C $fakeRepository add -- LateProject/LateProject.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Late-project fixture Git add failed.' }

    $lateBuildOutputPaths = @(Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $fakeRepository)
    Assert-LhmReleaseTest (
        (Join-Path $lateProject 'bin') -in $lateBuildOutputPaths -and
        (Join-Path $lateProject 'obj') -in $lateBuildOutputPaths
    ) 'build-output path cache must discover a newly tracked project'

    $movedProject = Join-Path $fakeRepository 'MovedProject'
    [System.IO.Directory]::CreateDirectory($movedProject) | Out-Null
    & git -C $fakeRepository mv -- LateProject/LateProject.csproj MovedProject/MovedProject.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Moved-project fixture Git move failed.' }

    $movedBuildOutputPaths = @(Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $fakeRepository)
    $movedGeneratedPaths = @(
        (Join-Path $movedProject 'bin'),
        (Join-Path $movedProject 'obj'))
    Assert-LhmReleaseTest (
        $movedGeneratedPaths[0] -in $movedBuildOutputPaths -and
        $movedGeneratedPaths[1] -in $movedBuildOutputPaths -and
        (Join-Path $lateProject 'bin') -notin $movedBuildOutputPaths -and
        (Join-Path $lateProject 'obj') -notin $movedBuildOutputPaths
    ) 'build-output path cache must invalidate after a tracked project move'
    $generatedPaths += $movedGeneratedPaths
    foreach ($generatedPath in $movedGeneratedPaths) {
        [System.IO.Directory]::CreateDirectory($generatedPath) | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $generatedPath 'stale.bin'), 'stale')
    }

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
    Assert-LhmReleaseTest ($cleaned.Count -eq 6) 'cleanup should target each exact generated root once'
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
    [System.IO.File]::WriteAllText((Join-Path $trackedOutputRepository 'LibreHardwareMonitor.sln'), '')
    [System.IO.File]::WriteAllText((Join-Path $trackedOutputRepository 'Directory.Build.props'), '<Project />')
    [System.IO.File]::WriteAllText((Join-Path $trackedOutputRepository 'Directory.Packages.props'), '<Project />')
    [System.IO.File]::WriteAllText(
        (Join-Path $trackedOutputRepository '.gitignore'),
        "[Bb]in/`r`n[Oo]bj/`r`n")
    [System.IO.File]::WriteAllText($trackedOutputPath, 'tracked output must survive')
    & git -C $trackedOutputRepository init -q -b main
    if ($LASTEXITCODE -ne 0) { throw 'Tracked-output fixture Git initialization failed.' }
    & git -C $trackedOutputRepository config user.name 'Release Fixture'
    & git -C $trackedOutputRepository config user.email 'release-fixture@example.invalid'
    & git -C $trackedOutputRepository config core.autocrlf false
    & git -C $trackedOutputRepository add -- .gitignore TrackedOutput.csproj LibreHardwareMonitor.sln Directory.Build.props Directory.Packages.props
    & git -C $trackedOutputRepository add -f -- Bin/tracked.keep
    & git -C $trackedOutputRepository commit -q -m 'tracked output fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Tracked-output fixture Git commit failed.' }
    Assert-LhmReleaseThrows {
        Clear-LhmRepositoryBuildOutput -RepositoryRoot $trackedOutputRepository | Out-Null
    } 'cleanup must reject case-variant tracked files beneath an otherwise ignored bin target' '*containing tracked files*'
    Assert-LhmReleaseThrows {
        & $standaloneCleanupScript -RepositoryRoot $trackedOutputRepository -WhatIf | Out-Null
    } 'standalone cleanup must reject case-variant tracked files before deletion' '*containing tracked files*'
    Assert-LhmReleaseTest (
        [System.IO.File]::Exists($trackedOutputPath)
    ) 'rejected tracked-output cleanup must preserve the tracked file'
    Assert-LhmReleaseTest (
        [System.IO.File]::ReadAllText($trackedOutputPath) -ceq 'tracked output must survive'
    ) 'rejected tracked-output cleanup must preserve tracked file contents'

    $orchestrationRepository = Join-Path $testRoot 'orchestration-repository'
    $orchestrationReleaseRoot = Join-Path $testRoot 'orchestration-releases'
    $orchestrationScripts = Join-Path $orchestrationRepository 'ops\candidate'
    $orchestrationLiveScripts = Join-Path $orchestrationRepository 'ops\live-verification'
    [System.IO.Directory]::CreateDirectory($orchestrationScripts) | Out-Null
    [System.IO.Directory]::CreateDirectory($orchestrationLiveScripts) | Out-Null
    foreach ($scriptName in @(
        'LhmRelease.Common.ps1',
        'New-LhmRelease.ps1',
        'Test-LhmReleaseCandidate.ps1',
        'Show-LhmExecutableIdentity.ps1')) {
        Copy-Item `
            -LiteralPath (Join-Path $PSScriptRoot $scriptName) `
            -Destination (Join-Path $orchestrationScripts $scriptName)
    }
    Copy-Item `
        -LiteralPath (Join-Path $PSScriptRoot '..\live-verification\LhmLiveManifest.Common.ps1') `
        -Destination (Join-Path $orchestrationLiveScripts 'LhmLiveManifest.Common.ps1')
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
    $smokeFixtureSource = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

internal static class Program
{
    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--release-smoke")
            return 19;

        string executablePath = Environment.ProcessPath;
        if (String.IsNullOrWhiteSpace(executablePath))
            return 24;
        executablePath = Path.GetFullPath(executablePath);
        string frameworkPath = Path.Combine(
            Path.GetDirectoryName(executablePath),
            "fixture-framework.txt");
        if (!File.Exists(frameworkPath))
            return 25;
        string framework = File.ReadAllText(frameworkPath).Trim();
        if (Environment.GetEnvironmentVariable("FAKE_LHM_SMOKE_FAIL_FRAMEWORK") == framework)
            return 23;

        string receiptPath = Environment.GetEnvironmentVariable("LHM_RELEASE_SMOKE_RECEIPT");
        if (String.IsNullOrWhiteSpace(receiptPath) || !Path.IsPathRooted(receiptPath))
            return 20;

        FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
        string json =
            "{\"schema\":\"sq.lhm-release-smoke\",\"version\":1," +
            "\"targetFramework\":\"" + Escape(framework) + "\"," +
            "\"executablePath\":\"" + Escape(executablePath) + "\"," +
            "\"fileVersion\":\"" + Escape(version.FileVersion) + "\"," +
            "\"productVersion\":\"" + Escape(version.ProductVersion) + "\"}";
        using (StreamWriter writer = new StreamWriter(
            new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None),
            new UTF8Encoding(false)))
        {
            writer.Write(json);
        }

        string logPath = Environment.GetEnvironmentVariable("FAKE_LHM_SMOKE_LOG");
        if (!String.IsNullOrWhiteSpace(logPath))
            File.AppendAllText(logPath, framework + "\t" + executablePath + Environment.NewLine);
        return 0;
    }
}
'@
    $smokeFixtureProjectRoot = Join-Path $testRoot 'smoke-fixture-project'
    $smokeFixtureArtifacts = Join-Path $testRoot 'smoke-fixture-artifacts'
    $smokeFixtureTemplateRoot = Join-Path $testRoot 'smoke-fixture-templates'
    [System.IO.Directory]::CreateDirectory($smokeFixtureProjectRoot) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $smokeFixtureProjectRoot 'Program.cs'),
        $smokeFixtureSource,
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $smokeFixtureProjectRoot 'ReleaseSmokeFixture.csproj'),
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
    <AssemblyName>LibreHardwareMonitor.Windows.Forms</AssemblyName>
    <Version>$fixtureBaseVersion</Version>
    <AssemblyVersion>$($fixtureBaseVersion).0</AssemblyVersion>
    <FileVersion>$($fixtureBaseVersion).1</FileVersion>
    <InformationalVersion>$($fixtureBaseVersion)+fixture</InformationalVersion>
  </PropertyGroup>
</Project>
"@,
        [System.Text.UTF8Encoding]::new($false))
    $realDotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $smokeFrameworkRevisions = [ordered]@{
        'net10.0-windows' = 1
        'net472' = 2
    }
    foreach ($smokeFramework in $smokeFrameworkRevisions.Keys) {
        $revision = [int]$smokeFrameworkRevisions[$smokeFramework]
        $frameworkArtifacts = Join-Path $smokeFixtureArtifacts (
            $smokeFramework -replace '[^A-Za-z0-9]', '')
        $smokeFixtureBuildOutput = @(
            & $realDotnet build `
                (Join-Path $smokeFixtureProjectRoot 'ReleaseSmokeFixture.csproj') `
                -c Release `
                -r win-x64 `
                --self-contained false `
                --artifacts-path $frameworkArtifacts `
                "-p:AssemblyTitle=LibreHardwareMonitor fixture $revision" `
                --nologo
        )
        if ($LASTEXITCODE -ne 0) {
            throw (
                "SDK smoke fixture build failed for $smokeFramework`: " +
                ([string[]]$smokeFixtureBuildOutput -join "`n"))
        }
        $smokeFixtureExecutables = @(
            Get-ChildItem -LiteralPath $frameworkArtifacts `
                -Filter 'LibreHardwareMonitor.Windows.Forms.exe' `
                -File `
                -Recurse |
                Where-Object { $_.FullName -match '[\\/]bin[\\/]' }
        )
        if ($smokeFixtureExecutables.Count -ne 1) {
            throw (
                "Expected one SDK-built $smokeFramework smoke fixture executable; " +
                "found $($smokeFixtureExecutables.Count).")
        }
        $templatePath = Join-Path $smokeFixtureTemplateRoot (
            $smokeFramework -replace '[^A-Za-z0-9]', '')
        [System.IO.Directory]::CreateDirectory($templatePath) | Out-Null
        foreach ($fixtureEntry in @(Get-ChildItem -LiteralPath $smokeFixtureExecutables[0].DirectoryName -Force)) {
            Copy-Item -LiteralPath $fixtureEntry.FullName -Destination $templatePath -Recurse -Force
        }
    }
    $env:FAKE_LHM_SMOKE_PAYLOAD_TEMPLATE_ROOT = $smokeFixtureTemplateRoot
    [System.IO.File]::WriteAllText(
        (Join-Path $orchestrationRepository 'Directory.Build.props'),
        "<Project><PropertyGroup><Version>$fixtureBaseVersion</Version></PropertyGroup></Project>")
    [System.IO.File]::WriteAllText(
        (Join-Path $orchestrationRepository 'global.json'),
        @'
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "disable",
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
$fixturePayloadTemplateRoot = $env:FAKE_LHM_SMOKE_PAYLOAD_TEMPLATE_ROOT
if ([string]::IsNullOrWhiteSpace($fixturePayloadTemplateRoot)) {
    exit 94
}
$fixturePayloadTemplate = Join-Path $fixturePayloadTemplateRoot (
    $framework -replace '[^A-Za-z0-9]', '')
if (-not [System.IO.Directory]::Exists($fixturePayloadTemplate)) {
    exit 94
}
foreach ($fixtureEntry in @(Get-ChildItem -LiteralPath $fixturePayloadTemplate -Force)) {
    Copy-Item -LiteralPath $fixtureEntry.FullName -Destination $output -Recurse -Force
}
[System.IO.File]::WriteAllText(
    (Join-Path $output 'fixture-framework.txt'),
    $framework,
    [System.Text.UTF8Encoding]::new($false))
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
    $fakeSmokeInvocationLog = Join-Path $testRoot 'fake-smoke-invocations.log'
    try {
        $env:FAKE_LHM_INVOCATION_LOG = $fakeDotnetInvocationLog
        $env:FAKE_LHM_SMOKE_LOG = $fakeSmokeInvocationLog
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
        Remove-Item Env:\FAKE_LHM_SMOKE_LOG -ErrorAction SilentlyContinue
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
        $fixtureManifest.version -eq 2
    ) 'new release candidates should use manifest version 2 for package-smoke evidence'
    Assert-LhmReleaseTest (
        @($fixtureManifest.packages | Where-Object {
            $null -eq $_.smoke -or
            $_.smoke.completed -ne $true -or
            $_.smoke.targetFramework -cne $_.targetFramework -or
            $_.smoke.fileVersion -cne $_.fileVersion -or
            $_.smoke.productVersion -cne $_.productVersion
        }).Count -eq 0
    ) 'every package should bind a successful extracted-entry-point smoke receipt to its versions'
    $smokeInvocations = @(Get-Content -LiteralPath $fakeSmokeInvocationLog)
    Assert-LhmReleaseTest (
        $smokeInvocations.Count -eq 2 -and
        @($smokeInvocations | Where-Object {
            $_ -notmatch '^(net10\.0-windows|net472)\t.+LibreHardwareMonitor\.Windows\.Forms\.exe$'
        }).Count -eq 0
    ) 'candidate creation should execute both exact ZIP-extracted framework entry points'
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

    $identityProbeRoot = Join-Path $testRoot 'identity-probe'
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        (Join-Path $fixtureCandidate ($fixtureManifest.packages[0].archive.Replace('/', '\'))),
        $identityProbeRoot)
    $identityProbePath = Join-Path $identityProbeRoot $fixtureManifest.packages[0].entryPoint
    $identityScript = Join-Path $orchestrationScripts 'Show-LhmExecutableIdentity.ps1'
    $candidateIdentityBefore = Get-LhmCandidateContentIdentity -CandidatePath $fixtureCandidate
    $candidateIdentityJson = @(
        & $identityScript `
            -Path $identityProbePath `
            -ReleaseRoot $orchestrationReleaseRoot `
            -Json
    ) -join "`n"
    $candidateIdentity = ConvertFrom-LhmReleaseJson -Json $candidateIdentityJson
    Assert-LhmReleaseTest (
        $candidateIdentity.role -ceq 'candidate-payload'
    ) 'identity command should classify an extracted manifest-matching entry point as candidate payload'
    Assert-LhmReleaseTest (
        $candidateIdentity.sha256 -ceq (Get-LhmReleaseFileSha256 -Path $identityProbePath)
    ) 'identity command should report the exact executable SHA-256'
    Assert-LhmReleaseTest (
        @($candidateIdentity.candidateMatches).Count -eq 1 -and
        @($candidateIdentity.candidateMatches | Where-Object {
            $_.releaseId -cne 'fixture-success' -or
            $_.targetFramework -cne $fixtureManifest.packages[0].targetFramework -or
            $_.validationStatus -cne 'VERIFIED'
        }).Count -eq 0
    ) ("identity command should name and validate the exact matching framework payload in the immutable candidate: " +
        (($candidateIdentity.candidateMatches | ConvertTo-Json -Depth 8 -Compress) -join ''))
    Assert-LhmCandidateContentUnchanged `
        -CandidatePath $fixtureCandidate `
        -Expected $candidateIdentityBefore

    $mixedIdentityReleaseRoot = Join-Path $testRoot 'identity-mixed-release'
    $mixedIdentityCandidateRoot = [System.IO.Directory]::CreateDirectory(
        (Join-Path $mixedIdentityReleaseRoot 'candidates')).FullName
    Copy-Item `
        -LiteralPath $fixtureCandidate `
        -Destination (Join-Path $mixedIdentityCandidateRoot 'fixture-success') `
        -Recurse
    $malformedIdentityCandidate = [System.IO.Directory]::CreateDirectory(
        (Join-Path $mixedIdentityCandidateRoot 'fixture-malformed')).FullName
    [System.IO.File]::WriteAllText(
        (Join-Path $malformedIdentityCandidate 'release-manifest.json'),
        '{malformed-json',
        [System.Text.UTF8Encoding]::new($false))
    $mixedCandidateIdentityJson = @(
        & $identityScript `
            -Path $identityProbePath `
            -ReleaseRoot $mixedIdentityReleaseRoot `
            -Json
    ) -join "`n"
    $mixedCandidateIdentity = ConvertFrom-LhmReleaseJson -Json $mixedCandidateIdentityJson
    Assert-LhmReleaseTest (
        $mixedCandidateIdentity.role -ceq 'candidate-payload' -and
        $mixedCandidateIdentity.authority -ceq 'candidate-manifest' -and
        $mixedCandidateIdentity.status -ceq 'INCOMPLETE' -and
        $mixedCandidateIdentity.candidateScan.status -ceq 'incomplete' -and
        @($mixedCandidateIdentity.candidateScan.diagnostics | Where-Object {
            $_.candidatePath -ceq $malformedIdentityCandidate -and
            -not [string]::IsNullOrWhiteSpace([string]$_.error)
        }).Count -eq 1
    ) 'identity command should retain an exact valid match while reporting a malformed unrelated candidate as incomplete evidence'

    $invalidIdentityReleaseRoot = Join-Path $testRoot 'identity-invalid-release'
    $invalidIdentityCandidate = Join-Path (
        [System.IO.Directory]::CreateDirectory(
            (Join-Path $invalidIdentityReleaseRoot 'candidates')).FullName
    ) 'fixture-invalid'
    Copy-Item `
        -LiteralPath $fixtureCandidate `
        -Destination $invalidIdentityCandidate `
        -Recurse
    $invalidCandidateIdentityJson = @(
        & $identityScript `
            -Path $identityProbePath `
            -ReleaseRoot $invalidIdentityReleaseRoot `
            -Json
    ) -join "`n"
    $invalidCandidateIdentity = ConvertFrom-LhmReleaseJson -Json $invalidCandidateIdentityJson
    Assert-LhmReleaseTest (
        $invalidCandidateIdentity.role -ceq 'candidate-payload-invalid' -and
        $invalidCandidateIdentity.authority -ceq 'none' -and
        @($invalidCandidateIdentity.candidateMatches).Count -eq 1 -and
        $invalidCandidateIdentity.candidateMatches[0].validationStatus -ceq 'INVALID'
    ) 'identity command must not grant candidate authority from a manifest whose candidate fails validation'

    $developmentProbePath = Join-Path $orchestrationRepository 'bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe'
    [System.IO.Directory]::CreateDirectory(
        [System.IO.Path]::GetDirectoryName($developmentProbePath)) | Out-Null
    [System.IO.File]::Copy($whereExecutable, $developmentProbePath, $false)
    $developmentIdentityJson = @(
        & $identityScript -Path $developmentProbePath -Json
    ) -join "`n"
    $developmentIdentity = ConvertFrom-LhmReleaseJson -Json $developmentIdentityJson
    Assert-LhmReleaseTest (
        $developmentIdentity.role -ceq 'development-build' -and
        $developmentIdentity.checkout.root -ceq $orchestrationRepository
    ) 'identity command should classify repo-root bin output against its enclosing checkout'

    $stackRoot = Join-Path $testRoot 'identity-stack'
    $liveRoot = Join-Path $stackRoot 'deployments\current'
    $rollbackRoot = Join-Path $stackRoot 'deployments\rollback'
    $logManagementRoot = Join-Path $stackRoot 'operations\log-management'
    $manifestRoot = Join-Path $stackRoot 'manifests'
    $liveManifestRoot = Join-Path $stackRoot 'manifests\channels'
    [System.IO.Directory]::CreateDirectory($liveRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($rollbackRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($logManagementRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($liveManifestRoot) | Out-Null
    $livePath = Join-Path $liveRoot 'LibreHardwareMonitor.Windows.Forms.exe'
    [System.IO.File]::Copy($identityProbePath, $livePath, $false)
    $layoutManifest = [ordered]@{
        schema = 'sq.librehw-layout'
        version = 1
        machineId = 'fixture-host'
        computerName = [System.Environment]::MachineName
        operations = [ordered]@{
            root = $stackRoot
            live = $liveRoot
            logManagement = $logManagementRoot
        }
    }
    $consumerManifest = [ordered]@{
        schema = 'sq.librehw-consumers'
        version = 1
        machineId = 'fixture-host'
        bindings = @(
            [ordered]@{
                kind = 'scheduledTask'
                name = '\Fixture-Lhm-Live'
                target = $livePath
            },
            [ordered]@{
                kind = 'scheduledTask'
                name = '\Fixture-Lhm-Logs'
                target = (Join-Path $logManagementRoot 'Invoke-LhmLogManagement.ps1')
            }
        )
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $manifestRoot 'layout.json'),
        ($layoutManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $manifestRoot 'consumers.json'),
        ($consumerManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    $liveManifest = [ordered]@{
        schema = 'sq.librehw-live-channel'
        version = 1
        machineId = 'fixture-host'
        deploymentRoot = $liveRoot
        entryPoint = 'LibreHardwareMonitor.Windows.Forms.exe'
        productVersion = [string]$fixtureManifest.packages[0].productVersion
        sha256 = Get-LhmReleaseFileSha256 -Path $livePath
        sourceCandidate = 'fixture-success'
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $liveManifestRoot 'live.json'),
        ($liveManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    $liveIdentityJson = @(
        & $identityScript `
            -Path $livePath `
            -StackRoot $stackRoot `
            -ReleaseRoot $orchestrationReleaseRoot `
            -Json
    ) -join "`n"
    $liveIdentity = ConvertFrom-LhmReleaseJson -Json $liveIdentityJson
    Assert-LhmReleaseTest (
        $liveIdentity.role -ceq 'live' -and
        $liveIdentity.authority -ceq 'live-manifest' -and
        $liveIdentity.liveManifestMatch -eq $true -and
        $liveIdentity.liveEvidence.status -ceq 'verified' -and
        $liveIdentity.sourceCandidate -ceq 'fixture-success'
    ) 'identity command should give an exact stack-manifest match precedence over candidate-payload classification'

    # Scheduled and sandboxed sessions can run without COMPUTERNAME; the machine
    # binding must come from the OS, not the process environment.
    $savedComputerName = $env:COMPUTERNAME
    try {
        Remove-Item Env:\COMPUTERNAME -ErrorAction SilentlyContinue
        $noComputerNameIdentityJson = @(
            & $identityScript -Path $livePath -StackRoot $stackRoot -Json
        ) -join "`n"
    }
    finally {
        $env:COMPUTERNAME = $savedComputerName
    }
    $noComputerNameIdentity = ConvertFrom-LhmReleaseJson -Json $noComputerNameIdentityJson
    Assert-LhmReleaseTest (
        $noComputerNameIdentity.role -ceq 'live' -and
        $noComputerNameIdentity.liveEvidence.status -ceq 'verified'
    ) 'live identity must not depend on the COMPUTERNAME environment variable'

    $liveManifest.sha256 = '00' * 32
    [System.IO.File]::WriteAllText(
        (Join-Path $liveManifestRoot 'live.json'),
        ($liveManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    $mismatchedLiveIdentityJson = @(
        & $identityScript -Path $livePath -StackRoot $stackRoot -Json
    ) -join "`n"
    $mismatchedLiveIdentity = ConvertFrom-LhmReleaseJson -Json $mismatchedLiveIdentityJson
    Assert-LhmReleaseTest (
        $mismatchedLiveIdentity.role -ceq 'live-mismatch' -and
        $mismatchedLiveIdentity.authority -ceq 'none' -and
        $mismatchedLiveIdentity.liveManifestMatch -eq $false -and
        $mismatchedLiveIdentity.liveEvidence.status -ceq 'mismatch'
    ) 'a live path whose hash does not match the valid live manifest must not receive live authority'
    $liveManifest.sha256 = Get-LhmReleaseFileSha256 -Path $livePath

    $consumerManifest.version = 2
    [System.IO.File]::WriteAllText(
        (Join-Path $manifestRoot 'consumers.json'),
        ($consumerManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $liveManifestRoot 'live.json'),
        ($liveManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    $invalidLiveIdentityJson = @(
        & $identityScript -Path $livePath -StackRoot $stackRoot -Json
    ) -join "`n"
    $invalidLiveIdentity = ConvertFrom-LhmReleaseJson -Json $invalidLiveIdentityJson
    Assert-LhmReleaseTest (
        $invalidLiveIdentity.role -ceq 'live-mismatch' -and
        $invalidLiveIdentity.authority -ceq 'none' -and
        $invalidLiveIdentity.status -ceq 'INCOMPLETE' -and
        $invalidLiveIdentity.liveEvidence.status -ceq 'invalid' -and
        $invalidLiveIdentity.liveEvidence.error -like '*version*expected*'
    ) 'an invalid stack manifest contract must fail closed without granting live authority'
    $consumerManifest.version = 1
    [System.IO.File]::WriteAllText(
        (Join-Path $manifestRoot 'consumers.json'),
        ($consumerManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))

    $layoutManifest.computerName = 'OTHER-FIXTURE-HOST'
    [System.IO.File]::WriteAllText(
        (Join-Path $manifestRoot 'layout.json'),
        ($layoutManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    $wrongMachineLiveIdentityJson = @(
        & $identityScript -Path $livePath -StackRoot $stackRoot -Json
    ) -join "`n"
    $wrongMachineLiveIdentity = ConvertFrom-LhmReleaseJson -Json $wrongMachineLiveIdentityJson
    Assert-LhmReleaseTest (
        $wrongMachineLiveIdentity.role -ceq 'live-mismatch' -and
        $wrongMachineLiveIdentity.authority -ceq 'none' -and
        $wrongMachineLiveIdentity.status -ceq 'INCOMPLETE' -and
        $wrongMachineLiveIdentity.liveEvidence.error -like '*OTHER-FIXTURE-HOST*'
    ) 'live authority must fail closed when the manifest contract names another computer'
    $layoutManifest.computerName = [System.Environment]::MachineName
    [System.IO.File]::WriteAllText(
        (Join-Path $manifestRoot 'layout.json'),
        ($layoutManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))

    $rollbackPacketRoot = Join-Path $rollbackRoot 'fixture-packet'
    $rollbackRuntimeRoot = [System.IO.Directory]::CreateDirectory(
        (Join-Path $rollbackPacketRoot 'runtime')).FullName
    $rollbackPath = Join-Path $rollbackRuntimeRoot 'LibreHardwareMonitor.Windows.Forms.exe'
    [System.IO.File]::Copy($identityProbePath, $rollbackPath, $false)
    $rollbackSha256 = Get-LhmReleaseFileSha256 -Path $rollbackPath
    $rollbackPacketManifest = [ordered]@{
        Schema = 'sq.librehw-rollback-packet'
        Version = 1
        MachineId = 'fixture-host'
        OperationId = 'fixture-packet'
        CreatedUtc = '2026-08-21T00:00:00Z'
        RestoresToEntryPointSha256 = $rollbackSha256
        RestoresToProductVersion = [string]$fixtureManifest.packages[0].productVersion
        FileCount = 1
        Files = @(
            [ordered]@{
                RelativePath = 'runtime/LibreHardwareMonitor.Windows.Forms.exe'
                Length = [System.IO.FileInfo]::new($rollbackPath).Length
                Sha256 = $rollbackSha256
            }
        )
    }
    $rollbackManifestPath = Join-Path $rollbackPacketRoot 'packet-manifest.json'
    [System.IO.File]::WriteAllText(
        $rollbackManifestPath,
        ($rollbackPacketManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    $rollbackIdentityJson = @(
        & $identityScript -Path $rollbackPath -StackRoot $stackRoot -Json
    ) -join "`n"
    $rollbackIdentity = ConvertFrom-LhmReleaseJson -Json $rollbackIdentityJson
    Assert-LhmReleaseTest (
        $rollbackIdentity.role -ceq 'rollback-payload' -and
        $rollbackIdentity.authority -ceq 'rollback-packet' -and
        $rollbackIdentity.rollbackEvidence.status -ceq 'verified'
    ) 'an exact executable record in a valid machine-bound rollback packet should receive rollback authority'

    $undeclaredRollbackFile = Join-Path $rollbackPacketRoot 'undeclared.txt'
    [System.IO.File]::WriteAllText($undeclaredRollbackFile, 'not sealed by the manifest')
    try {
        $extraFileRollbackIdentityJson = @(
            & $identityScript -Path $rollbackPath -StackRoot $stackRoot -Json
        ) -join "`n"
        $extraFileRollbackIdentity = ConvertFrom-LhmReleaseJson -Json $extraFileRollbackIdentityJson
        Assert-LhmReleaseTest (
            $extraFileRollbackIdentity.role -ceq 'rollback-payload-invalid' -and
            $extraFileRollbackIdentity.authority -ceq 'none' -and
            $extraFileRollbackIdentity.rollbackEvidence.status -ceq 'invalid' -and
            $extraFileRollbackIdentity.rollbackEvidence.error -like '*undeclared*'
        ) 'rollback authority must reject packet files that are not sealed by the manifest'
    }
    finally {
        Remove-Item -LiteralPath $undeclaredRollbackFile -Force
    }

    $validRollbackRecord = $rollbackPacketManifest.Files[0]
    $rollbackPacketManifest.Files = @(
        $validRollbackRecord,
        [ordered]@{
            RelativePath = 'runtime\LibreHardwareMonitor.Windows.Forms.exe'
            Length = $validRollbackRecord.Length
            Sha256 = $validRollbackRecord.Sha256
        }
    )
    $rollbackPacketManifest.FileCount = 2
    [System.IO.File]::WriteAllText(
        $rollbackManifestPath,
        ($rollbackPacketManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    $duplicateRollbackIdentityJson = @(
        & $identityScript -Path $rollbackPath -StackRoot $stackRoot -Json
    ) -join "`n"
    $duplicateRollbackIdentity = ConvertFrom-LhmReleaseJson -Json $duplicateRollbackIdentityJson
    Assert-LhmReleaseTest (
        $duplicateRollbackIdentity.role -ceq 'rollback-payload-invalid' -and
        $duplicateRollbackIdentity.authority -ceq 'none' -and
        $duplicateRollbackIdentity.rollbackEvidence.status -ceq 'invalid' -and
        $duplicateRollbackIdentity.rollbackEvidence.error -like '*duplicate*'
    ) 'rollback authority must reject duplicate or case-colliding manifest records'
    $rollbackPacketManifest.Files = @($validRollbackRecord)
    $rollbackPacketManifest.FileCount = 1
    [System.IO.File]::WriteAllText(
        $rollbackManifestPath,
        ($rollbackPacketManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))

    $rollbackJunctionTarget = Join-Path $testRoot 'rollback-packet-junction-target'
    $rollbackJunction = Join-Path $rollbackPacketRoot 'linked-content'
    [System.IO.Directory]::CreateDirectory($rollbackJunctionTarget) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $rollbackJunctionTarget 'outside.txt'),
        'outside rollback packet')
    try {
        New-Item `
            -ItemType Junction `
            -Path $rollbackJunction `
            -Target $rollbackJunctionTarget | Out-Null
        $reparseRollbackIdentityJson = @(
            & $identityScript -Path $rollbackPath -StackRoot $stackRoot -Json
        ) -join "`n"
        $reparseRollbackIdentity = ConvertFrom-LhmReleaseJson -Json $reparseRollbackIdentityJson
        Assert-LhmReleaseTest (
            $reparseRollbackIdentity.role -ceq 'rollback-payload-invalid' -and
            $reparseRollbackIdentity.authority -ceq 'none' -and
            $reparseRollbackIdentity.rollbackEvidence.status -ceq 'invalid' -and
            $reparseRollbackIdentity.rollbackEvidence.error -like '*reparse*'
        ) 'rollback authority must reject descendant reparse points in the packet tree'
    }
    finally {
        if ([System.IO.Directory]::Exists($rollbackJunction)) {
            [System.IO.Directory]::Delete($rollbackJunction)
        }
    }

    $rollbackPacketManifest.Files[0].Sha256 = '00' * 32
    [System.IO.File]::WriteAllText(
        $rollbackManifestPath,
        ($rollbackPacketManifest | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    $invalidRollbackIdentityJson = @(
        & $identityScript -Path $rollbackPath -StackRoot $stackRoot -Json
    ) -join "`n"
    $invalidRollbackIdentity = ConvertFrom-LhmReleaseJson -Json $invalidRollbackIdentityJson
    Assert-LhmReleaseTest (
        $invalidRollbackIdentity.role -ceq 'rollback-payload-invalid' -and
        $invalidRollbackIdentity.authority -ceq 'none' -and
        $invalidRollbackIdentity.rollbackEvidence.status -ceq 'invalid' -and
        $invalidRollbackIdentity.rollbackEvidence.error -like '*SHA-256*'
    ) 'rollback location alone must not grant authority when the packet hash record is invalid'

    function Get-CimInstance {
        [CmdletBinding()]
        param(
            [string]$ClassName,
            [string[]]$Property
        )

        throw 'injected identity CIM failure'
    }
    try {
        $incompleteIdentityJson = @(
            & $identityScript -Path $developmentProbePath -Json
        ) -join "`n"
        $incompleteIdentity = ConvertFrom-LhmReleaseJson -Json $incompleteIdentityJson
        Assert-LhmReleaseTest (
            $incompleteIdentity.status -ceq 'INCOMPLETE' -and
            $incompleteIdentity.processQuery.status -ceq 'failed' -and
            $incompleteIdentity.processQuery.error -like '*injected identity CIM failure*' -and
            @($incompleteIdentity.runningProcessIds).Count -eq 0
        ) 'failed process observation must be explicit and must prevent an overall IDENTIFIED result'
    }
    finally {
        Remove-Item -LiteralPath Function:\Get-CimInstance -Force
    }

    $fixtureVerifier = Join-Path $orchestrationScripts 'Test-LhmReleaseCandidate.ps1'
    $invalidSmokeManifest = $fixtureManifestJson | ConvertFrom-Json
    $invalidSmokeManifest.packages[0].smoke.completed = $false
    try {
        [System.IO.File]::WriteAllText(
            $fixtureManifestPath,
            ($invalidSmokeManifest | ConvertTo-Json -Depth 16),
            [System.Text.UTF8Encoding]::new($false))
        Assert-LhmReleaseThrows {
            & $fixtureVerifier `
                -CandidatePath $fixtureCandidate `
                -ReleaseRoot $orchestrationReleaseRoot | Out-Null
        } 'validator must reject tampered version 2 package-smoke evidence' '*smoke evidence*'
    }
    finally {
        [System.IO.File]::WriteAllText(
            $fixtureManifestPath,
            $fixtureManifestJson,
            [System.Text.UTF8Encoding]::new($false))
    }
    Assert-LhmCandidateContentUnchanged `
        -CandidatePath $fixtureCandidate `
        -Expected $candidateIdentityBefore
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
        $env:FAKE_LHM_SMOKE_FAIL_FRAMEWORK = 'net472'
        Assert-LhmReleaseThrows {
            & $fixtureCreator `
                -ReleaseRoot $orchestrationReleaseRoot `
                -ReleaseId 'fixture-second-smoke-failure' `
                -SkipVerification `
                -DotnetPath $fakeDotnet | Out-Null
        } 'second framework extracted-entry-point smoke failure must abort the whole candidate' '*smoke*net472*exit code 23*'
    }
    finally {
        Remove-Item Env:\FAKE_LHM_SMOKE_FAIL_FRAMEWORK -ErrorAction SilentlyContinue
    }
    Assert-LhmReleaseTest (
        -not [System.IO.Directory]::Exists(
            (Join-Path $orchestrationReleaseRoot 'candidates\fixture-second-smoke-failure'))
    ) 'second framework smoke failure must publish no partial candidate'

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
            commands = @(Get-LhmReleaseVerificationCommands -ManifestVersion 1)
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

    # Candidate identity must detect content and filesystem-boundary changes.
    $candidateIdentity = Get-LhmCandidateContentIdentity -CandidatePath $candidate
    Assert-LhmCandidateContentUnchanged -CandidatePath $candidate -Expected $candidateIdentity
    Assert-LhmReleaseTest $true 'unchanged candidate content identity should verify'

    $candidateIdentityAlias = Join-Path $testRoot 'candidate-identity-alias'
    try {
        New-Item `
            -ItemType Junction `
            -Path $candidateIdentityAlias `
            -Target $candidate | Out-Null
        Assert-LhmReleaseThrows {
            Get-LhmCandidateContentIdentity -CandidatePath $candidateIdentityAlias | Out-Null
        } 'candidate content identity must reject a reparse-backed root' '*reparse*'
    }
    finally {
        if ([System.IO.Directory]::Exists($candidateIdentityAlias)) {
            [System.IO.Directory]::Delete($candidateIdentityAlias)
        }
    }

    $identityJunctionTarget = Join-Path $testRoot 'candidate-identity-junction-target'
    $identityJunction = Join-Path $packages 'candidate-identity-junction'
    [System.IO.Directory]::CreateDirectory($identityJunctionTarget) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $identityJunctionTarget 'outside.txt'),
        'must remain outside candidate identity')
    try {
        New-Item `
            -ItemType Junction `
            -Path $identityJunction `
            -Target $identityJunctionTarget | Out-Null
        Assert-LhmReleaseThrows {
            Assert-LhmCandidateContentUnchanged `
                -CandidatePath $candidate `
                -Expected $candidateIdentity
        } 'candidate content identity must reject descendant reparse points' '*reparse*'
        Assert-LhmReleaseTest (
            [System.IO.File]::Exists((Join-Path $identityJunctionTarget 'outside.txt'))
        ) 'rejected candidate identity must preserve the reparse target'
    }
    finally {
        if ([System.IO.Directory]::Exists($identityJunction)) {
            [System.IO.Directory]::Delete($identityJunction)
        }
    }

    $manifestPath = Join-Path $candidate 'release-manifest.json'
    $originalManifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
    $changedManifestBytes = [byte[]]$originalManifestBytes.Clone()
    $manifestFlipIndex = [int][Math]::Floor($changedManifestBytes.Length / 2)
    $changedManifestBytes[$manifestFlipIndex] = $changedManifestBytes[$manifestFlipIndex] -bxor 0x01
    [System.IO.File]::WriteAllBytes($manifestPath, $changedManifestBytes)
    Assert-LhmReleaseThrows {
        Assert-LhmCandidateContentUnchanged -CandidatePath $candidate -Expected $candidateIdentity
    } 'candidate content identity must detect an equal-length manifest byte change' '*content changed*'
    [System.IO.File]::WriteAllBytes($manifestPath, $originalManifestBytes)

    $identityArchivePath = [string]$manifest.packages[0].archive
    $identityArchive = Join-Path $candidate ($identityArchivePath.Replace('/', '\'))
    $originalIdentityArchiveBytes = [System.IO.File]::ReadAllBytes($identityArchive)
    $changedIdentityArchiveBytes = [byte[]]$originalIdentityArchiveBytes.Clone()
    $changedIdentityArchiveBytes[100] = $changedIdentityArchiveBytes[100] -bxor 0xFF
    [System.IO.File]::WriteAllBytes($identityArchive, $changedIdentityArchiveBytes)
    Assert-LhmReleaseThrows {
        Assert-LhmCandidateContentUnchanged -CandidatePath $candidate -Expected $candidateIdentity
    } 'candidate content identity must detect an equal-length archive byte change' '*content changed*'

    [System.IO.File]::WriteAllBytes(
        $identityArchive,
        $originalIdentityArchiveBytes[0..($originalIdentityArchiveBytes.Length - 2)])
    Assert-LhmReleaseThrows {
        Assert-LhmCandidateContentUnchanged -CandidatePath $candidate -Expected $candidateIdentity
    } 'candidate content identity must detect archive truncation' '*content changed*'
    [System.IO.File]::WriteAllBytes($identityArchive, $originalIdentityArchiveBytes)

    $identityExtraPackage = Join-Path $packages 'unexpected-identity-package.zip'
    [System.IO.File]::WriteAllBytes($identityExtraPackage, [byte[]](1, 2, 3, 4))
    Assert-LhmReleaseThrows {
        Assert-LhmCandidateContentUnchanged -CandidatePath $candidate -Expected $candidateIdentity
    } 'candidate content identity must detect an added package' '*content changed*'
    [System.IO.File]::Delete($identityExtraPackage)

    $secondIdentityArchivePath = [string]$manifest.packages[1].archive
    $secondIdentityArchive = Join-Path $candidate ($secondIdentityArchivePath.Replace('/', '\'))
    $secondIdentityArchiveBytes = [System.IO.File]::ReadAllBytes($secondIdentityArchive)
    [System.IO.File]::Delete($secondIdentityArchive)
    Assert-LhmReleaseThrows {
        Assert-LhmCandidateContentUnchanged -CandidatePath $candidate -Expected $candidateIdentity
    } 'candidate content identity must detect a removed package' '*content changed*'
    [System.IO.File]::WriteAllBytes($secondIdentityArchive, $secondIdentityArchiveBytes)

    Assert-LhmCandidateContentUnchanged -CandidatePath $candidate -Expected $candidateIdentity
    Assert-LhmReleaseTest $true 'restored candidate content identity should verify again'

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
    Assert-LhmReleaseTest ($globalJson.sdk.version -eq '10.0.400') 'release SDK should be pinned to the 10.0.400 feature band'
    Assert-LhmReleaseTest ($globalJson.sdk.rollForward -ceq 'latestPatch') 'release SDK selection must stay within the pinned feature band that candidate creation enforces'
    Assert-LhmReleaseTest (-not [bool]$globalJson.sdk.allowPrerelease) 'release SDK must reject prerelease selection'

    # Candidate creation must accept exactly what the dotnet host selects for
    # the pinned policy, so SDK patch updates do not strand release creation.
    Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.400' -RollForward 'latestPatch' -ResolvedVersion '10.0.400'
    Assert-LhmReleaseTest $true 'latestPatch should accept the pinned SDK'
    Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.400' -RollForward 'latestPatch' -ResolvedVersion '10.0.401'
    Assert-LhmReleaseTest $true 'latestPatch should accept a newer patch in the pinned feature band'
    Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.400' -RollForward '' -ResolvedVersion '10.0.401'
    Assert-LhmReleaseTest $true 'an omitted rollForward should use the dotnet latestPatch default'
    Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.302' -RollForward 'disable' -ResolvedVersion '10.0.302'
    Assert-LhmReleaseTest $true 'disable should accept the exact pinned SDK'
    Assert-LhmReleaseThrows {
        Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.400' -RollForward 'disable' -ResolvedVersion '10.0.401'
    } 'disable must require the exact pinned SDK' "*resolved '10.0.401'*"
    Assert-LhmReleaseThrows {
        Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.400' -RollForward 'latestPatch' -ResolvedVersion '10.0.501'
    } 'latestPatch must stay within the pinned feature band' "*resolved '10.0.501'*"
    Assert-LhmReleaseThrows {
        Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.400' -RollForward 'latestPatch' -ResolvedVersion '11.0.401'
    } 'latestPatch must stay within the pinned major and minor version' "*resolved '11.0.401'*"
    Assert-LhmReleaseThrows {
        Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.401' -RollForward 'latestPatch' -ResolvedVersion '10.0.400'
    } 'latestPatch must not accept an SDK older than the pin' "*resolved '10.0.400'*"
    Assert-LhmReleaseThrows {
        Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.400' -RollForward 'latestPatch' -ResolvedVersion '10.0.401-preview.1'
    } 'release candidates must reject prerelease SDKs' '*stable SDK*'
    Assert-LhmReleaseThrows {
        Assert-LhmReleaseSdkSelection -PinnedVersion '10.0.400' -RollForward 'latestFeature' -ResolvedVersion '10.0.400'
    } 'release candidates must reject roll-forward policies that leave the feature band' '*roll-forward*'

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
    Remove-Item Env:\FAKE_LHM_SMOKE_PAYLOAD_TEMPLATE_ROOT -ErrorAction SilentlyContinue
    if ([System.IO.Directory]::Exists($testRoot)) {
        $resolvedCleanup = Resolve-LhmReleaseFullPath -Path $testRoot
        if (-not $resolvedCleanup.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean release test path outside temp: $resolvedCleanup"
        }
        Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force
    }
}

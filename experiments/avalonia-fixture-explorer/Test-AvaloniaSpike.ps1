[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..'))
$spikeSolution = 'experiments\avalonia-fixture-explorer\LibreHardwareMonitor.Avalonia.Spike.slnx'
$spikeTestProject =
    'experiments\avalonia-fixture-explorer\LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj'
$shippingSolution = Join-Path $repositoryRoot 'LibreHardwareMonitor.sln'
$expectedGateCount = 10
$completedGateCount = 0

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) {
    throw "Avalonia spike gate is not running from a Git checkout: $repositoryRoot"
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    Write-Host "GATE: $Description"
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $FilePath @ArgumentList 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Host ([string]$line)
    }

    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }

    return @($output | ForEach-Object { [string]$_ })
}

function Get-RequiredMatchCount {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Output,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $matches = @(
        foreach ($line in $Output) {
            $match = [System.Text.RegularExpressions.Regex]::Match(
                $line,
                $Pattern,
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if ($match.Success) {
                $match
            }
        }
    )

    if ($matches.Count -eq 0) {
        throw "Unable to read $Description from command output."
    }

    return [int]$matches[$matches.Count - 1].Groups['count'].Value
}

Push-Location -LiteralPath $repositoryRoot
try {
    $globalJsonPath = Join-Path $repositoryRoot 'global.json'
    $globalJson = Get-Content -Raw -LiteralPath $globalJsonPath |
        ConvertFrom-Json
    $pinnedSdk = [string]$globalJson.sdk.version
    if ($pinnedSdk -cne '10.0.302') {
        throw "Expected global.json to pin SDK '10.0.302'; found '$pinnedSdk'."
    }

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop
    $sdkOutput = @(Invoke-CheckedCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @('--version') `
        -Description 'resolve pinned .NET SDK')
    if ($sdkOutput.Count -ne 1 -or
        $sdkOutput[0].Trim() -cne $pinnedSdk -or
        $sdkOutput[0].Contains('-')) {
        throw "Expected pinned stable SDK '$pinnedSdk'; resolved '$($sdkOutput -join ', ')'."
    }
    $completedGateCount++

    $nativeFailureProbePassed = $false
    try {
        Invoke-CheckedCommand `
            -FilePath $env:ComSpec `
            -ArgumentList @('/d', '/c', 'exit 23') `
            -Description 'prove native nonzero exit handling' |
            Out-Null
    }
    catch {
        if ($_.Exception.Message -notlike '*exit code 23*') {
            throw
        }

        $nativeFailureProbePassed = $true
    }
    if (-not $nativeFailureProbePassed) {
        throw 'Native nonzero exit probe did not fail the command wrapper.'
    }

    Invoke-CheckedCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @('restore', $spikeSolution) `
        -Description 'restore separate Avalonia spike solution' |
        Out-Null
    $completedGateCount++
    $spikeBuildOutput = @(Invoke-CheckedCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @(
            'build',
            $spikeSolution,
            '-c',
            'Release',
            '--no-restore') `
        -Description 'build separate Avalonia spike solution in Release')
    $completedGateCount++

    $spikeTestOutput = @(Invoke-CheckedCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @(
            'run',
            '--project',
            $spikeTestProject,
            '-c',
            'Release',
            '--no-build',
            '--',
            '-noColor') `
        -Description 'run isolated xUnit v3 Avalonia spike executable')
    $spikeTestCount = Get-RequiredMatchCount `
        -Output $spikeTestOutput `
        -Pattern 'Tests\s+Total:\s*(?<count>\d+)' `
        -Description 'isolated xUnit test total'
    $spikeTestErrorCount = Get-RequiredMatchCount `
        -Output $spikeTestOutput `
        -Pattern 'Errors:\s*(?<count>\d+)' `
        -Description 'isolated xUnit error total'
    $spikeTestFailedCount = Get-RequiredMatchCount `
        -Output $spikeTestOutput `
        -Pattern 'Failed:\s*(?<count>\d+)' `
        -Description 'isolated xUnit failed-test total'
    $spikeTestSkippedCount = Get-RequiredMatchCount `
        -Output $spikeTestOutput `
        -Pattern 'Skipped:\s*(?<count>\d+)' `
        -Description 'isolated xUnit skipped-test total'
    $spikeTestNotRunCount = Get-RequiredMatchCount `
        -Output $spikeTestOutput `
        -Pattern 'Not Run:\s*(?<count>\d+)' `
        -Description 'isolated xUnit not-run total'
    if ($spikeTestErrorCount -ne 0 -or
        $spikeTestFailedCount -ne 0 -or
        $spikeTestSkippedCount -ne 0 -or
        $spikeTestNotRunCount -ne 0) {
        throw (
            "Expected every isolated spike test to pass; errors=" +
            "$spikeTestErrorCount, failed=$spikeTestFailedCount, " +
            "skipped=$spikeTestSkippedCount, not-run=$spikeTestNotRunCount.")
    }
    $completedGateCount++

    $spikeRoots = @(
        (Join-Path $repositoryRoot 'experiments\avalonia-fixture-explorer\LibreHardwareMonitor.Avalonia.Spike'),
        (Join-Path $repositoryRoot 'experiments\avalonia-fixture-explorer\LibreHardwareMonitor.Avalonia.Spike.Core'),
        (Join-Path $repositoryRoot 'experiments\avalonia-fixture-explorer\LibreHardwareMonitor.Avalonia.Spike.Tests'))
    $spikeSourceExtensions = @(
        '.axaml',
        '.cs',
        '.csproj',
        '.json',
        '.slnx')
    $spikeSourceFiles = @(
        Get-ChildItem -LiteralPath $spikeRoots `
            -File `
            -Recurse |
            Where-Object {
                $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' -and
                $spikeSourceExtensions -contains $_.Extension.ToLowerInvariant()
            }
        Get-Item -LiteralPath (Join-Path $repositoryRoot $spikeSolution)
    )
    $spikeSourceFiles = @(
        $spikeSourceFiles |
            Sort-Object -Property FullName -Unique)
    if ($spikeSourceFiles.Count -eq 0) {
        throw 'The spike isolation scan resolved no source or project files.'
    }

    $spikeProjectFiles = @(
        Get-ChildItem -LiteralPath $spikeRoots -Filter '*.csproj' -File)
    $allowedSpikeProjects = @{}
    foreach ($project in $spikeProjectFiles) {
        $allowedSpikeProjects[$project.FullName.ToLowerInvariant()] = $true
    }
    $projectReferenceCount = 0
    foreach ($project in $spikeProjectFiles) {
        [xml]$projectXml = Get-Content -Raw -LiteralPath $project.FullName
        foreach ($projectReference in @(
            $projectXml.SelectNodes('//ProjectReference'))) {
            $include = [string]$projectReference.Include
            if ([string]::IsNullOrWhiteSpace($include)) {
                throw "ProjectReference without Include: $($project.FullName)"
            }

            $resolvedReference = [System.IO.Path]::GetFullPath(
                (Join-Path $project.DirectoryName $include))
            if (-not $allowedSpikeProjects.ContainsKey(
                $resolvedReference.ToLowerInvariant())) {
                throw (
                    "Spike project references a project outside the isolated " +
                    "spike set: $($project.Name) -> $include")
            }

            $projectReferenceCount++
        }
    }

    $manifestFiles = @(
        Get-ChildItem -LiteralPath $spikeRoots -Filter '*.manifest' -File -Recurse |
            Where-Object {
                $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]'
            })
    if ($manifestFiles.Count -gt 0) {
        throw "Spike manifest files are forbidden: $($manifestFiles.FullName -join '; ')"
    }

    $forbiddenRules = @(
        [pscustomobject]@{
            Name = 'WinForms project or namespace'
            Pattern = '(?:LibreHardwareMonitor\.Windows\.Forms|\bSystem\.Windows\.Forms\b|<UseWindowsForms>\s*true\s*</UseWindowsForms>|Microsoft\.WindowsDesktop\.App\.WindowsForms)'
        },
        [pscustomobject]@{
            Name = 'hardware library'
            Pattern = '\bLibreHardwareMonitorLib\b'
        },
        [pscustomobject]@{
            Name = 'hardware open'
            Pattern = '\bComputer\s*\.\s*Open\s*\('
        },
        [pscustomobject]@{
            Name = 'persistent settings'
            Pattern = '\bPersistentSettings\b'
        },
        [pscustomobject]@{
            Name = 'HTTP client'
            Pattern = '(?:\bSystem\.Net\.Http\b|\bHttpClient\b|\bHttpMessageInvoker\b|\bHttpRequestMessage\b|\bWebClient\b|\bWebRequest\b)'
        },
        [pscustomobject]@{
            Name = 'HTTP mutation method'
            Pattern = '(?:HttpMethod\s*\.\s*Post|PostAsync\s*\(|MapPost\s*\(|["'']POST["''])'
        },
        [pscustomobject]@{
            Name = 'sensor control or reset route'
            Pattern = '(?:/Sensor\?action=(?:Set|ResetMinMax)|/ResetAllMinMax|action=(?:Set|ResetMinMax))'
        },
        [pscustomobject]@{
            Name = 'administrator manifest'
            Pattern = '(?:requestedExecutionLevel|requireAdministrator|ApplicationManifest|app(?:\.net472)?\.manifest)'
        },
        [pscustomobject]@{
            Name = 'scheduled task integration'
            Pattern = '(?:Register-ScheduledTask|New-ScheduledTask|schtasks(?:\.exe)?|\bTaskScheduler\b)'
        },
        [pscustomobject]@{
            Name = 'SND-HOST live runtime root'
            Pattern = 'E:[\\/]+SQ_HQ[\\/]+Monitoring[\\/]+LibreHardwareMonitor(?:[\\/]|["''])'
        },
        [pscustomobject]@{
            Name = 'release store'
            Pattern = '(?:LibreHardwareMonitor-Releases|\bLHM_RELEASE_ROOT\b)'
        })

    $forbiddenMatches = New-Object System.Collections.Generic.List[string]
    foreach ($file in $spikeSourceFiles) {
        $text = Get-Content -Raw -LiteralPath $file.FullName
        foreach ($rule in $forbiddenRules) {
            if ([System.Text.RegularExpressions.Regex]::IsMatch(
                $text,
                $rule.Pattern,
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                $relativePath = $file.FullName.Substring(
                    $repositoryRoot.Length + 1)
                $forbiddenMatches.Add(
                    "$relativePath [$($rule.Name)]")
            }
        }
    }
    if ($forbiddenMatches.Count -gt 0) {
        throw "Forbidden spike references found: $($forbiddenMatches -join '; ')"
    }
    $completedGateCount++

    $shippingSolutionText =
        Get-Content -Raw -LiteralPath $shippingSolution
    if ($shippingSolutionText -match 'LibreHardwareMonitor\.Avalonia\.Spike') {
        throw 'LibreHardwareMonitor.sln must not list any Avalonia spike project.'
    }
    $shippingSolutionOutput = @(Invoke-CheckedCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @('sln', 'LibreHardwareMonitor.sln', 'list') `
        -Description 'list shipping solution projects independently')
    if (($shippingSolutionOutput -join "`n") -match
        'LibreHardwareMonitor\.Avalonia\.Spike') {
        throw 'Shipping solution command output lists an Avalonia spike project.'
    }
    $completedGateCount++

    $existingTestOutput = @(Invoke-CheckedCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @(
            'test',
            'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj',
            '-p:Platform=x64') `
        -Description 'run existing .NET regression tests')
    $existingTestCount = Get-RequiredMatchCount `
        -Output $existingTestOutput `
        -Pattern 'Total:\s*(?<count>\d+)' `
        -Description 'existing .NET test total'
    $existingSkippedCount = Get-RequiredMatchCount `
        -Output $existingTestOutput `
        -Pattern 'Skipped:\s*(?<count>\d+)' `
        -Description 'existing .NET skipped-test total'
    $completedGateCount++

    $net10BuildOutput = @(Invoke-CheckedCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @(
            'build',
            'LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj',
            '-c',
            'Release',
            '-f',
            'net10.0-windows',
            '-p:Platform=x64') `
        -Description 'build WinForms x64 Release net10.0-windows')
    $net10BuildWarningCount = Get-RequiredMatchCount `
        -Output $net10BuildOutput `
        -Pattern '^\s*(?<count>\d+)\s+Warning\(s\)\s*$' `
        -Description 'net10.0-windows build warning total'
    $net10BuildErrorCount = Get-RequiredMatchCount `
        -Output $net10BuildOutput `
        -Pattern '^\s*(?<count>\d+)\s+Error\(s\)\s*$' `
        -Description 'net10.0-windows build error total'
    if ($net10BuildErrorCount -ne 0) {
        throw "net10.0-windows build reported $net10BuildErrorCount errors."
    }
    $completedGateCount++
    $net472BuildOutput = @(Invoke-CheckedCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @(
            'build',
            'LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj',
            '-c',
            'Release',
            '-f',
            'net472',
            '-p:Platform=x64') `
        -Description 'build WinForms x64 Release net472')
    $net472BuildWarningCount = Get-RequiredMatchCount `
        -Output $net472BuildOutput `
        -Pattern '^\s*(?<count>\d+)\s+Warning\(s\)\s*$' `
        -Description 'net472 build warning total'
    $net472BuildErrorCount = Get-RequiredMatchCount `
        -Output $net472BuildOutput `
        -Pattern '^\s*(?<count>\d+)\s+Error\(s\)\s*$' `
        -Description 'net472 build error total'
    if ($net472BuildErrorCount -ne 0) {
        throw "net472 build reported $net472BuildErrorCount errors."
    }
    $completedGateCount++

    $windowsPowerShell = Join-Path (
        [System.Environment]::GetFolderPath(
            [System.Environment+SpecialFolder]::System)) `
        'WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) {
        throw "Windows PowerShell is unavailable: $windowsPowerShell"
    }
    $releaseTestOutput = @(Invoke-CheckedCommand `
        -FilePath $windowsPowerShell `
        -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            'ops\release\Test-LhmReleaseSystem.ps1') `
        -Description 'run release-system behavior fixture')
    $releaseAssertionCount = Get-RequiredMatchCount `
        -Output $releaseTestOutput `
        -Pattern '\((?<count>\d+)\s+assertions\)' `
        -Description 'release-system assertion total'
    $completedGateCount++

    if ($completedGateCount -ne $expectedGateCount) {
        throw (
            "Expected $expectedGateCount completed gates; " +
            "recorded $completedGateCount.")
    }

    $spikeProjectCount = @(
        $spikeProjectFiles).Count
    $spikeBuildWarningCount = 0
    foreach ($line in $spikeBuildOutput) {
        $warningMatch = [System.Text.RegularExpressions.Regex]::Match(
            $line,
            '^\s*(?<count>\d+)\s+Warning\(s\)\s*$',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($warningMatch.Success) {
            $spikeBuildWarningCount =
                [int]$warningMatch.Groups['count'].Value
        }
    }

    Write-Output 'PASS: Avalonia fixture spike source and regression gate'
    Write-Output "  Fail-fast gates: $completedGateCount/$expectedGateCount"
    Write-Output '  Native nonzero exit probe: 1/1 rejected'
    Write-Output "  SDK: 1/1 pinned stable version ($pinnedSdk)"
    Write-Output "  Spike project inventory: $spikeProjectCount projects"
    Write-Output (
        "  Spike restore/build: 1/1 solution restored; 1/1 solution built " +
        "($spikeBuildWarningCount warnings, 0 errors)")
    Write-Output "  Spike tests: $spikeTestCount/$spikeTestCount passed"
    Write-Output (
        "  Isolation scan: $($spikeSourceFiles.Count) files x " +
        "$($forbiddenRules.Count) rules; 0 forbidden matches; " +
        "$projectReferenceCount isolated project references; 0 manifests")
    Write-Output '  Shipping solution isolation: 1/1 command; 0 spike projects'
    Write-Output (
        "  Existing .NET tests: $existingTestCount total; " +
        "$existingSkippedCount skipped; 0 failed")
    Write-Output (
        "  WinForms x64 Release builds: 2/2 target frameworks; " +
        "net10.0-windows $net10BuildWarningCount warnings/" +
        "$net10BuildErrorCount errors; net472 " +
        "$net472BuildWarningCount warnings/$net472BuildErrorCount errors")
    Write-Output (
        "  Release-system fixture: $releaseAssertionCount/" +
        "$releaseAssertionCount assertions passed")
}
finally {
    Pop-Location
}

[CmdletBinding()]
param()

# Permanent verification-suite boundary gate (Plan-006).
# The deterministic CI membership is defined by LibreHardwareMonitor.Tests.slnf:
# exactly the Library, Application, and Contracts suites. The Attended suite is
# a real project in the shipping solution but is outside deterministic CI *by
# construction* — this gate fails if the slnf or any configured build-gate
# command ever comes to reference it, and if the golden master loses the
# CallerFilePath adjacency DataJsonGoldenTests depends on.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$failures = New-Object System.Collections.Generic.List[string]

function Assert-Ok {
    param([string]$Label, [scriptblock]$Predicate)
    if (& $Predicate) {
        Write-Output "PASS: $Label"
    } else {
        Write-Output "FAIL: $Label"
        $failures.Add($Label)
    }
}

$testsRoot = Join-Path $repositoryRoot 'LibreHardwareMonitor.Tests'
$slnfPath = Join-Path $testsRoot 'LibreHardwareMonitor.Tests.slnf'
$deterministicSuites = @(
    'LibreHardwareMonitor.Tests.Library'
    'LibreHardwareMonitor.Tests.Application'
    'LibreHardwareMonitor.Tests.Contracts'
)
$attendedSuite = 'LibreHardwareMonitor.Tests.Attended'

# 1. The slnf parses as JSON and lists exactly the three deterministic suites.
$slnf = $null
Assert-Ok "solution filter parses as JSON" {
    try {
        $script:slnf = Get-Content -Raw -LiteralPath $slnfPath | ConvertFrom-Json
        $null -ne $script:slnf.solution
    } catch { $false }
}
if ($null -ne $slnf) {
    $slnfProjects = @($slnf.solution.projects | ForEach-Object { [string]$_ })
    Assert-Ok "solution filter lists exactly the three deterministic suites" {
        if ($slnfProjects.Count -ne 3) { return $false }
        foreach ($suite in $deterministicSuites) {
            $matched = @($slnfProjects | Where-Object {
                ($_ -replace '/', '\') -eq "LibreHardwareMonitor.Tests\$suite\$suite.csproj"
            })
            if ($matched.Count -ne 1) { return $false }
        }
        return $true
    }
    Assert-Ok "solution filter does not reference the Attended suite" {
        @($slnfProjects | Where-Object { $_ -match [regex]::Escape($attendedSuite) }).Count -eq 0
    }
    Assert-Ok "solution filter points at the shipping solution" {
        (([string]$slnf.solution.path) -replace '/', '\') -match '(^|\\)LibreHardwareMonitor\.sln$'
    }
}

# 2. The Attended suite exists on disk and is a project in the shipping solution.
$attendedCsproj = Join-Path $testsRoot "$attendedSuite\$attendedSuite.csproj"
Assert-Ok "Attended suite project exists" { Test-Path -LiteralPath $attendedCsproj }
$slnText = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'LibreHardwareMonitor.sln')
Assert-Ok "Attended suite is present in the shipping solution" {
    $slnText -match [regex]::Escape("$attendedSuite.csproj")
}
foreach ($suite in $deterministicSuites) {
    Assert-Ok "deterministic suite is present in the shipping solution: $suite" {
        $slnText -match [regex]::Escape("$suite.csproj")
    }
}

# 3. No configured build-gate command references the Attended suite, and the
#    winforms-net10 test command runs the solution filter. Parse the TOML with
#    the same tomllib call the gate runner uses; single-quote every Python
#    string literal because native argument passing strips double quotes.
$configPath = Join-Path $repositoryRoot '.codex\skills\project.toml'
$pyScript = 'import json,sys,tomllib;sys.stdout.write(json.dumps(tomllib.load(open(sys.argv[1],''rb''))[''build-gate'']))'
$gatesJson = & python -c $pyScript $configPath
Assert-Ok "build-gate tables parse from project.toml" { $LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gatesJson) }
$gates = $gatesJson | ConvertFrom-Json
$gateCommands = @()
foreach ($gateProperty in $gates.PSObject.Properties) {
    foreach ($keyProperty in $gateProperty.Value.PSObject.Properties) {
        $gateCommands += [pscustomobject]@{
            Gate = $gateProperty.Name
            Key = $keyProperty.Name
            Command = [string]$keyProperty.Value
        }
    }
}
Assert-Ok "no configured gate command references the Attended suite" {
    @($gateCommands | Where-Object { $_.Command -match [regex]::Escape($attendedSuite) }).Count -eq 0
}
Assert-Ok "winforms-net10 test command runs the deterministic solution filter" {
    $winformsTest = @($gateCommands | Where-Object { $_.Gate -eq 'winforms-net10' -and $_.Key -eq 'test' })
    $winformsTest.Count -eq 1 -and $winformsTest[0].Command -match 'LibreHardwareMonitor\.Tests\.slnf'
}

# 4. The golden master sits beside DataJsonGoldenTests.cs — the test resolves
#    it via [CallerFilePath] adjacency, so separating them breaks the contract.
$contractsDir = Join-Path $testsRoot 'LibreHardwareMonitor.Tests.Contracts'
Assert-Ok "data.golden.json is adjacent to DataJsonGoldenTests.cs" {
    (Test-Path -LiteralPath (Join-Path $contractsDir 'DataJsonGoldenTests.cs')) -and
    (Test-Path -LiteralPath (Join-Path $contractsDir 'data.golden.json'))
}

if ($failures.Count -eq 0) {
    Write-Output '-> ok'
    exit 0
}
Write-Output "-> FAILED ($($failures.Count) failures)"
exit 1

<#
.SYNOPSIS
    Runs every eng/ci test script and reports an aggregate result.

.DESCRIPTION
    A dispatcher, not a test. It discovers eng/ci/tests/Test-*.ps1, runs each in
    a child process of the same PowerShell engine so the suite behaves
    identically under Windows PowerShell 5.1 and PowerShell 7, and aggregates
    the PASS / FAIL / SKIP lines each script prints.

    Each child's exit code is authoritative for pass or fail. The parsed counts
    are for reporting only.

    An empty or missing test directory is not a failure. This script is
    registered as the ci-gates build gate and must succeed with whatever tests
    exist at the time it runs.

.EXAMPLE
    .\Test-LhmCiGates.ps1

.EXAMPLE
    .\Test-LhmCiGates.ps1 -Filter Test-GateRunner
#>
[CmdletBinding()]
param(
    [string] $Filter = '*',
    [string] $TestRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:PYTHONDONTWRITEBYTECODE = '1'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($TestRoot)) {
    $TestRoot = Join-Path $PSScriptRoot 'tests'
}
elseif (-not [System.IO.Path]::IsPathRooted($TestRoot)) {
    $TestRoot = Join-Path $repositoryRoot $TestRoot
}

Write-Host "eng/ci test suite"
Write-Host "  test root: $TestRoot"
Write-Host "  filter:    $Filter"
Write-Host ''

if (-not (Test-Path -LiteralPath $TestRoot)) {
    Write-Host "No test directory at $TestRoot. Nothing to run."
    exit 0
}

$pattern = $Filter
if ($pattern -notlike '*Test-*') { $pattern = "Test-$Filter" }
if ($Filter -eq '*') { $pattern = 'Test-*' }

$scripts = @(Get-ChildItem -LiteralPath $TestRoot -Filter 'Test-*.ps1' -File |
    Where-Object { $_.BaseName -like $pattern } |
    Sort-Object -Property Name)

if ($scripts.Count -eq 0) {
    Write-Host "No test scripts matched '$pattern'. Nothing to run."
    exit 0
}

$engine = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName

$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0
$failedScripts = @()

foreach ($script in $scripts) {
    Write-Host "-- $($script.Name)"

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $script.FullName 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previous

    $lines = @($output | ForEach-Object { [string]$_ })
    $passed = @($lines | Where-Object { $_ -match '^\s*PASS:' }).Count
    $failed = @($lines | Where-Object { $_ -match '^\s*FAIL:' }).Count
    $skipped = @($lines | Where-Object { $_ -match '^\s*SKIP:' }).Count

    foreach ($line in $lines) {
        if ($line -match '^\s*(PASS|FAIL|SKIP):') { Write-Host "   $line" }
    }

    $totalPassed += $passed
    $totalFailed += $failed
    $totalSkipped += $skipped

    if ($exitCode -ne 0) {
        $failedScripts += $script.Name
        Write-Host "   -> FAILED (exit $exitCode; passed $passed, failed $failed, skipped $skipped)"
        # Surface the whole transcript so a child failure is diagnosable from
        # the gate output alone.
        foreach ($line in $lines) {
            if ($line -notmatch '^\s*(PASS|FAIL|SKIP):') { Write-Host "      $line" }
        }
    }
    else {
        Write-Host "   -> ok (passed $passed, failed $failed, skipped $skipped)"
    }
    Write-Host ''
}

Write-Host ('Scripts {0}, assertions passed {1}, failed {2}, skipped {3}' -f $scripts.Count, $totalPassed, $totalFailed, $totalSkipped)

if ($failedScripts.Count -gt 0) {
    Write-Host "Failed scripts: $($failedScripts -join ', ')"
    exit 1
}

exit 0

<#
.SYNOPSIS
    Regression suite for eng/ci/Invoke-LhmGates.ps1.

.DESCRIPTION
    Asserts observable runner behavior only: exit codes, JSON summary contents,
    and whether a command actually executed. Nothing here reaches into the
    runner's internals.

    Command-level assertions read the JSON summary rather than stdout. A child
    PowerShell process wraps console output mid-command, so a substring check
    against stdout is unreliable for anything longer than a short token.

    Follows the eng/ci test-script convention: no required parameters, one
    PASS/FAIL/SKIP line per assertion, temp-only writes, exit 0 only when
    nothing failed.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:PYTHONDONTWRITEBYTECODE = '1'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runner = Join-Path $repositoryRoot 'eng\ci\Invoke-LhmGates.ps1'
$realConfig = Join-Path $repositoryRoot '.codex\skills\project.toml'
$engine = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName

$script:Failures = 0

function Assert-Case {
    param([string] $Name, [bool] $Condition, [string] $Detail = '')

    if ($Condition) {
        Write-Host "PASS: $Name"
    }
    else {
        Write-Host "FAIL: $Name$(if ($Detail) { " - $Detail" })"
        $script:Failures++
    }
}

function Invoke-Runner {
    param([string[]] $Arguments)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $runner @Arguments 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous

    return @{
        ExitCode = $code
        Lines    = @($output | ForEach-Object { [string]$_ })
    }
}

function Get-Flat {
    # Child-process output wraps at the console width, and wrapping only ever
    # inserts whitespace. Stripping all whitespace makes a token match immune to
    # where the wrap landed.
    param([object] $Result)
    return (($Result.Lines -join "`n") -replace '\s', '')
}

function Get-Summary {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Write-FixtureConfig {
    # Windows PowerShell's Set-Content -Encoding UTF8 emits a BOM, and tomllib
    # rejects a BOM, so every fixture would fail to parse and the runner would
    # exit 2 instead of exercising the behavior under test. Write BOM-less UTF-8.
    param([string] $Path, [string] $Content)
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-ConfiguredGateNames {
    # Parse the configuration the same way the runner does, so the expected set
    # cannot go stale when a gate is added. Single-quote every Python string
    # literal: native argument passing strips embedded double quotes.
    $script = 'import json,sys,tomllib;sys.stdout.write(json.dumps(sorted(tomllib.load(open(sys.argv[1],''rb''))[''build-gate''].keys())))'
    $raw = & python -c $script $realConfig
    if ($LASTEXITCODE -ne 0) { return @() }
    return @($raw | ConvertFrom-Json)
}

function Get-AllStepCommands {
    param([object] $Summary)
    $commands = @()
    foreach ($gate in $Summary.gates) {
        foreach ($step in $gate.steps) { $commands += $step.command }
    }
    return $commands
}

$temp = Join-Path $env:TEMP ("lhm-ci-gaterunner-" + [guid]::NewGuid().ToString('N').Substring(0, 12))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    $sentinel = Join-Path $temp 'sentinel.txt'
    $listJson = Join-Path $temp 'list.json'
    $dryJson = Join-Path $temp 'dry.json'

    # --- Case 1: -List names every configured gate --------------------------
    $list = Invoke-Runner @('-List', '-JsonSummary', $listJson)
    $listSummary = Get-Summary -Path $listJson
    $configured = Get-ConfiguredGateNames
    $listed = @($listSummary.gates | ForEach-Object { $_.name })
    $missing = @($configured | Where-Object { $listed -notcontains $_ })
    # Guard against an empty expected set: a failed configuration parse would
    # otherwise make the missing-set check vacuously true.
    Assert-Case '-List exits 0 and names every configured build gate' `
        (($list.ExitCode -eq 0) -and ($configured.Count -gt 0) -and ($missing.Count -eq 0)) `
        "exit $($list.ExitCode); configured: $($configured.Count); missing: $($missing -join ', ')"

    # --- Case 2: every gate carries a classification and a reason ------------
    $blank = @($listSummary.gates | Where-Object {
            [string]::IsNullOrWhiteSpace($_.classification) -or [string]::IsNullOrWhiteSpace($_.reason)
        })
    Assert-Case 'every listed gate carries a non-empty classification and reason' `
        ($blank.Count -eq 0) `
        "blank: $(@($blank | ForEach-Object { $_.name }) -join ', ')"

    # --- Case 3: an unclassified gate is a hard error ------------------------
    $rogueConfig = Join-Path $temp 'rogue.toml'
    Write-FixtureConfig -Path $rogueConfig -Content @'
[build-gate.rogue]
test = 'powershell.exe -NoProfile -Command exit 0'
'@
    $rogue = Invoke-Runner @('-ConfigPath', $rogueConfig, '-All')
    $flatRogue = Get-Flat $rogue
    # Distinguish the unclassified-gate error from a parse failure, which also
    # exits 2. Only the former names the gate.
    Assert-Case 'an unclassified gate exits 2 and names the gate' `
        (($rogue.ExitCode -eq 2) -and ($flatRogue.Contains('unclassifiedgate:rogue'))) `
        "exit $($rogue.ExitCode)"

    # --- Case 4: the deny-list refuses without executing ---------------------
    $denyConfig = Join-Path $temp 'deny.toml'
    Write-FixtureConfig -Path $denyConfig -Content @"
[build-gate.log-management]
test = 'powershell.exe -NoProfile -Command Set-Content -Path $sentinel -Value ran; ops/candidate/New-LhmRelease.ps1'
"@
    $deny = Invoke-Runner @('-ConfigPath', $denyConfig, '-All')
    Assert-Case 'a denied command exits 3 and never runs' `
        (($deny.ExitCode -eq 3) -and (-not (Test-Path -LiteralPath $sentinel))) `
        "exit $($deny.ExitCode); sentinel present: $(Test-Path -LiteralPath $sentinel)"

    # --- Case 5: release-candidate runs its fixture only ---------------------
    $rcJson = Join-Path $temp 'rc.json'
    $rc = Invoke-Runner @('-Gate', 'release-candidate', '-DryRun', '-JsonSummary', $rcJson)
    $rcSummary = Get-Summary -Path $rcJson
    $rcGate = $rcSummary.gates | Where-Object { $_.name -eq 'release-candidate' }
    $rcCommands = @($rcGate.steps | ForEach-Object { $_.command })
    $runsFixture = @($rcCommands | Where-Object { $_ -match 'Test-LhmReleaseSystem\.ps1' }).Count -eq 1
    $runsDeploying = @($rcCommands | Where-Object { $_ -match 'New-LhmRelease|-RequireCurrentSource' }).Count -gt 0
    Assert-Case 'release-candidate runs only its release-system fixture' `
        (($rc.ExitCode -eq 0) -and $runsFixture -and (-not $runsDeploying)) `
        "commands: $($rcCommands -join ' | ')"

    # --- Case 6: ci-gates is excluded as self-referential --------------------
    $ciGate = $listSummary.gates | Where-Object { $_.name -eq 'ci-gates' }
    $classifiedExcluded = ($null -ne $ciGate) -and ($ciGate.classification -eq 'excluded') -and ($ciGate.reason -match 'recurse')
    $dry = Invoke-Runner @('-All', '-DryRun', '-JsonSummary', $dryJson)
    $drySummary = Get-Summary -Path $dryJson
    $ciDry = $drySummary.gates | Where-Object { $_.name -eq 'ci-gates' }
    $neverRun = ($null -eq $ciDry) -or (@($ciDry.steps).Count -eq 0)
    Assert-Case 'ci-gates is classified excluded for self-reference and never runs' `
        ($classifiedExcluded -and $neverRun) `
        "classified: $classifiedExcluded; steps: $(@($ciDry.steps).Count)"

    # --- Case 7: no included command matches the runner's own deny-list ------
    $patterns = @($listSummary.denyList)
    $offenders = @()
    foreach ($command in (Get-AllStepCommands -Summary $drySummary)) {
        foreach ($pattern in $patterns) {
            if ($command -match $pattern) { $offenders += "$command  ~  $pattern" }
        }
    }
    Assert-Case 'no command in the included set matches the deny-list' `
        (($patterns.Count -gt 0) -and ($offenders.Count -eq 0)) `
        "patterns: $($patterns.Count); offenders: $($offenders -join '; ')"

    # --- Case 8: an unknown gate name is rejected ----------------------------
    $unknown = Invoke-Runner @('-Gate', 'does-not-exist')
    Assert-Case 'an unknown -Gate name exits 2 and names the gate' `
        (($unknown.ExitCode -eq 2) -and ((Get-Flat $unknown).Contains('does-not-exist'))) `
        "exit $($unknown.ExitCode)"

    # --- Case 9: the JSON summary is well formed -----------------------------
    $singleJson = Join-Path $temp 'single.json'
    $single = Invoke-Runner @('-Gate', 'campaign-control', '-DryRun', '-JsonSummary', $singleJson)
    $singleSummary = Get-Summary -Path $singleJson
    $requested = @($singleSummary.gates | Where-Object { @($_.steps).Count -gt 0 } | ForEach-Object { $_.name })
    Assert-Case 'the JSON summary is schema-versioned, carries the deny-list, and matches the request' `
        (($single.ExitCode -eq 0) -and ($singleSummary.schemaVersion -eq 1) -and (@($singleSummary.denyList).Count -gt 0) -and ($requested.Count -eq 1) -and ($requested[0] -eq 'campaign-control')) `
        "schema $($singleSummary.schemaVersion); denyList $(@($singleSummary.denyList).Count); requested $($requested -join ', ')"

    # --- Case 10: a failing gate is reported and exits 1 ---------------------
    $failConfig = Join-Path $temp 'fail.toml'
    $failJson = Join-Path $temp 'fail.json'
    Write-FixtureConfig -Path $failConfig -Content @'
[build-gate.log-management]
test = 'powershell.exe -NoProfile -Command exit 1'
'@
    $fail = Invoke-Runner @('-ConfigPath', $failConfig, '-All', '-JsonSummary', $failJson)
    $failSummary = Get-Summary -Path $failJson
    $failGate = $null
    if ($null -ne $failSummary) { $failGate = $failSummary.gates | Where-Object { $_.name -eq 'log-management' } }
    Assert-Case 'a failing gate exits 1 and is recorded as Failed' `
        (($fail.ExitCode -eq 1) -and ($null -ne $failGate) -and ($failGate.status -eq 'Failed')) `
        "exit $($fail.ExitCode); summary written: $($null -ne $failSummary)"

    # --- Case 11: -FailFast stops after the first failure --------------------
    # winforms-net10 precedes log-management in the classification order, so the
    # second gate must never start.
    $secondSentinel = Join-Path $temp 'second.txt'
    $ffConfig = Join-Path $temp 'failfast.toml'
    $ffJson = Join-Path $temp 'failfast.json'
    Write-FixtureConfig -Path $ffConfig -Content @"
[build-gate.winforms-net10]
test = 'powershell.exe -NoProfile -Command exit 1'

[build-gate.log-management]
test = 'powershell.exe -NoProfile -Command Set-Content -Path $secondSentinel -Value ran; exit 1'
"@
    $ff = Invoke-Runner @('-ConfigPath', $ffConfig, '-All', '-FailFast', '-JsonSummary', $ffJson)
    $ffSummary = Get-Summary -Path $ffJson
    $secondGate = $null
    if ($null -ne $ffSummary) { $secondGate = $ffSummary.gates | Where-Object { $_.name -eq 'log-management' } }
    Assert-Case '-FailFast stops before the second failing gate runs' `
        (($ff.ExitCode -eq 1) -and (-not (Test-Path -LiteralPath $secondSentinel)) -and ($null -ne $secondGate) -and ($secondGate.status -ne 'Passed') -and (@($secondGate.steps).Count -eq 0)) `
        "exit $($ff.ExitCode); sentinel: $(Test-Path -LiteralPath $secondSentinel); summary written: $($null -ne $ffSummary)"
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:Failures -gt 0) {
    Write-Host "$($script:Failures) assertion(s) failed."
    exit 1
}

exit 0

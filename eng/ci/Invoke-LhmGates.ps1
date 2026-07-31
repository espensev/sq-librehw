<#
.SYNOPSIS
    Runs the repository's non-deploying verification gates.

.DESCRIPTION
    Gate commands are defined once, in the [build-gate.*] tables of
    .codex/skills/project.toml. This runner reads them at run time and hard-codes
    none of them, so the configuration stays the single source of truth.

    Every gate present in the configuration must be explicitly classified below.
    An unclassified gate is a hard error rather than a silent skip, because a
    silently skipped gate is indistinguishable from a passing one.

    Every command is checked against the deny-list immediately before execution,
    not only at classification time, so a later configuration edit that puts a
    deploying command inside an already-included gate is still refused.

    The runner never elevates, never starts or stops a process, service, or
    scheduled task, and writes no file other than -JsonSummary.

.EXAMPLE
    .\Invoke-LhmGates.ps1 -List

.EXAMPLE
    .\Invoke-LhmGates.ps1 -All -JsonSummary ci-gates-summary.json

.NOTES
    Exit codes:
      0  every attempted gate passed, or -List / -DryRun completed
      1  at least one gate failed
      2  configuration error
      3  a command was refused by the deny-list
#>
[CmdletBinding()]
param(
    [string[]] $Gate,
    [switch] $All,
    [switch] $List,
    [switch] $DryRun,
    [switch] $FailFast,
    [string] $JsonSummary,
    [string] $ConfigPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Set in this process so gate children inherit it. Without it the
# campaign-control gate's unittest discovery leaves __pycache__ directories in
# the repository, which the structural baseline records as zero. This is the
# only environment variable the runner sets.
$env:PYTHONDONTWRITEBYTECODE = '1'

$ExitOk = 0
$ExitFailed = 1
$ExitConfig = 2
$ExitRefused = 3

# ---------------------------------------------------------------------------
# Gate classification
#
# AllowedKeys is the set of command keys this runner may execute for the gate,
# in the fixed order build, verify, test. ExcludedKeys records the keys that
# exist in the configuration but are deliberately not run, each with a reason.
# ---------------------------------------------------------------------------
$GateClassification = [ordered]@{
    'winforms-net10'                = @{
        Classification = 'included'
        AllowedKeys    = @('build', 'test')
        ExcludedKeys   = @()
        Reason         = 'Non-deploying source build and the .NET test suite.'
    }
    'winforms-net472'               = @{
        Classification = 'included'
        AllowedKeys    = @('build')
        ExcludedKeys   = @()
        Reason         = 'Non-deploying second-framework source build.'
    }
    'avalonia-spike'                = @{
        Classification = 'included'
        AllowedKeys    = @('build', 'test')
        ExcludedKeys   = @()
        Reason         = 'Fixture-only, non-shipping spike solution and its runner.'
    }
    'web-dashboard'                 = @{
        Classification = 'included'
        AllowedKeys    = @('verify', 'test')
        ExcludedKeys   = @()
        Reason         = 'Static dashboard self-test and Node contract tests.'
    }
    'log-management'                = @{
        Classification = 'included'
        AllowedKeys    = @('test')
        ExcludedKeys   = @()
        Reason         = 'Host-neutral fixture test; installs nothing.'
    }
    'campaign-control'              = @{
        Classification = 'included'
        AllowedKeys    = @('verify', 'test')
        ExcludedKeys   = @()
        Reason         = 'Planning preflight and campaign-runtime regression tests.'
    }
    'release-candidate'             = @{
        Classification = 'partial'
        AllowedKeys    = @('test')
        ExcludedKeys   = @(
            @{
                Key    = 'build'
                Reason = 'Creates an external release candidate. Candidate creation belongs only to its named release gate, never to CI.'
            },
            @{
                Key    = 'verify'
                Reason = 'Test-LhmReleaseCandidate.ps1 -RequirePromotable -RequireCurrentSource is a promotion check, not a verification check. It is expected to fail whenever HEAD moves ahead of the last candidate, which is the normal state after a documentation or tooling commit.'
            }
        )
        Reason         = 'Only the release-system fixture runs.'
    }
    'snd-desk-local-release-fixture' = @{
        Classification = 'included'
        AllowedKeys    = @('test')
        ExcludedKeys   = @()
        Reason         = 'Peer-safe fixture using isolated temporary roots; proves the SND-DESK path fails closed on SND-HOST.'
    }
    'ci-gates'                      = @{
        Classification = 'excluded'
        AllowedKeys    = @()
        ExcludedKeys   = @()
        Reason         = 'Self-referential: this gate''s test suite invokes this runner, so running it here would recurse. Run eng/ci/Test-LhmCiGates.ps1 directly.'
    }
}

# ---------------------------------------------------------------------------
# Deny-list
#
# Applied to the resolved command string of every key immediately before it
# would run, including under -DryRun. A match refuses the whole gate.
# ---------------------------------------------------------------------------
$DenyList = @(
    'New-LhmRelease'
    '-RequireCurrentSource'
    'Publish-LibreHardwareMonitor'
    'Install-'
    'Finalize-'
    'Restore-Legacy'
    'Restore-PreStable'
    'Restore-LibreHardwareMonitorRelease'
    'Start-LibreHardwareMonitor'
    'Clear-LhmRepositoryBuildOutputs'
    'schtasks'
    'Register-ScheduledTask'
    'Set-ScheduledTask'
    'Start-ScheduledTask'
    'Unregister-ScheduledTask'
    'Start-Process'
    'Stop-Process'
    'New-Service'
    'Set-Service'
    'Stop-Service'
    'Start-Service'
    'git\s+push'
    'git\s+clean'
    'git\s+reset\s+--hard'
    'reg\s+add'
    'HKLM:'
    'netsh'
)

$KeyOrder = @('build', 'verify', 'test')

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Get-Prop {
    # StrictMode makes missing-property access throw on PSCustomObject, which is
    # what ConvertFrom-Json produces. Probe explicitly instead.
    param([object] $Object, [string] $Name)

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-DenyMatch {
    param([string] $Command)

    foreach ($pattern in $DenyList) {
        if ($Command -match $pattern) { return $pattern }
    }
    return $null
}

function Resolve-Configuration {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Configuration not found: $Path"
    }

    # Single-quote the Python string literal. PowerShell's native argument
    # passing strips embedded double quotes, so "rb" would reach Python as a
    # bare name.
    $script = 'import json,sys,tomllib;sys.stdout.write(json.dumps(tomllib.load(open(sys.argv[1],''rb''))))'
    $raw = $null
    try {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $raw = & python -c $script $Path 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = $previous
    }
    catch {
        throw "Python is required to read $Path but could not be started: $($_.Exception.Message)"
    }

    if ($code -ne 0) {
        throw "Failed to parse $Path (python exit $code): $raw"
    }

    try {
        return ($raw | Out-String).Trim() | ConvertFrom-Json
    }
    catch {
        throw "Configuration parsed but produced no usable JSON: $($_.Exception.Message)"
    }
}

function Get-GateInventory {
    param([object] $Configuration)

    $table = Get-Prop -Object $Configuration -Name 'build-gate'
    if ($null -eq $table) {
        throw 'Configuration declares no [build-gate.*] tables.'
    }

    $inventory = [ordered]@{}
    foreach ($property in $table.PSObject.Properties) {
        $steps = @()
        foreach ($key in $KeyOrder) {
            $command = Get-Prop -Object $property.Value -Name $key
            if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace([string]$command)) {
                $steps += , @{ Key = $key; Command = ([string]$command).Trim() }
            }
        }
        $inventory[$property.Name] = $steps
    }
    return $inventory
}

function Invoke-GateCommand {
    param([string] $Command, [string] $WorkingDirectory)

    $previousLocation = Get-Location
    $previousPreference = $ErrorActionPreference
    # Native tools legitimately write to stderr. Do not let that become a
    # terminating error; the exit code is the only authority.
    $ErrorActionPreference = 'Continue'
    if (Test-Path -LiteralPath 'Variable:PSNativeCommandUseErrorActionPreference') {
        $previousNative = $PSNativeCommandUseErrorActionPreference
        $PSNativeCommandUseErrorActionPreference = $false
    }
    try {
        Set-Location -LiteralPath $WorkingDirectory
        $global:LASTEXITCODE = 0
        Invoke-Expression -Command $Command | Out-Host
        return $global:LASTEXITCODE
    }
    catch {
        Write-Host "      error: $($_.Exception.Message)"
        return 1
    }
    finally {
        if (Test-Path -LiteralPath 'Variable:previousNative') {
            $PSNativeCommandUseErrorActionPreference = $previousNative
        }
        $ErrorActionPreference = $previousPreference
        Set-Location -LiteralPath $previousLocation
    }
}

function Write-Summary {
    param([object[]] $Gates)

    Write-Host ''
    Write-Host 'Gate results'
    Write-Host '------------'
    foreach ($gate in $Gates) {
        Write-Host ('  {0,-32} {1,-10} {2}' -f $gate.name, $gate.status, $gate.reason)
        foreach ($step in $gate.steps) {
            Write-Host ('      {0,-7} {1,-8} exit {2,-4} {3:n1}s' -f $step.key, $step.status, $step.exitCode, $step.durationSeconds)
        }
    }
}

# ---------------------------------------------------------------------------
# Resolve paths and configuration
# ---------------------------------------------------------------------------

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $repositoryRoot '.codex\skills\project.toml'
}
elseif (-not [System.IO.Path]::IsPathRooted($ConfigPath)) {
    $ConfigPath = Join-Path $repositoryRoot $ConfigPath
}

$startedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

try {
    $configuration = Resolve-Configuration -Path $ConfigPath
    $inventory = Get-GateInventory -Configuration $configuration
}
catch {
    Write-Host "Configuration error: $($_.Exception.Message)"
    exit $ExitConfig
}

# Present in the configuration but unclassified is a hard error. Classified but
# absent from the configuration is only a warning: ci-gates does not exist until
# it is registered, and a missing gate must never fail a run.
$unclassified = @($inventory.Keys | Where-Object { -not $GateClassification.Contains($_) })
if ($unclassified.Count -gt 0) {
    Write-Host 'Configuration error: unclassified build gate(s) found.'
    foreach ($name in $unclassified) {
        Write-Host "  unclassified gate: $name"
    }
    Write-Host ''
    Write-Host 'Every [build-gate.*] name must be classified in Invoke-LhmGates.ps1 before it can be'
    Write-Host 'included in or excluded from a CI run. Refusing to continue.'
    exit $ExitConfig
}

$missing = @($GateClassification.Keys | Where-Object { -not $inventory.Contains($_) })

# ---------------------------------------------------------------------------
# List mode
# ---------------------------------------------------------------------------

$mode = 'run'
if ($List) { $mode = 'list' }
elseif ($DryRun) { $mode = 'dryrun' }

if ($List) {
    Write-Host "Gate classification for $ConfigPath"
    Write-Host ''
    Write-Host ('  {0,-32} {1,-12} {2,-16} {3}' -f 'Gate', 'Class', 'Allowed keys', 'Reason')
    Write-Host ('  {0,-32} {1,-12} {2,-16} {3}' -f ('-' * 32), ('-' * 12), ('-' * 16), ('-' * 6))
    foreach ($name in $GateClassification.Keys) {
        $entry = $GateClassification[$name]
        $present = $inventory.Contains($name)
        $allowed = '(none)'
        if ($entry.AllowedKeys.Count -gt 0) { $allowed = ($entry.AllowedKeys -join ', ') }
        $suffix = ''
        if (-not $present) { $suffix = '  [not present in configuration]' }
        Write-Host ('  {0,-32} {1,-12} {2,-16} {3}{4}' -f $name, $entry.Classification, $allowed, $entry.Reason, $suffix)
        foreach ($excluded in $entry.ExcludedKeys) {
            Write-Host ('      excluded key {0}: {1}' -f $excluded.Key, $excluded.Reason)
        }
    }

    if ($missing.Count -gt 0) {
        Write-Host ''
        Write-Host 'Warning: classified but not present in the configuration:'
        foreach ($name in $missing) { Write-Host "  $name" }
    }

    Write-Host ''
    Write-Host 'Deny-list patterns (matched against every resolved command before execution):'
    foreach ($pattern in $DenyList) { Write-Host "  $pattern" }

    Write-Host ''
    Write-Host 'Test-script convention for eng/ci/tests/Test-*.ps1:'
    Write-Host '  - self-contained, no required parameter'
    Write-Host '  - one line per assertion, prefixed PASS:, FAIL:, or SKIP:'
    Write-Host '  - SKIP: only for a genuinely unavailable optional dependency'
    Write-Host '  - writes nothing outside a directory it creates under $env:TEMP and removes on exit'
    Write-Host '  - exits 0 only when no assertion failed'

    Write-Host ''
    Write-Host 'Exit codes: 0 ok, 1 gate failed, 2 configuration error, 3 command refused.'
}

# ---------------------------------------------------------------------------
# Selection
# ---------------------------------------------------------------------------

$selected = @()
if (-not $List) {
    if ($Gate -and $Gate.Count -gt 0) {
        foreach ($name in $Gate) {
            if (-not $GateClassification.Contains($name)) {
                Write-Host "Configuration error: unknown gate '$name'."
                Write-Host "Known gates: $($GateClassification.Keys -join ', ')"
                exit $ExitConfig
            }
            $entry = $GateClassification[$name]
            if ($entry.AllowedKeys.Count -eq 0) {
                Write-Host "Configuration error: gate '$name' is excluded and cannot be run."
                Write-Host "  reason: $($entry.Reason)"
                exit $ExitConfig
            }
            if (-not $inventory.Contains($name)) {
                Write-Host "Configuration error: gate '$name' is not present in $ConfigPath."
                exit $ExitConfig
            }
            $selected += $name
        }
    }
    else {
        foreach ($name in $GateClassification.Keys) {
            $entry = $GateClassification[$name]
            if ($entry.AllowedKeys.Count -gt 0 -and $inventory.Contains($name)) {
                $selected += $name
            }
        }
    }
}

# ---------------------------------------------------------------------------
# Execution
# ---------------------------------------------------------------------------

$gateResults = @()
$refusedCount = 0
$failedCount = 0
$passedCount = 0
$skippedCount = 0
$stopped = $false

foreach ($name in $GateClassification.Keys) {
    $entry = $GateClassification[$name]
    $isSelected = ($selected -contains $name)

    $result = [ordered]@{
        name           = $name
        classification = $entry.Classification
        reason         = $entry.Reason
        allowedKeys    = @($entry.AllowedKeys)
        excludedKeys   = @($entry.ExcludedKeys | ForEach-Object { [ordered]@{ key = $_.Key; reason = $_.Reason } })
        steps          = @()
        status         = 'Skipped'
    }

    if (-not $isSelected -or $stopped) {
        $gateResults += , $result
        if (-not $List) { $skippedCount++ }
        continue
    }

    Write-Host ''
    Write-Host "== $name =="

    $gateRefused = $false
    $gateFailed = $false

    foreach ($step in $inventory[$name]) {
        if ($entry.AllowedKeys -notcontains $step.Key) { continue }

        $denied = Get-DenyMatch -Command $step.Command
        if ($null -ne $denied) {
            Write-Host "   REFUSED $($step.Key): matches deny-list pattern '$denied'"
            Write-Host "     command: $($step.Command)"
            $result.steps += , ([ordered]@{
                    key             = $step.Key
                    command         = $step.Command
                    status          = 'Refused'
                    exitCode        = $null
                    durationSeconds = 0.0
                    deniedPattern   = $denied
                })
            $gateRefused = $true
            break
        }

        if ($DryRun) {
            Write-Host "   would run $($step.Key): $($step.Command)"
            $result.steps += , ([ordered]@{
                    key             = $step.Key
                    command         = $step.Command
                    status          = 'DryRun'
                    exitCode        = $null
                    durationSeconds = 0.0
                })
            continue
        }

        Write-Host "   $($step.Key): $($step.Command)"
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $exitCode = Invoke-GateCommand -Command $step.Command -WorkingDirectory $repositoryRoot
        $clock.Stop()
        $stepStatus = 'Passed'
        if ($exitCode -ne 0) { $stepStatus = 'Failed' }

        $result.steps += , ([ordered]@{
                key             = $step.Key
                command         = $step.Command
                status          = $stepStatus
                exitCode        = $exitCode
                durationSeconds = [math]::Round($clock.Elapsed.TotalSeconds, 2)
            })

        Write-Host ('   -> {0} (exit {1}, {2:n1}s)' -f $stepStatus, $exitCode, $clock.Elapsed.TotalSeconds)

        if ($stepStatus -eq 'Failed') {
            $gateFailed = $true
            break
        }
    }

    if ($gateRefused) {
        $result.status = 'Refused'
        $refusedCount++
    }
    elseif ($gateFailed) {
        $result.status = 'Failed'
        $failedCount++
    }
    elseif ($DryRun) {
        $result.status = 'Skipped'
        $skippedCount++
    }
    else {
        $result.status = 'Passed'
        $passedCount++
    }

    $gateResults += , $result

    if ($FailFast -and ($gateRefused -or $gateFailed)) {
        Write-Host ''
        Write-Host "-FailFast: stopping after $name."
        $stopped = $true
    }
}

# ---------------------------------------------------------------------------
# Summary and exit
# ---------------------------------------------------------------------------

$exitCode = $ExitOk
if ($failedCount -gt 0) { $exitCode = $ExitFailed }
# Refusal outranks a plain failure: a denied command is a contract violation,
# not a red test.
if ($refusedCount -gt 0) { $exitCode = $ExitRefused }

if (-not $List) {
    Write-Summary -Gates $gateResults
    Write-Host ''
    Write-Host ('  passed {0}  failed {1}  refused {2}  skipped {3}  exit {4}' -f $passedCount, $failedCount, $refusedCount, $skippedCount, $exitCode)
}

if (-not [string]::IsNullOrWhiteSpace($JsonSummary)) {
    $includedCount = @($GateClassification.Keys | Where-Object { $GateClassification[$_].AllowedKeys.Count -gt 0 }).Count
    $summary = [ordered]@{
        schemaVersion  = 1
        configPath     = $ConfigPath
        repositoryRoot = $repositoryRoot
        machineName    = $env:COMPUTERNAME
        mode           = $mode
        startedAt      = $startedAt
        completedAt    = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        denyList       = @($DenyList)
        gates          = @($gateResults)
        summary        = [ordered]@{
            included = $includedCount
            excluded = ($GateClassification.Count - $includedCount)
            run      = $selected.Count
            passed   = $passedCount
            failed   = $failedCount
            refused  = $refusedCount
            skipped  = $skippedCount
        }
        exitCode       = $exitCode
    }

    $summaryPath = $JsonSummary
    if (-not [System.IO.Path]::IsPathRooted($summaryPath)) {
        $summaryPath = Join-Path $repositoryRoot $summaryPath
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Host "Summary written to $summaryPath"
}

exit $exitCode

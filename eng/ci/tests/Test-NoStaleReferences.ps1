[CmdletBinding()]
param()

# Permanent stale-current-reference gate.
# Asserts that no tracked configuration or current document references a path
# that no longer exists after a taxonomy move, while explicitly allowing
# immutable historical evidence (completed agent specs, rendered campaign
# documents, the acceptance ledger, and plan JSON) via a centralized allow-list.
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

# Data: relocated items (old root-relative path -> new nested-relative path).
$moveMap = @(
    @{ Old = 'LibreHardwareMonitor.Avalonia.Spike.slnx';       New = 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.slnx' }
    @{ Old = 'LibreHardwareMonitor.Avalonia.Spike.Core';       New = 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.Core' }
    @{ Old = 'LibreHardwareMonitor.Avalonia.Spike';            New = 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike' }
    @{ Old = 'LibreHardwareMonitor.Avalonia.Spike.Tests';      New = 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.Tests' }
    @{ Old = 'scripts/Test-AvaloniaSpike.ps1';                 New = 'experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1' }
)

# Data: allow-list of exempt paths (immutable historical evidence).
$exemptPatterns = @(
    '^agents/agent-[^/]+\.md$'
    '^docs/campaign-plan-[^/]+\.md$'
    '^docs/campaign-history\.md$'
    '^live-tracker\.md$'
    '^data/plans/[^/]+\.json$'
    '^eng/ci/tests/Test-NoStaleReferences\.ps1$'
)

# 1. Existence: each new path exists, each old path is gone.
foreach ($m in $moveMap) {
    $oldFull = Join-Path $repositoryRoot ($m.Old -replace '/', '\')
    $newFull = Join-Path $repositoryRoot ($m.New -replace '/', '\')
    Assert-Ok "move complete: $($m.Old) -> $($m.New)" {
        (-not (Test-Path -LiteralPath $oldFull)) -and (Test-Path -LiteralPath $newFull)
    }
}

# Tracked text files (config + docs) minus the allow-list.
$tracked = @(git ls-files) | Where-Object { $_ -match '\.(toml|md|ps1|yml)$' }
function Test-Exempt([string]$Path) {
    foreach ($p in $exemptPatterns) { if ($Path -match $p) { return $true } }
    return $false
}
$scannable = @($tracked | Where-Object { -not (Test-Exempt $_) })

# 2. Config staleness: project.toml has no bare (non-experiments-prefixed) spike path.
$configFull = Join-Path $repositoryRoot '.codex\skills\project.toml'
$configText = Get-Content -Raw -LiteralPath $configFull
$bare = [regex]::Matches($configText,
    '(?<!experiments[/\\]+avalonia-fixture-explorer[/\\]+)LibreHardwareMonitor\.Avalonia\.Spike')
Assert-Ok ".codex/skills/project.toml has no bare spike path references" { $bare.Count -eq 0 }

# 3. Unambiguous doc staleness: no non-exempt file references the old script location.
$oldScriptRe = 'scripts[/\\]+Test-AvaloniaSpike\.ps1'
$scriptHits = @()
foreach ($f in $scannable) {
    $full = Join-Path $repositoryRoot ($f -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) { continue }
    if ([regex]::IsMatch((Get-Content -Raw -LiteralPath $full), $oldScriptRe)) { $scriptHits += $f }
}
Assert-Ok "no non-exempt tracked file references the old script location" { $scriptHits.Count -eq 0 }

# 4. Solution-path staleness: no non-exempt current document references the old root .slnx.
$slnxRe = '(?<!experiments[/\\]+avalonia-fixture-explorer[/\\]+)LibreHardwareMonitor\.Avalonia\.Spike\.slnx'
$slnxHits = @()
foreach ($f in ($scannable | Where-Object { $_ -match '\.md$' })) {
    $full = Join-Path $repositoryRoot ($f -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) { continue }
    if ([regex]::IsMatch((Get-Content -Raw -LiteralPath $full), $slnxRe)) { $slnxHits += $f }
}
Assert-Ok "no non-exempt current document references the old root .slnx" { $slnxHits -eq 0 -or $slnxHits.Count -eq 0 }

Write-Output "-> " + $(if ($failures.Count -eq 0) { 'ok' } else { "FAILED ($($failures.Count) failures)" })
if ($scriptHits.Count -gt 0)  { Write-Output ("  old-script refs: " + ($scriptHits -join '; ')) }
if ($slnxHits.Count -gt 0)    { Write-Output ("  old-slnx refs: " + ($slnxHits -join '; ')) }
if ($failures.Count -gt 0) { exit 1 }
exit 0

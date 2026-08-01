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
    @{ Old = 'ops/release';                                      New = 'ops/candidate' }
    @{ Old = 'ops/local-release';                                New = 'ops/deploy/snd-desk' }
    @{ Old = 'scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1'; New = 'eng/Clear-LhmRepositoryBuildOutputs.ps1' }
    @{ Old = 'docs/refactor-roadmap.md';                         New = 'docs/architecture/refactor-roadmap.md' }
    @{ Old = 'docs/repository-build-output-cleanup.md';          New = 'docs/architecture/repository-build-output-cleanup.md' }
)

# Plan-006: the flat test project became four boundary suites. Entries are
# per-file because the parent LibreHardwareMonitor.Tests/ directory survives,
# so a directory-level entry could not prove any individual move completed.
$plan006SuiteFiles = @{
    'LibreHardwareMonitor.Tests.Library' = @(
        'ComputerOpenLifetimeTests.cs', 'MotherboardModelCompatibilityTests.cs',
        'Nct677XFanConfigTests.cs', 'NvidiaGroupSnapshotTests.cs',
        'SensorHistoryTests.cs', 'StorageGroupLifetimeTests.cs',
        'StorageSmartUpdateCycleTests.cs')
    'LibreHardwareMonitor.Tests.Application' = @(
        'HardwareOperationCoordinatorTests.cs', 'PlotPanelHistoryTests.cs',
        'PlotPanelTextScaleTests.cs', 'RuntimePathsTests.cs',
        'SettingsPersistenceTests.cs', 'SmartUpdateCyclePolicyTests.cs',
        'StartupManagerTests.cs', 'TemperatureRateSensorTests.cs',
        'TextScaleSliderMenuTests.cs', 'UiScaleTests.cs',
        'UiShutdownCoordinatorTests.cs', 'UiTextScaleCommitGateTests.cs',
        'WinFormsUiLifetimeTests.cs')
    'LibreHardwareMonitor.Tests.Contracts' = @(
        'DataJsonGoldenTests.cs', 'data.golden.json',
        'HttpServerAuthenticationTests.cs', 'HttpServerLifetimeTests.cs',
        'HttpServerPrometheusTests.cs', 'HttpServerSensorApiTests.cs',
        'CsvTimestampContractTests.cs', 'WebDashboardRetirementTests.cs')
}
foreach ($suite in ($plan006SuiteFiles.Keys | Sort-Object)) {
    foreach ($file in $plan006SuiteFiles[$suite]) {
        $moveMap += @{
            Old = "LibreHardwareMonitor.Tests/$file"
            New = "LibreHardwareMonitor.Tests/$suite/$file"
        }
    }
}

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

# 2b. Config staleness: project.toml has no dissolved ops path references.
$dissolvedConfig = [regex]::Matches($configText, 'ops[/\\]+release|ops[/\\]+local-release|scripts[/\\]+local-release')
Assert-Ok ".codex/skills/project.toml has no dissolved ops path references" { $dissolvedConfig.Count -eq 0 }

# 2c. No non-exempt tracked text file references a dissolved ops path.
$dissolvedRe = 'ops[/\\]+release(?![/\\]+notes)|ops[/\\]+local-release|scripts[/\\]+local-release'
$dissolvedHits = @()
foreach ($f in $scannable) {
    $full = Join-Path $repositoryRoot ($f -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) { continue }
    if ([regex]::IsMatch((Get-Content -Raw -LiteralPath $full), $dissolvedRe)) { $dissolvedHits += $f }
}
Assert-Ok "no non-exempt tracked file references a dissolved ops path" { $dissolvedHits.Count -eq 0 }

# 2d. No non-exempt tracked text file references a retired discovery doc or a bare (non-features-prefixed) doc path.
$retiredRe = 'docs[/\\]+discovery-[a-z]'
$bareDocRe = '(?<!features[/\\]+)docs[/\\]+feature-|(?<!architecture[/\\]+)docs[/\\]+refactor-roadmap|(?<!architecture[/\\]+)docs[/\\]+repository-build-output-cleanup'
$docHits = @()
foreach ($f in $scannable) {
    $full = Join-Path $repositoryRoot ($f -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) { continue }
    $t = Get-Content -Raw -LiteralPath $full
    if ([regex]::IsMatch($t, $retiredRe) -or [regex]::IsMatch($t, $bareDocRe)) { $docHits += $f }
}
Assert-Ok "no non-exempt tracked file references a retired or bare doc path" { $docHits.Count -eq 0 }

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

# 5. Plan-006 dissolved project: the retired flat test csproj is gone and the
#    four suite projects plus the deterministic solution filter exist.
$retiredCsproj = Join-Path $repositoryRoot 'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj'
Assert-Ok "retired flat test csproj is dissolved" { -not (Test-Path -LiteralPath $retiredCsproj) }
$plan006NewProjects = @(
    'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf'
    'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj'
    'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj'
    'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj'
    'LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Attended\LibreHardwareMonitor.Tests.Attended.csproj'
)
foreach ($p in $plan006NewProjects) {
    Assert-Ok "suite artifact exists: $p" { Test-Path -LiteralPath (Join-Path $repositoryRoot $p) }
}

# 6. Plan-006 textual staleness over a widened file set. The default scan set
#    (.toml|.md|.ps1|.yml) cannot see .csproj/.sln/.slnf/.json, which is exactly
#    where stale test-project paths would hide after this campaign, so this
#    block scans those too. Bare directory references (module globs like
#    "LibreHardwareMonitor.Tests/") remain legal: both patterns require a file
#    segment that does not begin with the suite prefix.
$wideTracked = @(git ls-files) | Where-Object { $_ -match '\.(toml|md|ps1|yml|csproj|sln|slnf|json)$' }
$wideScannable = @($wideTracked | Where-Object { -not (Test-Exempt $_) })
$oldTestCsprojRe = 'LibreHardwareMonitor\.Tests[/\\]+LibreHardwareMonitor\.Tests\.csproj'
$flatTestFileRe = 'LibreHardwareMonitor\.Tests[/\\]+(?!LibreHardwareMonitor\.Tests\.)[A-Za-z0-9_.-]+\.(?:cs|json|csproj)'
$oldTestHits = @()
foreach ($f in $wideScannable) {
    $full = Join-Path $repositoryRoot ($f -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) { continue }
    $t = Get-Content -Raw -LiteralPath $full
    if ([regex]::IsMatch($t, $oldTestCsprojRe) -or [regex]::IsMatch($t, $flatTestFileRe)) { $oldTestHits += $f }
}
Assert-Ok "no non-exempt tracked file references the retired flat test project layout" { $oldTestHits.Count -eq 0 }

Write-Output "-> " + $(if ($failures.Count -eq 0) { 'ok' } else { "FAILED ($($failures.Count) failures)" })
if ($scriptHits.Count -gt 0)  { Write-Output ("  old-script refs: " + ($scriptHits -join '; ')) }
if ($slnxHits.Count -gt 0)    { Write-Output ("  old-slnx refs: " + ($slnxHits -join '; ')) }
if ($oldTestHits.Count -gt 0) { Write-Output ("  retired-test-layout refs: " + ($oldTestHits -join '; ')) }
if ($failures.Count -gt 0) { exit 1 }
exit 0

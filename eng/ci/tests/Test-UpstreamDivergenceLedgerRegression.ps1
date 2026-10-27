[CmdletBinding()]
param()

# Exercise the real check against isolated Git history, without altering this checkout.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$engine = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$temp = Join-Path ([IO.Path]::GetTempPath()) ('lhm-ledger-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $temp 'source'
$failures = 0

function Invoke-FixtureGit {
    param([string[]]$Arguments)
    $output = & git -C $fixture -c commit.gpgsign=false -c core.autocrlf=false @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $output" }
    return $output
}

function Assert-Check {
    param([string]$Name, [string]$Root, [int]$ExpectedExit, [string]$ExpectedText)
    $check = Join-Path $Root 'eng/ci/tests/Test-UpstreamDivergenceLedger.ps1'
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $check 2>&1
    $code = $LASTEXITCODE
    if ($code -eq $ExpectedExit -and ($output -join "`n") -match $ExpectedText) {
        Write-Output "PASS: $Name"
    } else {
        Write-Output "FAIL: $Name (exit $code; expected $ExpectedExit)"
        $output | ForEach-Object { Write-Output "  $_" }
        $script:failures++
    }
}

try {
    New-Item -ItemType Directory -Path (Join-Path $fixture 'eng/ci/tests') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-UpstreamDivergenceLedger.ps1') `
        -Destination (Join-Path $fixture 'eng/ci/tests/Test-UpstreamDivergenceLedger.ps1')
    [IO.File]::WriteAllText((Join-Path $fixture 'README.md'), 'upstream')
    Invoke-FixtureGit @('init', '-q') | Out-Null
    Invoke-FixtureGit @('config', 'user.name', 'Ledger fixture') | Out-Null
    Invoke-FixtureGit @('config', 'user.email', 'ledger@example.invalid') | Out-Null
    Invoke-FixtureGit @('config', 'core.hooksPath', (Join-Path $temp 'hooks')) | Out-Null
    Invoke-FixtureGit @('add', '--all') | Out-Null
    Invoke-FixtureGit @('commit', '-qm', 'upstream') | Out-Null
    $base = [string](Invoke-FixtureGit @('rev-parse', 'HEAD'))

    New-Item -ItemType Directory -Path (Join-Path $fixture 'docs/architecture') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixture 'LibreHardwareMonitorLib') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'README.md'), 'fork')
    [IO.File]::WriteAllText((Join-Path $fixture 'LibreHardwareMonitorLib/Fork.cs'), 'fork-only')
    $ledgerPath = Join-Path $fixture 'docs/architecture/upstream-divergence.md'
    $ledger = @'
**Upstream base:** `BASE`
<!-- ledger:upstream-owned:begin -->
| `README.md` | fork | fixture |
<!-- ledger:upstream-owned:end -->
<!-- ledger:fork-only:begin -->
| `LibreHardwareMonitorLib/Fork.cs` | fixture |
<!-- ledger:fork-only:end -->
'@.Replace('BASE', $base)
    [IO.File]::WriteAllText($ledgerPath, $ledger)
    Invoke-FixtureGit @('add', '--all') | Out-Null
    Invoke-FixtureGit @('commit', '-qm', 'documented fork') | Out-Null

    Assert-Check 'complete history and ledger pass' $fixture 0 'PASS: every upstream-owned path'

    [IO.File]::WriteAllText($ledgerPath, $ledger.Replace($base, ('0' * 40)))
    Assert-Check 'a nonexistent pinned base fails instead of skipping' $fixture 1 'FAIL: pinned upstream base is available'

    [IO.File]::WriteAllText($ledgerPath, $ledger.Replace('`README.md`', '`omitted`'))
    Assert-Check 'an undocumented upstream change fails' $fixture 1 'missing row: README.md'

    [IO.File]::WriteAllText($ledgerPath, $ledger.Replace('`LibreHardwareMonitorLib/Fork.cs`', '`omitted`'))
    Assert-Check 'an undocumented fork-only product file fails' $fixture 1 'unlisted fork-only file: LibreHardwareMonitorLib/Fork.cs'
    [IO.File]::WriteAllText($ledgerPath, $ledger)

    $shallow = Join-Path $temp 'shallow'
    $sourceUri = ([uri]($fixture + [IO.Path]::DirectorySeparatorChar)).AbsoluteUri
    Invoke-FixtureGit @('clone', '-q', '--depth=1', $sourceUri, $shallow) | Out-Null
    Assert-Check 'a shallow clone missing its base fails instead of skipping' $shallow 1 'FAIL: pinned upstream base is available'
}
finally {
    # Delete only the random directory this fixture created under the system temp root.
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($temp)
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^lhm-ledger-[0-9a-f]{32}$') {
        throw "Refusing fixture cleanup outside its temporary directory: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

if ($failures -gt 0) { exit 1 }
exit 0

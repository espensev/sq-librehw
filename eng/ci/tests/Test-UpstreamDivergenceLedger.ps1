[CmdletBinding()]
param(
    [string] $LedgerPath = 'docs/architecture/upstream-divergence.md'
)

# Upstream divergence ledger gate.
# Asserts that docs/architecture/upstream-divergence.md names every upstream-owned
# path that differs from the pinned upstream base, names no upstream-owned path
# that no longer differs, lists every fork-only file added inside the inherited
# product roots, and still pins the merge-base with a fetched upstream/master.
# Missing base history is a failure: a skipped comparison cannot establish coverage.
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

function Invoke-Git {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git -C $repositoryRoot -c core.quotePath=false @Arguments 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Lines    = @($output | ForEach-Object { [string]$_ } | Where-Object { $_ -ne '' })
    }
}

function Get-RegionText {
    param([string]$Text, [string]$Name)
    $begin = "<!-- ledger:${Name}:begin -->"
    $end = "<!-- ledger:${Name}:end -->"
    $start = $Text.IndexOf($begin, [System.StringComparison]::Ordinal)
    $stop = $Text.IndexOf($end, [System.StringComparison]::Ordinal)
    if ($start -lt 0 -or $stop -lt 0 -or $stop -le $start) { return $null }
    return $Text.Substring($start + $begin.Length, $stop - $start - $begin.Length)
}

function Get-BacktickTokens {
    param([string]$Text)
    $tokens = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($Text, '`([^`\r\n]+)`')) {
        $value = $match.Groups[1].Value.Trim().Replace('\', '/')
        if ($value.Length -gt 0) { [void]$tokens.Add($value) }
    }
    return $tokens
}

$ledgerFullPath = Join-Path $repositoryRoot $LedgerPath
Assert-Ok "ledger exists at $LedgerPath" { Test-Path -LiteralPath $ledgerFullPath -PathType Leaf }
if ($failures.Count -gt 0) { exit 1 }

$text = [System.IO.File]::ReadAllText($ledgerFullPath, [System.Text.Encoding]::UTF8)
$baseMatch = [regex]::Match($text, '(?m)^\*\*Upstream base:\*\*\s+`([0-9a-f]{40})`')
Assert-Ok 'ledger pins a full 40-hex upstream base commit' { $baseMatch.Success }
if ($failures.Count -gt 0) { exit 1 }
$base = $baseMatch.Groups[1].Value

$upstreamRegion = Get-RegionText -Text $text -Name 'upstream-owned'
$forkOnlyRegion = Get-RegionText -Text $text -Name 'fork-only'
Assert-Ok 'ledger has the upstream-owned marker region' { $null -ne $upstreamRegion }
Assert-Ok 'ledger has the fork-only marker region' { $null -ne $forkOnlyRegion }
if ($failures.Count -gt 0) { exit 1 }

$baseAvailable = (Invoke-Git -Arguments @('cat-file', '-e', "$base^{commit}")).ExitCode -eq 0
Assert-Ok 'pinned upstream base is available' { $baseAvailable }
if (-not $baseAvailable) {
    Write-Output "Fetch history containing upstream base $base before running this check; shallow clones may need git fetch --unshallow."
    exit 1
}

Assert-Ok "pinned base $($base.Substring(0, 8)) is an ancestor of HEAD" {
    (Invoke-Git -Arguments @('merge-base', '--is-ancestor', $base, 'HEAD')).ExitCode -eq 0
}

$upstreamRef = Invoke-Git -Arguments @('rev-parse', '--verify', '-q', 'refs/remotes/upstream/master')
if ($upstreamRef.ExitCode -eq 0) {
    $mergeBase = (Invoke-Git -Arguments @('merge-base', 'HEAD', 'refs/remotes/upstream/master')).Lines | Select-Object -First 1
    Assert-Ok "pinned base equals the merge-base with upstream/master ($mergeBase)" { $mergeBase -eq $base }
} else {
    Write-Output 'SKIP: upstream/master is not fetched; merge-base freshness not compared'
}

$diff = Invoke-Git -Arguments @('diff', '--name-status', $base, '--')
Assert-Ok 'git diff against the pinned base succeeded' { $diff.ExitCode -eq 0 }
if ($failures.Count -gt 0) { exit 1 }

$differing = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($line in $diff.Lines) {
    $parts = $line -split "`t"
    if ($parts.Count -lt 2) { continue }
    $status = $parts[0].Substring(0, 1)
    switch ($status) {
        'A' { }
        'R' { [void]$differing.Add($parts[1]) }
        'C' { [void]$differing.Add($parts[1]) }
        default { [void]$differing.Add($parts[1]) }
    }
}

$baseTree = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($path in (Invoke-Git -Arguments @('ls-tree', '-r', '--name-only', $base)).Lines) { [void]$baseTree.Add($path) }
$headTree = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($path in (Invoke-Git -Arguments @('ls-tree', '-r', '--name-only', 'HEAD')).Lines) { [void]$headTree.Add($path) }

$upstreamTokens = Get-BacktickTokens -Text $upstreamRegion
$forkOnlyTokens = Get-BacktickTokens -Text $forkOnlyRegion

$listedUpstreamOwned = @($upstreamTokens | Where-Object { $baseTree.Contains($_) })
Write-Output "  upstream-owned paths differing from $($base.Substring(0, 8)): $($differing.Count); ledger rows naming an upstream-owned path: $($listedUpstreamOwned.Count)"

$missing = @($differing | Where-Object { -not $upstreamTokens.Contains($_) } | Sort-Object)
Assert-Ok 'every upstream-owned path that differs from the base has a ledger row' { $missing.Count -eq 0 }
foreach ($path in $missing) { Write-Output "  missing row: $path" }

$stale = @($listedUpstreamOwned | Where-Object { -not $differing.Contains($_) } | Sort-Object)
Assert-Ok 'no ledger row names an upstream-owned path that no longer differs' { $stale.Count -eq 0 }
foreach ($path in $stale) { Write-Output "  stale row: $path" }

$unknown = @($upstreamTokens | Where-Object { $_.Contains('/') -and -not $_.EndsWith('/') -and -not $baseTree.Contains($_) -and -not $headTree.Contains($_) } | Sort-Object)
Assert-Ok 'every path named in the upstream-owned region exists in the base or in HEAD' { $unknown.Count -eq 0 }
foreach ($path in $unknown) { Write-Output "  unknown path: $path" }

$forkOnlyMissing = @($forkOnlyTokens | Where-Object { $_.Contains('/') -and -not $_.EndsWith('/') -and -not $headTree.Contains($_) } | Sort-Object)
Assert-Ok 'every path named in the fork-only region exists in HEAD' { $forkOnlyMissing.Count -eq 0 }
foreach ($path in $forkOnlyMissing) { Write-Output "  fork-only path not in HEAD: $path" }

$forkOnlyInBase = @($forkOnlyTokens | Where-Object { $baseTree.Contains($_) } | Sort-Object)
Assert-Ok 'no path named in the fork-only region exists in the upstream base' { $forkOnlyInBase.Count -eq 0 }
foreach ($path in $forkOnlyInBase) { Write-Output "  upstream-owned path listed as fork-only: $path" }

$productRoots = @('Aga.Controls/', 'LibreHardwareMonitorLib/', 'LibreHardwareMonitor.Windows.Forms/')
$forkOnlyInProductRoots = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($line in $diff.Lines) {
    $parts = $line -split "`t"
    if ($parts.Count -lt 2) { continue }
    $status = $parts[0].Substring(0, 1)
    $added = $null
    if ($status -eq 'A') { $added = $parts[1] }
    elseif (($status -eq 'R' -or $status -eq 'C') -and $parts.Count -ge 3) { $added = $parts[2] }
    if ($null -eq $added) { continue }
    foreach ($root in $productRoots) {
        if ($added.StartsWith($root, [System.StringComparison]::Ordinal)) { [void]$forkOnlyInProductRoots.Add($added) }
    }
}
$forkOnlyListed = @($forkOnlyTokens | Where-Object { $forkOnlyInProductRoots.Contains($_) })
Write-Output "  fork-only files inside the product roots: $($forkOnlyInProductRoots.Count); listed in the fork-only region: $($forkOnlyListed.Count)"
$forkOnlyUnlisted = @($forkOnlyInProductRoots | Where-Object { -not $forkOnlyTokens.Contains($_) } | Sort-Object)
Assert-Ok 'every fork-only file inside an inherited product root is listed in the fork-only region' { $forkOnlyUnlisted.Count -eq 0 }
foreach ($path in $forkOnlyUnlisted) { Write-Output "  unlisted fork-only file: $path" }

if ($failures.Count -gt 0) {
    Write-Output "Failed assertions: $($failures.Count)"
    exit 1
}

exit 0

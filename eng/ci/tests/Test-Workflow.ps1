<#
.SYNOPSIS
    Structural regression suite for .github/workflows/non-deploying-gates.yml.

.DESCRIPTION
    The workflow cannot be verified by running it: origin is deliberately
    unpushed, so it has never executed on a hosted runner. These assertions are
    the substitute, and they are deliberately about safety properties rather
    than about whether the workflow would succeed.

    The safety-critical assertions are text-based so they run whether or not
    PyYAML is installed. The YAML parse adds structural confirmation on top and
    reports SKIP, not PASS, when PyYAML is unavailable.

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
$workflowPath = Join-Path $repositoryRoot '.github\workflows\non-deploying-gates.yml'
$runner = Join-Path $repositoryRoot 'eng\ci\Invoke-LhmGates.ps1'
$engine = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName

$script:Failures = 0

function Assert-Case {
    param([string] $Name, [bool] $Condition, [string] $Detail = '')
    if ($Condition) { Write-Host "PASS: $Name" }
    else {
        Write-Host "FAIL: $Name$(if ($Detail) { " - $Detail" })"
        $script:Failures++
    }
}

function Skip-Case {
    param([string] $Reason)
    Write-Host "SKIP: $Reason"
}

function Get-Block {
    # Collect the lines belonging to a key: the key line plus every following
    # line indented further than it, stopping at the first line that is not.
    param([string[]] $Lines, [int] $Index)

    $indent = ($Lines[$Index] -replace '\S.*$', '').Length
    $block = @($Lines[$Index])
    for ($i = $Index + 1; $i -lt $Lines.Count; $i++) {
        $line = $Lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) { $block += $line; continue }
        $lineIndent = ($line -replace '\S.*$', '').Length
        if ($lineIndent -le $indent) { break }
        $block += $line
    }
    return $block
}

$temp = Join-Path $env:TEMP ("lhm-ci-workflow-" + [guid]::NewGuid().ToString('N').Substring(0, 12))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    # --- Case 1: the workflow exists -----------------------------------------
    $exists = Test-Path -LiteralPath $workflowPath
    $text = ''
    if ($exists) { $text = Get-Content -LiteralPath $workflowPath -Raw }
    Assert-Case 'the workflow file exists and is non-empty' `
        ($exists -and -not [string]::IsNullOrWhiteSpace($text)) `
        "path: $workflowPath"

    if (-not $exists) {
        Write-Host 'Cannot continue without the workflow file.'
        exit 1
    }

    $lines = @(Get-Content -LiteralPath $workflowPath)
    # Strip comments before content assertions: the header deliberately
    # discusses deploying and publishing, and must not trip the checks.
    $bodyLines = @($lines | Where-Object { $_ -notmatch '^\s*#' })
    $body = $bodyLines -join "`n"

    # --- Case 2: it parses as YAML -------------------------------------------
    $parsed = $null
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) {
        Assert-Case 'the workflow parses as YAML' $false 'python is not available, and this repository requires it'
    }
    else {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & python -c 'import yaml' 2>&1 | Out-Null
        $hasYaml = ($LASTEXITCODE -eq 0)
        $ErrorActionPreference = $previous

        if (-not $hasYaml) {
            Skip-Case 'PyYAML unavailable; YAML parse not executed'
        }
        else {
            $script = 'import json,sys,yaml;sys.stdout.write(json.dumps(yaml.safe_load(open(sys.argv[1],encoding=''utf-8''))))'
            $previous = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            $raw = & python -c $script $workflowPath 2>&1
            $parseOk = ($LASTEXITCODE -eq 0)
            $ErrorActionPreference = $previous

            if ($parseOk) { $parsed = ($raw | Out-String).Trim() | ConvertFrom-Json }
            Assert-Case 'the workflow parses as YAML' $parseOk "$raw"
        }
    }

    # --- Case 3: permissions are exactly contents: read ----------------------
    $permissionIndex = -1
    for ($i = 0; $i -lt $bodyLines.Count; $i++) {
        if ($bodyLines[$i] -match '^permissions:\s*$') { $permissionIndex = $i; break }
    }
    $permissionEntries = @()
    if ($permissionIndex -ge 0) {
        $permissionEntries = @(Get-Block -Lines $bodyLines -Index $permissionIndex |
            Select-Object -Skip 1 |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() })
    }
    Assert-Case 'top-level permissions grant exactly contents: read' `
        (($permissionIndex -ge 0) -and ($permissionEntries.Count -eq 1) -and ($permissionEntries[0] -eq 'contents: read')) `
        "entries: $($permissionEntries -join '; ')"

    # --- Case 4: no repository secret is referenced --------------------------
    Assert-Case 'the workflow references no repository secret' `
        (-not ($body -match 'secrets\.')) `
        'found a secrets. reference'

    # --- Case 5: no deploying or publishing action ---------------------------
    $usesValues = @($bodyLines |
        Where-Object { $_ -match '^\s*uses:\s*(\S+)' } |
        ForEach-Object { ($_ -replace '^\s*uses:\s*', '').Trim() })
    $badActions = @($usesValues | Where-Object {
            $_ -match '(?i)deploy|publish|release|gh-pages|^azure/|^aws-actions/|^docker/login'
        })
    Assert-Case 'no step uses a deployment, publish, or cloud-login action' `
        (($usesValues.Count -gt 0) -and ($badActions.Count -eq 0)) `
        "uses: $($usesValues -join ', ')"

    # --- Case 6: every gate-running step delegates to the runner -------------
    $runBlocks = @()
    for ($i = 0; $i -lt $bodyLines.Count; $i++) {
        if ($bodyLines[$i] -match '^\s*run:\s*') {
            $runBlocks += , ((Get-Block -Lines $bodyLines -Index $i) -join "`n")
        }
    }
    $delegating = @($runBlocks | Where-Object { $_ -match 'Invoke-LhmGates\.ps1' })
    Assert-Case 'every run step delegates to eng/ci/Invoke-LhmGates.ps1' `
        (($runBlocks.Count -gt 0) -and ($delegating.Count -eq $runBlocks.Count)) `
        "run blocks: $($runBlocks.Count); delegating: $($delegating.Count)"

    # --- Case 7: no gate command is duplicated inline ------------------------
    $inlinePatterns = @('dotnet build', 'dotnet test', 'node webtests', 'node --test', 'python -m unittest')
    $duplicated = @()
    foreach ($block in $runBlocks) {
        foreach ($pattern in $inlinePatterns) {
            if ($block -like "*$pattern*") { $duplicated += $pattern }
        }
    }
    Assert-Case 'no run step duplicates a gate command inline' `
        ($duplicated.Count -eq 0) `
        "duplicated: $($duplicated -join ', ')"

    # --- Case 8: no run step matches the runner's own deny-list --------------
    $denyJson = Join-Path $temp 'deny.json'
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $engine -NoProfile -ExecutionPolicy Bypass -File $runner -List -JsonSummary $denyJson 2>&1 | Out-Null
    $listExit = $LASTEXITCODE
    $ErrorActionPreference = $previous

    $patterns = @()
    if (($listExit -eq 0) -and (Test-Path -LiteralPath $denyJson)) {
        $patterns = @((Get-Content -LiteralPath $denyJson -Raw | ConvertFrom-Json).denyList)
    }
    $offenders = @()
    foreach ($block in $runBlocks) {
        foreach ($pattern in $patterns) {
            if ($block -match $pattern) { $offenders += $pattern }
        }
    }
    Assert-Case 'no run step matches the gate runner deny-list' `
        (($patterns.Count -gt 0) -and ($offenders.Count -eq 0)) `
        "patterns: $($patterns.Count); offenders: $($offenders -join ', ')"

    # --- Case 9: the job runs on Windows -------------------------------------
    $runsOn = @($bodyLines | Where-Object { $_ -match '^\s*runs-on:\s*windows-latest\s*$' })
    Assert-Case 'the job runs on windows-latest' `
        ($runsOn.Count -ge 1) `
        'every gate in this repository is Windows-only'

    # --- Case 10: concurrency and a job timeout are declared -----------------
    $hasConcurrency = @($bodyLines | Where-Object { $_ -match '^concurrency:\s*$' }).Count -ge 1
    $hasGroup = @($bodyLines | Where-Object { $_ -match '^\s*group:\s*\S' }).Count -ge 1
    $hasTimeout = @($bodyLines | Where-Object { $_ -match '^\s*timeout-minutes:\s*\d+' }).Count -ge 1
    Assert-Case 'a concurrency group and a job timeout are declared' `
        ($hasConcurrency -and $hasGroup -and $hasTimeout) `
        "concurrency: $hasConcurrency; group: $hasGroup; timeout: $hasTimeout"

    # --- Structural confirmation from the parsed model -----------------------
    if ($null -ne $parsed) {
        $permissionNames = @($parsed.permissions.PSObject.Properties.Name)
        $job = $parsed.jobs.gates
        Assert-Case 'the parsed model agrees on permissions, runner, and timeout' `
            (($permissionNames.Count -eq 1) -and ($permissionNames[0] -eq 'contents') -and ($parsed.permissions.contents -eq 'read') -and ($job.'runs-on' -eq 'windows-latest') -and ($job.'timeout-minutes' -gt 0)) `
            "permissions: $($permissionNames -join ','); runs-on: $($job.'runs-on')"
    }
    else {
        Skip-Case 'parsed-model confirmation skipped; no YAML parse available'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:Failures -gt 0) {
    Write-Host "$($script:Failures) assertion(s) failed."
    exit 1
}

exit 0

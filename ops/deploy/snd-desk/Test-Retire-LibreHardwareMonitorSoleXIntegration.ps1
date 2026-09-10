#Requires -Version 5.1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$operator = Join-Path $PSScriptRoot 'Retire-LibreHardwareMonitorSoleXIntegration.ps1'
$common = Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1'
. $common

function Assert-True {
    param([Parameter(Mandatory)][bool] $Condition, [Parameter(Mandatory)][string] $Message)
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-Throws {
    param([Parameter(Mandatory)][scriptblock] $Action, [Parameter(Mandatory)][string] $Pattern)
    try { & $Action }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "Expected '$Pattern', got '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected failure '$Pattern', but the action succeeded."
}

function Write-Utf8Json {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][object] $Value)
    $text = ($Value | ConvertTo-Json -Depth 20) + [Environment]::NewLine
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

function New-Fixture {
    param([Parameter(Mandatory)][string] $Root, [switch] $IncludeShim)

    $soleXData = Join-Path $Root 'data\SoleX'
    $bin = Join-Path $Root 'bin'
    $recovery = Join-Path $Root 'data\LibreHardwareMonitor\solex-retirement'
    $generated = Join-Path $soleXData 'generated'
    $extensions = Join-Path $soleXData 'extensions'
    foreach ($directory in @($generated, $extensions, $bin)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    $catalogPath = Join-Path $generated 'solex.effective.json'
    $manifestPath = Join-Path $generated 'solex.commands.json'
    $receiptPath = Join-Path $soleXData '.solex-extensions.json'
    $shimPath = Join-Path $bin 'librehw-solex.cmd'
    $otherShim = Join-Path $bin 'other.cmd'
    [IO.File]::WriteAllText($otherShim, "@echo other`r`n", [Text.Encoding]::ASCII)
    if ($IncludeShim) {
        [IO.File]::WriteAllText($shimPath, "@echo retired`r`n", [Text.Encoding]::ASCII)
    }

    Write-Utf8Json -Path $catalogPath -Value ([ordered]@{
        targets = @(
            [ordered]@{ key = 'other'; displayName = 'Other' },
            [ordered]@{
                key = 'librehw-solex'
                aliases = @('librehw-ui')
                displayName = 'Libre Hardware Monitor'
            }
        )
    })
    Write-Utf8Json -Path $manifestPath -Value ([ordered]@{
        schemaVersion = 2
        commands = @('other', 'librehw-solex')
        shortcuts = @(
            [ordered]@{ id = 'other'; target = 'other' },
            [ordered]@{ id = 'librehw-solex'; target = 'librehw-ui' }
        )
    })
    $records = @(
        [ordered]@{ path = $catalogPath; sha256 = (Get-LhmFileSha256 -Path $catalogPath).ToUpperInvariant(); role = 'EffectiveCatalog' },
        [ordered]@{ path = $manifestPath; sha256 = (Get-LhmFileSha256 -Path $manifestPath).ToUpperInvariant(); role = 'EffectiveCommandManifest' },
        [ordered]@{ path = $otherShim; sha256 = (Get-LhmFileSha256 -Path $otherShim).ToUpperInvariant(); role = 'Shim' },
        [ordered]@{ path = $shimPath; sha256 = if ($IncludeShim) { (Get-LhmFileSha256 -Path $shimPath).ToUpperInvariant() } else { '5A123454D9122FA8C38B74EA9E5AE0CE2D2A029E0F1DF86B59B4F5729B6B1C4D' }; role = 'Shim' }
    )
    Write-Utf8Json -Path $receiptPath -Value ([ordered]@{
        product = 'SoleX.Sq.Extensions'
        schemaVersion = 1
        dataDirectory = $soleXData
        binDirectory = $bin
        files = $records
    })
    $identityPath = Join-Path $Root 'identity.ps1'
    [IO.File]::WriteAllText(
        $identityPath,
        "[pscustomobject]@{ status='VERIFIED'; machineId='snd-desk'; instanceId='ca96d510-7d87-4cec-8e1a-bd8fc3866903' }`r`n",
        [Text.UTF8Encoding]::new($false))

    [pscustomobject]@{
        Root = $Root
        SoleXData = $soleXData
        Bin = $bin
        Recovery = $recovery
        Catalog = $catalogPath
        Manifest = $manifestPath
        Receipt = $receiptPath
        Shim = $shimPath
        OtherShim = $otherShim
        Identity = $identityPath
    }
}

function Invoke-Operator {
    param(
        [Parameter(Mandatory)][object] $Fixture,
        [Parameter(Mandatory)][string] $Mode,
        [string] $FailurePoint = 'None'
    )
    & $operator `
        -Mode $Mode `
        -SoleXDataRoot $Fixture.SoleXData `
        -SoleXBinRoot $Fixture.Bin `
        -RecoveryRoot $Fixture.Recovery `
        -NonLiveTestMode `
        -TestIdentityVerifierPath $Fixture.Identity `
        -TestFailurePoint $FailurePoint `
        -Confirm:$false
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('librehw-solex-retirement-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $fixture = New-Fixture -Root (Join-Path $testRoot 'success') -IncludeShim
    $before = @{
        Catalog = Get-LhmFileSha256 -Path $fixture.Catalog
        Manifest = Get-LhmFileSha256 -Path $fixture.Manifest
        Receipt = Get-LhmFileSha256 -Path $fixture.Receipt
        Shim = Get-LhmFileSha256 -Path $fixture.Shim
        OtherShim = Get-LhmFileSha256 -Path $fixture.OtherShim
    }
    $plan = Invoke-Operator -Fixture $fixture -Mode Plan
    Assert-True ($plan.Status -ceq 'Planned' -and $plan.MutationRequired) 'Plan did not report retirement work.'
    Assert-True ((Get-LhmFileSha256 -Path $fixture.Catalog) -ceq $before.Catalog) 'Plan changed the catalog.'
    Assert-True ((Get-LhmFileSha256 -Path $fixture.Receipt) -ceq $before.Receipt) 'Plan changed the receipt.'

    $apply = Invoke-Operator -Fixture $fixture -Mode Apply
    Assert-True ($apply.Status -ceq 'Applied' -and $apply.MutationPerformed) 'Apply did not report a mutation.'
    Assert-True (-not (Test-Path -LiteralPath $fixture.Shim)) 'Apply retained the LibreHW SoleX shim.'
    Assert-True ((Get-LhmFileSha256 -Path $fixture.OtherShim) -ceq $before.OtherShim) 'Apply changed an unrelated shim.'
    $catalog = Get-Content -LiteralPath $fixture.Catalog -Raw | ConvertFrom-Json
    $manifest = Get-Content -LiteralPath $fixture.Manifest -Raw | ConvertFrom-Json
    $receipt = Get-Content -LiteralPath $fixture.Receipt -Raw | ConvertFrom-Json
    Assert-True (@($catalog.targets | Where-Object { $_.key -ieq 'librehw-solex' }).Count -eq 0) 'Target remained.'
    Assert-True (@($catalog.targets | Where-Object { $_.key -ceq 'other' }).Count -eq 1) 'Other target changed.'
    Assert-True (@($manifest.commands | Where-Object { $_ -ieq 'librehw-solex' }).Count -eq 0) 'Command remained.'
    Assert-True (@($manifest.shortcuts | Where-Object { $_.id -ieq 'librehw-solex' }).Count -eq 0) 'Shortcut remained.'
    Assert-True (@($receipt.files | Where-Object { $_.path -ieq $fixture.Shim }).Count -eq 0) 'Shim receipt remained.'
    Assert-True (@($receipt.files | Where-Object { $_.path -ieq $fixture.OtherShim }).Count -eq 1) 'Other receipt changed.'
    $validated = Invoke-Operator -Fixture $fixture -Mode Validate
    Assert-True ($validated.Status -ceq 'Validated') 'Validate did not pass.'
    $packetCount = @(Get-ChildItem -LiteralPath $fixture.Recovery -Directory).Count
    $idempotent = Invoke-Operator -Fixture $fixture -Mode Apply
    Assert-True ($idempotent.Status -ceq 'AlreadyRetired' -and -not $idempotent.MutationPerformed) 'Second Apply was not idempotent.'
    Assert-True (@(Get-ChildItem -LiteralPath $fixture.Recovery -Directory).Count -eq $packetCount) 'Idempotent Apply created recovery state.'

    $missingShimFixture = New-Fixture -Root (Join-Path $testRoot 'missing-shim')
    $missingApply = Invoke-Operator -Fixture $missingShimFixture -Mode Apply
    Assert-True ($missingApply.Status -ceq 'Applied') 'Apply did not prune an already-missing shim record.'
    $missingReceipt = Get-Content -LiteralPath $missingShimFixture.Receipt -Raw | ConvertFrom-Json
    Assert-True (@($missingReceipt.files | Where-Object { $_.path -ieq $missingShimFixture.Shim }).Count -eq 0) 'Missing shim record remained.'

    $rollbackFixture = New-Fixture -Root (Join-Path $testRoot 'rollback') -IncludeShim
    $rollbackBefore = @{
        Catalog = Get-LhmFileSha256 -Path $rollbackFixture.Catalog
        Manifest = Get-LhmFileSha256 -Path $rollbackFixture.Manifest
        Receipt = Get-LhmFileSha256 -Path $rollbackFixture.Receipt
        Shim = Get-LhmFileSha256 -Path $rollbackFixture.Shim
    }
    Assert-Throws -Pattern 'Injected retirement failure' -Action {
        Invoke-Operator -Fixture $rollbackFixture -Mode Apply -FailurePoint AfterShimRemoval
    }
    Assert-True ((Get-LhmFileSha256 -Path $rollbackFixture.Catalog) -ceq $rollbackBefore.Catalog) 'Rollback did not restore catalog.'
    Assert-True ((Get-LhmFileSha256 -Path $rollbackFixture.Manifest) -ceq $rollbackBefore.Manifest) 'Rollback did not restore manifest.'
    Assert-True ((Get-LhmFileSha256 -Path $rollbackFixture.Receipt) -ceq $rollbackBefore.Receipt) 'Rollback did not restore receipt.'
    Assert-True ((Get-LhmFileSha256 -Path $rollbackFixture.Shim) -ceq $rollbackBefore.Shim) 'Rollback did not restore shim.'

    $modifiedFixture = New-Fixture -Root (Join-Path $testRoot 'modified') -IncludeShim
    [IO.File]::AppendAllText($modifiedFixture.Shim, 'modified', [Text.Encoding]::ASCII)
    Assert-Throws -Pattern 'modified LibreHW SoleX shim' -Action {
        Invoke-Operator -Fixture $modifiedFixture -Mode Plan
    }

    $identityFixture = New-Fixture -Root (Join-Path $testRoot 'identity') -IncludeShim
    [IO.File]::WriteAllText(
        $identityFixture.Identity,
        "[pscustomobject]@{ status='UNVERIFIED'; machineId='snd-desk'; instanceId='ca96d510-7d87-4cec-8e1a-bd8fc3866903' }`r`n",
        [Text.UTF8Encoding]::new($false))
    $identityHash = Get-LhmFileSha256 -Path $identityFixture.Catalog
    Assert-Throws -Pattern 'identity verification failed' -Action {
        Invoke-Operator -Fixture $identityFixture -Mode Apply
    }
    Assert-True ((Get-LhmFileSha256 -Path $identityFixture.Catalog) -ceq $identityHash) 'Rejected identity changed state.'

    Write-Output 'LibreHW SoleX retirement tests passed: Plan, Apply, Validate, missing-shim pruning, idempotence, rollback, modified-file refusal, and identity refusal.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

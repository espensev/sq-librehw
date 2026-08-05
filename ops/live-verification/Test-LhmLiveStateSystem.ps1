[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-LhmTest {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

$verifier = Join-Path $PSScriptRoot 'Test-LhmLiveState.ps1'
$engine = (Get-Process -Id $PID).Path
$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$testRoot = [System.IO.Path]::GetFullPath((Join-Path $tempBase ('sq-lhm-live-test-' + [guid]::NewGuid().ToString('N'))))
$tempPrefix = $tempBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $testRoot.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use test root outside the system temp directory: $testRoot"
}

function Write-LhmFixtureJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Value)

    [System.IO.File]::WriteAllText($Path,
                                   ($Value | ConvertTo-Json -Depth 6),
                                   [System.Text.UTF8Encoding]::new($false))
}

try {
    $liveDir = Join-Path $testRoot 'deployments\current'
    $logMgmtDir = Join-Path $testRoot 'operations\log-management'
    $manifestDir = Join-Path $testRoot 'manifests'
    $channelDir = Join-Path $testRoot 'manifests\channels'
    foreach ($directory in @($liveDir, $logMgmtDir, $manifestDir, $channelDir)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    [System.IO.File]::WriteAllText((Join-Path $liveDir 'FakeLhm.exe'), 'not a real executable')
    foreach ($tool in 'LhmLogManagement.Common.ps1', 'Archive-LhmLogs.ps1', 'Clean-LhmLogArchives.ps1', 'Invoke-LhmLogManagement.ps1', 'log-management.json') {
        [System.IO.File]::WriteAllText((Join-Path $logMgmtDir $tool), '# fixture')
    }

    $layout = [ordered]@{
        schema = 'sq.librehw-layout'
        version = 1
        machineId = 'live-state-fixture'
        computerName = $env:COMPUTERNAME
        operations = [ordered]@{
            root = $testRoot
            live = $liveDir
            logManagement = $logMgmtDir
        }
    }
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout

    $missingTask = '\SqLhmLiveStateFixture-' + [guid]::NewGuid().ToString('N')
    $consumers = [ordered]@{
        schema = 'sq.librehw-consumers'
        version = 1
        bindings = @(
            [ordered]@{
                kind = 'scheduledTask'
                name = $missingTask
                target = (Join-Path $liveDir 'FakeLhm.exe')
            }
        )
    }
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers

    $liveChannel = [ordered]@{
        schema = 'sq.librehw-live-channel'
        version = 1
        entryPoint = 'FakeLhm.exe'
        sha256 = ('ab' * 32)
    }
    Write-LhmFixtureJson -Path (Join-Path $channelDir 'live.json') -Value $liveChannel

    $before = @(Get-ChildItem -LiteralPath $testRoot -Recurse -File |
        ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" } |
        Sort-Object)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $stdout = & $engine -NoProfile -ExecutionPolicy Bypass -File $verifier -StackRoot $testRoot -CsvGrowthSeconds 0 -Json 2>$null
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    Assert-LhmTest ($exitCode -ne 0) 'verifier should exit non-zero when live-state checks fail'

    $report = ($stdout -join [Environment]::NewLine) | ConvertFrom-Json
    Assert-LhmTest ($report.schema -eq 'sq.lhm.live-state') 'JSON report should carry the live-state schema'
    Assert-LhmTest (-not $report.pass) 'fixture stack should not pass'
    $byCheck = @{}
    $byDetail = @{}
    foreach ($check in $report.checks) {
        $byCheck[$check.Check] = $check.Status
        $byDetail[$check.Check] = [string]$check.Detail
    }
    Assert-LhmTest ($byCheck['manifests'] -eq 'Pass') 'manifests should parse'
    Assert-LhmTest ($byCheck['machine'] -eq 'Pass') 'machine declaration should match'
    Assert-LhmTest ($byCheck['live-root'] -eq 'Pass') 'live root and entry point should be present'
    Assert-LhmTest ($byCheck['process'] -eq 'Fail') 'missing process should fail'
    Assert-LhmTest ($byCheck['entry-point-hash'] -eq 'Fail') 'mismatched entry-point hash should fail'
    Assert-LhmTest ($byDetail['entry-point-hash'] -like '*SHA-256*expected*') 'hash check should fail for a hash reason, not an environment reason'
    Assert-LhmTest (@($report.checks | Where-Object { $_.Detail -like '*is not recognized*' }).Count -eq 0) 'no check may fail because a command is unavailable'
    Assert-LhmTest ($byCheck[('task:' + $missingTask)] -eq 'Fail') 'missing bound task should fail'
    Assert-LhmTest ($byCheck['listener-config'] -eq 'Fail') 'missing listener configuration should fail'
    Assert-LhmTest ($byCheck['endpoints'] -eq 'Fail') 'endpoints should fail without a listener'
    Assert-LhmTest ($byCheck['csv'] -eq 'Fail') 'missing current-day CSV should fail'
    Assert-LhmTest ($byCheck['log-tooling'] -eq 'Pass') 'complete installed tooling should pass'

    $after = @(Get-ChildItem -LiteralPath $testRoot -Recurse -File |
        ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" } |
        Sort-Object)
    Assert-LhmTest (@(Compare-Object -ReferenceObject $before -DifferenceObject $after).Count -eq 0) 'verifier must not write, grow, or touch any fixture file'

    $layout.computerName = 'OTHER-HOST-FIXTURE'
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout
    $ErrorActionPreference = 'Continue'
    $stdout = & $engine -NoProfile -ExecutionPolicy Bypass -File $verifier -StackRoot $testRoot -CsvGrowthSeconds 0 -Json 2>$null
    $ErrorActionPreference = $previousPreference
    Assert-LhmTest ($LASTEXITCODE -ne 0) 'machine mismatch should exit non-zero'
    $report = ($stdout -join [Environment]::NewLine) | ConvertFrom-Json
    $machineCheck = @($report.checks | Where-Object { $_.Check -eq 'machine' })
    Assert-LhmTest ($machineCheck.Count -eq 1 -and $machineCheck[0].Status -eq 'Fail') 'machine mismatch should fail the machine check'

    Write-Output 'PASS: live-state verifier manifest, failure-detection, read-only, and machine-boundary checks'
}
finally {
    if ([System.IO.Directory]::Exists($testRoot)) {
        $resolvedCleanup = [System.IO.Path]::GetFullPath($testRoot)
        if (-not $resolvedCleanup.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean test path outside temp: $resolvedCleanup"
        }
        Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force
    }
}

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

function Invoke-LhmFixtureProcess {
    param(
        [Parameter(Mandatory)][string]$Engine,
        [Parameter(Mandatory)][string]$Verifier,
        [Parameter(Mandatory)][string]$StackRoot
    )

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $stdout = @(& $Engine -NoProfile -ExecutionPolicy Bypass -File $Verifier -StackRoot $StackRoot -CsvGrowthSeconds 0 -Json 2>$null)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Report = (($stdout -join [Environment]::NewLine) | ConvertFrom-Json)
    }
}

function Get-LhmFixtureCheck {
    param(
        [Parameter(Mandatory)]$Report,
        [Parameter(Mandatory)][string]$Name
    )

    return @($Report.checks | Where-Object { $_.Check -eq $Name })
}

function Assert-LhmFixtureFailure {
    param(
        [Parameter(Mandatory)]$Invocation,
        [Parameter(Mandatory)][string]$Check,
        [Parameter(Mandatory)][string]$DetailPattern
    )

    Assert-LhmTest ($Invocation.ExitCode -ne 0) "$Check tamper should exit non-zero"
    $matches = @(Get-LhmFixtureCheck -Report $Invocation.Report -Name $Check)
    Assert-LhmTest ($matches.Count -eq 1 -and $matches[0].Status -eq 'Fail') "$Check tamper should emit one failed check"
    Assert-LhmTest ([string]$matches[0].Detail -like $DetailPattern) "$Check tamper should report detail matching '$DetailPattern'"
}

function Invoke-LhmFixtureInProcess {
    param(
        [Parameter(Mandatory)][string]$Verifier,
        [Parameter(Mandatory)][string]$StackRoot
    )

    $stdout = @(& {
        try {
            & $Verifier -StackRoot $StackRoot -CsvGrowthSeconds 0 -Json
        }
        catch {
            # The JSON report is emitted before the expected aggregate failure.
        }
    })
    return (($stdout -join [Environment]::NewLine) | ConvertFrom-Json)
}

try {
    $liveDir = Join-Path $testRoot 'deployments\current'
    $logMgmtDir = Join-Path $testRoot 'operations\log-management'
    $archiveDir = Join-Path $testRoot 'data\logs\archive'
    $manifestDir = Join-Path $testRoot 'manifests'
    $channelDir = Join-Path $testRoot 'manifests\channels'
    foreach ($directory in @($liveDir, $logMgmtDir, $archiveDir, $manifestDir, $channelDir)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    [System.IO.File]::WriteAllText((Join-Path $liveDir 'FakeLhm.exe'), 'not a real executable')
    $fakePowerShellExecutable = Join-Path $testRoot 'powershell.exe'
    [System.IO.File]::WriteAllText($fakePowerShellExecutable, 'not a real PowerShell executable')
    foreach ($tool in 'LhmLogManagement.Common.ps1', 'Archive-LhmLogs.ps1', 'Clean-LhmLogArchives.ps1', 'Invoke-LhmLogManagement.ps1') {
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
            logArchive = $archiveDir
        }
    }
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout

    $missingTask = '\SqLhmLiveStateFixture-' + [guid]::NewGuid().ToString('N')
    $missingLogTask = '\SqLhmLogStateFixture-' + [guid]::NewGuid().ToString('N')
    $consumers = [ordered]@{
        schema = 'sq.librehw-consumers'
        version = 1
        machineId = 'live-state-fixture'
        bindings = @(
            [ordered]@{
                kind = 'scheduledTask'
                name = $missingTask
                target = (Join-Path $liveDir 'FakeLhm.exe')
            },
            [ordered]@{
                kind = 'scheduledTask'
                name = $missingLogTask
                target = (Join-Path $logMgmtDir 'Invoke-LhmLogManagement.ps1')
            }
        )
    }
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers

    $liveChannel = [ordered]@{
        schema = 'sq.librehw-live-channel'
        version = 1
        machineId = 'live-state-fixture'
        deploymentRoot = $liveDir
        entryPoint = 'FakeLhm.exe'
        sha256 = ('ab' * 32)
    }
    Write-LhmFixtureJson -Path (Join-Path $channelDir 'live.json') -Value $liveChannel

    $logConfig = [ordered]@{
        Schema = 'sq.lhm-log-management'
        Version = 1
        SourceDirectories = @($liveDir)
        ArchiveRoot = $archiveDir
        MachineName = $env:COMPUTERNAME
        RetentionDays = 365
    }
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig

    $before = @(Get-ChildItem -LiteralPath $testRoot -Recurse -File |
        ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" } |
        Sort-Object)

    $invocation = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmTest ($invocation.ExitCode -ne 0) 'verifier should exit non-zero when live-state checks fail'

    $report = $invocation.Report
    Assert-LhmTest ($report.schema -eq 'sq.lhm.live-state') 'JSON report should carry the live-state schema'
    Assert-LhmTest (-not $report.pass) 'fixture stack should not pass'
    Assert-LhmTest (@($report.checks).Count -eq 11) 'fixture should preserve the eleven-check live report shape'
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
    Assert-LhmTest ($byCheck[('task:' + $missingLogTask)] -eq 'Fail') 'missing bound log task should fail'
    Assert-LhmTest ($byCheck['listener-config'] -eq 'Fail') 'missing listener configuration should fail'
    Assert-LhmTest ($byCheck['endpoints'] -eq 'Fail') 'endpoints should fail without a listener'
    Assert-LhmTest ($byCheck['csv'] -eq 'Fail') 'missing current-day CSV should fail'
    Assert-LhmTest ($byCheck['log-tooling'] -eq 'Pass') 'complete installed tooling should pass'

    $after = @(Get-ChildItem -LiteralPath $testRoot -Recurse -File |
        ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" } |
        Sort-Object)
    Assert-LhmTest (@(Compare-Object -ReferenceObject $before -DifferenceObject $after).Count -eq 0) 'verifier must not write, grow, or touch any fixture file'

    $layout.schema = 'sq.librehw-layout.invalid'
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'manifests' -DetailPattern '*schema*expected*'
    $layout.schema = 'sq.librehw-layout'

    $consumers.version = 2
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'manifests' -DetailPattern '*version*expected*'
    $consumers.version = 1

    $liveChannel.machineId = 'other-fixture'
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers
    Write-LhmFixtureJson -Path (Join-Path $channelDir 'live.json') -Value $liveChannel
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'manifests' -DetailPattern '*machineId*do not agree*'
    $liveChannel.machineId = 'live-state-fixture'

    $layout.operations.root = Join-Path $testRoot 'other-stack'
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout
    Write-LhmFixtureJson -Path (Join-Path $channelDir 'live.json') -Value $liveChannel
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'manifests' -DetailPattern '*operations.root*does not match StackRoot*'
    $layout.operations.root = $testRoot

    $liveChannel.deploymentRoot = Join-Path $testRoot 'other-live'
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout
    Write-LhmFixtureJson -Path (Join-Path $channelDir 'live.json') -Value $liveChannel
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'manifests' -DetailPattern '*deploymentRoot*does not match*'
    $liveChannel.deploymentRoot = $liveDir
    Write-LhmFixtureJson -Path (Join-Path $channelDir 'live.json') -Value $liveChannel

    $consumers.bindings[0].target = Join-Path $testRoot 'other-live\FakeLhm.exe'
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'manifests' -DetailPattern '*scheduledTask bindings*declared live target*'
    $consumers.bindings[0].target = Join-Path $liveDir 'FakeLhm.exe'

    $expectedConsumerBindings = @($consumers.bindings)
    $consumers.bindings = @()
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'manifests' -DetailPattern '*scheduledTask bindings*found 0 binding*'

    $consumers.bindings = @($expectedConsumerBindings[0])
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'manifests' -DetailPattern '*scheduledTask bindings*found 1 binding*'
    $consumers.bindings = $expectedConsumerBindings
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers

    $layout.computerName = 'OTHER-HOST-FIXTURE'
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'machine' -DetailPattern '*OTHER-HOST-FIXTURE*'
    $layout.computerName = $env:COMPUTERNAME
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'layout.json') -Value $layout

    $logConfig.Schema = 'sq.lhm-log-management.invalid'
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'log-tooling' -DetailPattern '*unsupported schema or version*'
    $logConfig.Schema = 'sq.lhm-log-management'

    $logConfig.Version = 2
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'log-tooling' -DetailPattern '*unsupported schema or version*'
    $logConfig.Version = 1

    $logConfig.SourceDirectories = @((Join-Path $testRoot 'other-source'))
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'log-tooling' -DetailPattern '*SourceDirectories*'
    $logConfig.SourceDirectories = @($liveDir)

    $logConfig.ArchiveRoot = Join-Path $testRoot 'other-archive'
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'log-tooling' -DetailPattern '*ArchiveRoot*'
    $logConfig.ArchiveRoot = $archiveDir

    $logConfig.MachineName = 'OTHER-HOST-FIXTURE'
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig
    $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
    Assert-LhmFixtureFailure -Invocation $tamper -Check 'log-tooling' -DetailPattern '*MachineName*'
    $logConfig.MachineName = $env:COMPUTERNAME

    foreach ($invalidRetention in @(0, 36501)) {
        $logConfig.RetentionDays = $invalidRetention
        Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig
        $tamper = Invoke-LhmFixtureProcess -Engine $engine -Verifier $verifier -StackRoot $testRoot
        Assert-LhmFixtureFailure -Invocation $tamper -Check 'log-tooling' -DetailPattern '*RetentionDays*'
    }
    $logConfig.RetentionDays = 365
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig

    $liveTaskName = '\SqLhmExactLiveFixture'
    $logTaskName = '\SqLhmExactLogFixture'
    $liveTarget = Join-Path $liveDir 'FakeLhm.exe'
    $logTarget = Join-Path $logMgmtDir 'Invoke-LhmLogManagement.ps1'
    $logConfigPath = Join-Path $logMgmtDir 'log-management.json'
    $consumers.bindings = @(
        [ordered]@{ kind = 'scheduledTask'; name = $liveTaskName; target = $liveTarget },
        [ordered]@{ kind = 'scheduledTask'; name = $logTaskName; target = $logTarget }
    )
    Write-LhmFixtureJson -Path (Join-Path $manifestDir 'consumers.json') -Value $consumers

    $liveAction = [pscustomobject]@{
        Execute = $liveTarget
        Arguments = ''
        WorkingDirectory = $liveDir
    }
    $logAction = [pscustomobject]@{
        Execute = (Get-Command powershell.exe -ErrorAction Stop).Source
        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$logTarget`" -ConfigPath `"$logConfigPath`""
        WorkingDirectory = $logMgmtDir
    }
    $global:LhmLiveFixtureTasks = @{
        $liveTaskName = [pscustomobject]@{ State = 'Running'; Actions = @($liveAction) }
        $logTaskName = [pscustomobject]@{ State = 'Ready'; Actions = @($logAction) }
    }

    function Get-ScheduledTask {
        [CmdletBinding()]
        param([string]$TaskPath, [string]$TaskName)

        $fullName = $TaskPath + $TaskName
        if (-not $global:LhmLiveFixtureTasks.ContainsKey($fullName)) {
            throw "Fixture scheduled task does not exist: $fullName"
        }
        return $global:LhmLiveFixtureTasks[$fullName]
    }

    function Get-ScheduledTaskInfo {
        [CmdletBinding()]
        param([string]$TaskPath, [string]$TaskName)

        return [pscustomobject]@{
            LastRunTime = (Get-Date).AddHours(-1)
            LastTaskResult = 0
        }
    }

    $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
    foreach ($taskCheckName in @(('task:' + $liveTaskName), ('task:' + $logTaskName))) {
        $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name $taskCheckName)
        $observedTaskDetail = @($taskCheck | ForEach-Object { "$($_.Status): $($_.Detail)" }) -join '; '
        Assert-LhmTest ($taskCheck.Count -eq 1 -and $taskCheck[0].Status -eq 'Pass') "$taskCheckName exact action should pass; observed '$observedTaskDetail'"
    }

    $wrongEngineAction = [pscustomobject]@{
        Execute = $fakePowerShellExecutable
        Arguments = $logAction.Arguments
        WorkingDirectory = $logMgmtDir
    }
    $previousProcessPath = $env:PATH
    try {
        $env:PATH = $testRoot + [System.IO.Path]::PathSeparator + $previousProcessPath
        $discoverableFake = @(Get-Command powershell.exe -CommandType Application -All -ErrorAction Stop | Where-Object {
            $_.Path -and $_.Path -ieq $fakePowerShellExecutable
        })
        Assert-LhmTest ($discoverableFake.Count -eq 1) 'fake same-basename engine should be discoverable from the prepended fixture PATH'
        $global:LhmLiveFixtureTasks[$logTaskName].Actions = @($wrongEngineAction)
        $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
        $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name ('task:' + $logTaskName))
        Assert-LhmTest ($taskCheck[0].Status -eq 'Fail' -and $taskCheck[0].Detail -like '*canonical legacy PowerShell engine paths*') 'caller-PATH same-basename script engine should fail the legacy fallback'
    }
    finally {
        $env:PATH = $previousProcessPath
    }
    $global:LhmLiveFixtureTasks[$logTaskName].Actions = @($logAction)

    $logConfig['PowerShellExecutable'] = $logAction.Execute
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig
    $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
    $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name ('task:' + $logTaskName))
    Assert-LhmTest ($taskCheck[0].Status -eq 'Pass') 'task engine should pass when it exactly matches configured PowerShellExecutable'

    $logConfig['PowerShellExecutable'] = $fakePowerShellExecutable
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig
    $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
    $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name ('task:' + $logTaskName))
    Assert-LhmTest ($taskCheck[0].Status -eq 'Fail' -and $taskCheck[0].Detail -like '*configured PowerShellExecutable*') 'configured PowerShellExecutable should replace the legacy fallback exactly'
    $logConfig.Remove('PowerShellExecutable')
    Write-LhmFixtureJson -Path (Join-Path $logMgmtDir 'log-management.json') -Value $logConfig

    $global:LhmLiveFixtureTasks[$liveTaskName].Actions = @($liveAction, $liveAction)
    $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
    $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name ('task:' + $liveTaskName))
    Assert-LhmTest ($taskCheck[0].Status -eq 'Fail' -and $taskCheck[0].Detail -like '*exactly one action*') 'multiple task actions should fail'
    $global:LhmLiveFixtureTasks[$liveTaskName].Actions = @($liveAction)

    $wrongWorkingAction = [pscustomobject]@{
        Execute = $liveTarget
        Arguments = ''
        WorkingDirectory = $testRoot
    }
    $global:LhmLiveFixtureTasks[$liveTaskName].Actions = @($wrongWorkingAction)
    $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
    $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name ('task:' + $liveTaskName))
    Assert-LhmTest ($taskCheck[0].Status -eq 'Fail' -and $taskCheck[0].Detail -like '*working directory*') 'wrong task working directory should fail'
    $global:LhmLiveFixtureTasks[$liveTaskName].Actions = @($liveAction)

    $undeclaredArgumentsAction = [pscustomobject]@{
        Execute = $liveTarget
        Arguments = '--unexpected'
        WorkingDirectory = $liveDir
    }
    $global:LhmLiveFixtureTasks[$liveTaskName].Actions = @($undeclaredArgumentsAction)
    $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
    $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name ('task:' + $liveTaskName))
    Assert-LhmTest ($taskCheck[0].Status -eq 'Fail' -and $taskCheck[0].Detail -like '*undeclared arguments*') 'direct executable task arguments should fail'
    $global:LhmLiveFixtureTasks[$liveTaskName].Actions = @($liveAction)

    $substringAction = [pscustomobject]@{
        Execute = $logAction.Execute
        Arguments = "-File `"$logTarget.bak`" -ConfigPath `"$logConfigPath`" -Note `"$logTarget`""
        WorkingDirectory = $logMgmtDir
    }
    $global:LhmLiveFixtureTasks[$logTaskName].Actions = @($substringAction)
    $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
    $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name ('task:' + $logTaskName))
    Assert-LhmTest ($taskCheck[0].Status -eq 'Fail' -and $taskCheck[0].Detail -like '*-File path*does not match*') 'target substring should not satisfy the exact -File path'

    $wrongConfigAction = [pscustomobject]@{
        Execute = $logAction.Execute
        Arguments = "-File `"$logTarget`" -ConfigPath `"$(Join-Path $testRoot 'other-config.json')`""
        WorkingDirectory = $logMgmtDir
    }
    $global:LhmLiveFixtureTasks[$logTaskName].Actions = @($wrongConfigAction)
    $report = Invoke-LhmFixtureInProcess -Verifier $verifier -StackRoot $testRoot
    $taskCheck = @(Get-LhmFixtureCheck -Report $report -Name ('task:' + $logTaskName))
    Assert-LhmTest ($taskCheck[0].Status -eq 'Fail' -and $taskCheck[0].Detail -like '*-ConfigPath*does not match*') 'wrong log configuration argument should fail'

    Remove-Item -LiteralPath 'Function:\Get-ScheduledTask' -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath 'Function:\Get-ScheduledTaskInfo' -ErrorAction SilentlyContinue
    Remove-Variable -Name LhmLiveFixtureTasks -Scope Global -ErrorAction SilentlyContinue

    Write-Output 'PASS: live-state verifier manifest contracts, exact task bindings, log configuration, failure detection, read-only behavior, and machine boundary checks'
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

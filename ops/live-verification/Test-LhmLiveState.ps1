[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$StackRoot,
    [ValidateRange(1, 60)][int]$EndpointTimeoutSeconds = 10,
    [ValidateRange(0, 60)][int]$CsvGrowthSeconds = 5,
    [string]$ExpectedSha256,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# An invoking PowerShell session may have cached the same stale zone as the collector.
[System.TimeZoneInfo]::ClearCachedData()
. (Join-Path $PSScriptRoot 'LhmLiveManifest.Common.ps1')

$stackRootPath = [System.IO.Path]::GetFullPath($StackRoot)
$checks = New-Object 'System.Collections.Generic.List[object]'

function Add-LhmLiveCheck {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet('Pass', 'Fail', 'Info')][string]$Status,
        [Parameter(Mandatory)][string]$Detail
    )

    $checks.Add([pscustomobject]@{ Check = $Name; Status = $Status; Detail = $Detail })
}

function Get-LhmTaskArgumentPath {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Arguments,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Name
    )

    $escapedName = [System.Text.RegularExpressions.Regex]::Escape($Name)
    $pattern = '(?:^|\s)-' + $escapedName + '(?:\s+|:)(?:"(?<quoted>[^"]+)"|(?<bare>\S+))(?=\s|$)'
    $argumentMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $Arguments,
        $pattern,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($argumentMatches.Count -ne 1) {
        throw "Task action must contain exactly one -$Name path argument; found $($argumentMatches.Count)."
    }

    $value = [string]$argumentMatches[0].Groups['quoted'].Value
    if ([string]::IsNullOrEmpty($value)) {
        $value = [string]$argumentMatches[0].Groups['bare'].Value
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Task action -$Name path argument is empty."
    }

    return (Get-LhmExpandedPath -Path $value)
}

function Get-LhmLegacyScriptEnginePaths {
    $paths = @()
    $machineWindowsPowerShell = Join-Path ([System.Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    if ([System.IO.File]::Exists($machineWindowsPowerShell)) {
        $paths += Get-LhmNormalizedPath -Path $machineWindowsPowerShell
    }

    $verifierEngine = [string](Get-Process -Id $PID -ErrorAction Stop).Path
    $verifierEngineName = [System.IO.Path]::GetFileName($verifierEngine)
    if ($verifierEngineName -in @('powershell.exe', 'pwsh.exe') -and
        [System.IO.Path]::IsPathRooted($verifierEngine) -and
        [System.IO.File]::Exists($verifierEngine)) {
        $paths += Get-LhmNormalizedPath -Path $verifierEngine
    }

    return @($paths | Sort-Object -Unique)
}

function Test-LhmReparsePoint {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force
    return (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Get-LhmCsvClockDetail {
    param([Parameter(Mandatory)][string]$Path)

    # Read a bounded tail with sharing so the live writer is never blocked.
    # A concurrent or partial append can give an older row a newer write time.
    $retryDetail = 'CSV clock check is inconclusive; retry verification.'
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        if ($attempt -gt 0) { Start-Sleep -Milliseconds 100 }
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $before = Get-Item -LiteralPath $Path
            $length = $before.Length
            $lastWriteUtc = $before.LastWriteTimeUtc
            $count = [int][Math]::Min(65536, $length)
            $null = $stream.Seek($length - $count, [System.IO.SeekOrigin]::Begin)
            $buffer = New-Object byte[] $count
            $read = 0
            while ($read -lt $count) {
                $next = $stream.Read($buffer, $read, $count - $read)
                if ($next -eq 0) { break }
                $read += $next
            }
            $after = Get-Item -LiteralPath $Path
            $stable = $read -eq $count -and $after.Length -eq $length -and
                $after.LastWriteTimeUtc -eq $lastWriteUtc
            $tail = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
        }
        finally {
            $stream.Dispose()
        }
        if (-not $stable) {
            $retryDetail = 'CSV changed during snapshot reads; clock check is inconclusive after bounded retries.'
            continue
        }

        $lastNewline = $tail.LastIndexOf("`n")
        if ($lastNewline -lt 0) { throw 'CSV has no completed row timestamp.' }
        $completed = $tail.Substring(0, $lastNewline)
        if ($completed.EndsWith("`r")) { $completed = $completed.Substring(0, $completed.Length - 1) }
        $lastRow = $completed.Substring($completed.LastIndexOf("`n") + 1)
        $timestampText = ($lastRow -split ',', 2)[0]
        $timestamp = [datetime]::MinValue
        $formats = [string[]]@('MM/dd/yyyy HH:mm:ss.fff', 'MM/dd/yyyy HH:mm:ss')
        if (-not [datetime]::TryParseExact($timestampText, $formats,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None, [ref]$timestamp)) {
            throw 'Latest completed CSV row has an invalid or missing timestamp.'
        }
        if ($lastNewline -ne $tail.Length - 1) {
            $retryDetail = 'CSV has an unfinished final row; clock check is inconclusive after bounded retries.'
            continue
        }

        # Compare with this snapshot's write time, not polling time: long logging
        # intervals stay valid. Retry disagreement to allow writer metadata to settle.
        $lastWrite = $lastWriteUtc.ToLocalTime()
        $skewSeconds = [Math]::Abs(($lastWrite - $timestamp).TotalSeconds)
        if ($skewSeconds -gt 120) {
            $retryDetail = "CSV timestamp {0} differs from host-local last write {1} by {2:N1} seconds (limit 120); check the collector time zone." -f
                $timestamp.ToString('o'), $lastWrite.ToString('o'), $skewSeconds
            continue
        }
        return ("CSV timestamp agrees with host-local last write (difference {0:N1} seconds)." -f $skewSeconds)
    }
    throw $retryDetail
}

$layout = $null
$consumers = $null
$live = $null
$manifestsValid = $false
$declaredLiveRootContract = $null
$declaredEntryPointContract = $null
$declaredEntryTargetContract = $null
$declaredLogManagementRootContract = $null
$declaredLogTaskTargetContract = $null
$declaredTaskBindingsContract = @()
try {
    $manifestContract = Get-LhmLiveManifestContract -StackRoot $stackRootPath
    $layout = $manifestContract.Layout
    $consumers = $manifestContract.Consumers
    $live = $manifestContract.Live
    $declaredLiveRootContract = $manifestContract.LiveRoot
    $declaredEntryPointContract = $manifestContract.EntryPoint
    $declaredEntryTargetContract = $manifestContract.EntryTarget
    $declaredLogManagementRootContract = $manifestContract.LogManagementRoot
    $declaredLogTaskTargetContract = $manifestContract.LogTaskTarget
    $declaredTaskBindingsContract = @($manifestContract.ScheduledTaskBindings)

    $manifestsValid = $true
    Add-LhmLiveCheck 'manifests' 'Pass' 'All three manifests parsed; schema, version, machineId, roots, and the required live and log-management scheduled-task target contracts agree.'
}
catch {
    Add-LhmLiveCheck 'manifests' 'Fail' $_.Exception.Message
}

$logConfigPathContract = $null
$logConfigContract = $null
$logConfigContractError = $null
$configuredPowerShellExecutablePresent = $false
$configuredPowerShellExecutableContract = $null
if ($manifestsValid) {
    try {
        $logConfigPathContract = Join-Path (Get-LhmNormalizedPath -Path $declaredLogManagementRootContract) 'log-management.json'
        if (-not [System.IO.File]::Exists($logConfigPathContract)) {
            throw "Installed log-management configuration does not exist: $logConfigPathContract"
        }
        $logConfigContract = Get-Content -LiteralPath $logConfigPathContract -Raw | ConvertFrom-Json
        $powerShellExecutableInfo = $logConfigContract.PSObject.Properties['PowerShellExecutable']
        if ($null -ne $powerShellExecutableInfo) {
            $configuredPowerShellExecutablePresent = $true
            $configuredPowerShellExecutable = [string]$powerShellExecutableInfo.Value
            if ([string]::IsNullOrWhiteSpace($configuredPowerShellExecutable) -or
                -not [System.IO.Path]::IsPathRooted($configuredPowerShellExecutable)) {
                throw 'Installed log-management PowerShellExecutable must be a full non-empty path when present.'
            }
            $configuredPowerShellExecutableContract = Get-LhmNormalizedPath -Path $configuredPowerShellExecutable
        }
    }
    catch {
        $logConfigContractError = $_.Exception.Message
    }
}

if ($manifestsValid) {
    try {
        $declaredComputer = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('computerName'))
        if ($declaredComputer -ieq $env:COMPUTERNAME) {
            Add-LhmLiveCheck 'machine' 'Pass' "Declared computer '$declaredComputer' matches the running machine."
        }
        else {
            Add-LhmLiveCheck 'machine' 'Fail' "Manifest declares '$declaredComputer' but this machine is '$env:COMPUTERNAME'."
        }
    }
    catch {
        Add-LhmLiveCheck 'machine' 'Fail' $_.Exception.Message
    }
}
else {
    Add-LhmLiveCheck 'machine' 'Fail' 'Prerequisite manifests check failed.'
}

$liveRoot = $null
$entryPoint = $null
$declaredEntryPath = $null
$livePath = $null
if ($manifestsValid) {
    try {
        $liveRoot = Get-LhmNormalizedPath -Path $declaredLiveRootContract
        $entryPoint = $declaredEntryPointContract
        $declaredEntryPath = $declaredEntryTargetContract
        if (-not [System.IO.Directory]::Exists($liveRoot)) {
            Add-LhmLiveCheck 'live-root' 'Fail' "Declared live root does not exist: $liveRoot"
        }
        elseif (Test-LhmReparsePoint -Path $liveRoot) {
            Add-LhmLiveCheck 'live-root' 'Fail' "Declared live root is a reparse point: $liveRoot"
        }
        elseif ([System.IO.File]::Exists($declaredEntryPath)) {
            $livePath = $declaredEntryPath
            Add-LhmLiveCheck 'live-root' 'Pass' "Live root and entry point present: $livePath"
        }
        else {
            Add-LhmLiveCheck 'live-root' 'Fail' "Entry point is missing: $declaredEntryPath"
        }
    }
    catch {
        Add-LhmLiveCheck 'live-root' 'Fail' $_.Exception.Message
    }
}
else {
    Add-LhmLiveCheck 'live-root' 'Fail' 'Prerequisite manifests check failed.'
}

if ($null -ne $livePath) {
    try {
        $processName = [System.IO.Path]::GetFileNameWithoutExtension($entryPoint)
        $processes = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
        $matching = @($processes | Where-Object { $_.Path -and ($_.Path -ieq $livePath) })
        $readablePaths = @($processes | Where-Object { $_.Path })
        if ($matching.Count -eq 1 -and $processes.Count -eq 1) {
            Add-LhmLiveCheck 'process' 'Pass' "PID $($matching[0].Id) runs the exact live entry point (started $($matching[0].StartTime.ToString('o')))."
        }
        elseif ($processes.Count -gt 0 -and $readablePaths.Count -eq 0) {
            Add-LhmLiveCheck 'process' 'Fail' "Found $($processes.Count) process(es) named '$processName' but their executable paths are unreadable from this session; run the verifier elevated to verify the exact path."
        }
        else {
            Add-LhmLiveCheck 'process' 'Fail' "Expected exactly one machine-wide process named '$processName', running $livePath; found $($matching.Count) matching of $($processes.Count)."
        }
    }
    catch {
        Add-LhmLiveCheck 'process' 'Fail' $_.Exception.Message
    }
}
else {
    Add-LhmLiveCheck 'process' 'Fail' 'Prerequisite live-root check failed.'
}

if ($null -ne $livePath) {
    try {
        $expectedHash = $ExpectedSha256
        if ([string]::IsNullOrWhiteSpace($expectedHash)) {
            $expectedHash = [string](Get-LhmManifestValue -Object $live -PropertyPath @('sha256'))
        }
        $actualHash = (Get-FileHash -LiteralPath $livePath -Algorithm SHA256).Hash
        if ($actualHash -ieq $expectedHash) {
            Add-LhmLiveCheck 'entry-point-hash' 'Pass' "Entry-point SHA-256 matches the expected value ($($actualHash.ToLowerInvariant()))."
        }
        else {
            Add-LhmLiveCheck 'entry-point-hash' 'Fail' "Entry-point SHA-256 is $($actualHash.ToLowerInvariant()); expected $($expectedHash.ToLowerInvariant())."
        }
    }
    catch {
        Add-LhmLiveCheck 'entry-point-hash' 'Fail' $_.Exception.Message
    }
}
else {
    Add-LhmLiveCheck 'entry-point-hash' 'Fail' 'Prerequisite live-root check failed.'
}

if ($manifestsValid) {
    $taskBindings = @($declaredTaskBindingsContract)

    foreach ($binding in $taskBindings) {
        $bindingName = [string]$binding.name
        try {
            $lastSlash = $bindingName.LastIndexOf('\')
            $taskPath = $bindingName.Substring(0, $lastSlash + 1)
            $taskName = $bindingName.Substring($lastSlash + 1)
            if ([string]::IsNullOrEmpty($taskPath)) {
                $taskPath = '\'
            }

            $task = Get-ScheduledTask -TaskPath $taskPath -TaskName $taskName -ErrorAction Stop
            $actions = @($task.Actions)
            if ($actions.Count -ne 1) {
                Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task must contain exactly one action; found $($actions.Count)."
                continue
            }

            $action = $actions[0]
            $target = Get-LhmNormalizedPath -Path ([string]$binding.target)
            $expectedWorkingDirectory = Get-LhmNormalizedPath -Path (Split-Path -Parent $target)
            $literalWorkingDirectory = [string]$action.WorkingDirectory
            $rawWorkingDirectoryProperty = $binding.PSObject.Properties['rawWorkingDirectory']
            $actualWorkingDirectory = Get-LhmExpandedPath -Path $literalWorkingDirectory
            if ($null -ne $rawWorkingDirectoryProperty) {
                $declaredRawWorkingDirectory = [string]$rawWorkingDirectoryProperty.Value
                if ($literalWorkingDirectory -ne $declaredRawWorkingDirectory) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task working directory '$literalWorkingDirectory' does not match declared rawWorkingDirectory '$declaredRawWorkingDirectory'."
                    continue
                }
                if (-not [string]::IsNullOrWhiteSpace($literalWorkingDirectory) -and
                    -not (Test-LhmPathEqual -Left $actualWorkingDirectory -Right $expectedWorkingDirectory)) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task working directory '$actualWorkingDirectory' does not match '$expectedWorkingDirectory'."
                    continue
                }
            }
            elseif ([string]::IsNullOrWhiteSpace($actualWorkingDirectory) -or
                    -not (Test-LhmPathEqual -Left $actualWorkingDirectory -Right $expectedWorkingDirectory)) {
                Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task working directory '$actualWorkingDirectory' does not match '$expectedWorkingDirectory'."
                continue
            }

            $actionArguments = ''
            if ($null -ne $action.PSObject.Properties['Arguments'] -and $action.Arguments) {
                $actionArguments = [string]$action.Arguments
            }
            $literalExecute = ([string]$action.Execute).Trim('"')
            $executePath = Get-LhmExpandedPath -Path $literalExecute
            $rawExecuteProperty = $binding.PSObject.Properties['rawExecute']
            $declaredRawExecute = $null
            if ($null -ne $rawExecuteProperty) {
                $declaredRawExecute = [string]$rawExecuteProperty.Value
                if ($literalExecute -ne $declaredRawExecute) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task execute path '$literalExecute' does not match declared rawExecute '$declaredRawExecute'."
                    continue
                }
            }
            $rawAllowsUnrootedEngine = ($null -ne $declaredRawExecute -and
                -not [System.IO.Path]::IsPathRooted($declaredRawExecute))
            if ([System.IO.Path]::GetExtension($target) -ieq '.ps1') {
                $engineName = [System.IO.Path]::GetFileName($executePath)
                if ($engineName -notin @('powershell.exe', 'pwsh.exe')) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Script task execute path '$executePath' is not powershell.exe or pwsh.exe."
                    continue
                }

                if (-not [System.IO.Path]::IsPathRooted($executePath) -and -not $rawAllowsUnrootedEngine) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Script task execute path '$executePath' is not a full application path."
                    continue
                }
                if ([System.IO.Path]::IsPathRooted($executePath)) {
                    $normalizedExecutePath = Get-LhmNormalizedPath -Path $executePath
                    if ($null -ne $logConfigContractError) {
                        Add-LhmLiveCheck "task:$bindingName" 'Fail' $logConfigContractError
                        continue
                    }
                    $allowedEnginePaths = if ($configuredPowerShellExecutablePresent) {
                        @($configuredPowerShellExecutableContract)
                    }
                    else {
                        @(Get-LhmLegacyScriptEnginePaths)
                    }
                    $allowedEngineMatch = @($allowedEnginePaths | Where-Object {
                        Test-LhmPathEqual -Left $_ -Right $normalizedExecutePath
                    }).Count -eq 1
                    if (-not $allowedEngineMatch) {
                        $engineContract = if ($configuredPowerShellExecutablePresent) {
                            "configured PowerShellExecutable '$configuredPowerShellExecutableContract'"
                        }
                        else {
                            'the canonical legacy PowerShell engine paths'
                        }
                        Add-LhmLiveCheck "task:$bindingName" 'Fail' "Script task execute path '$normalizedExecutePath' does not match $engineContract."
                        continue
                    }
                }

                $fileArgument = Get-LhmTaskArgumentPath -Arguments $actionArguments -Name 'File'
                if (-not (Test-LhmPathEqual -Left $fileArgument -Right $target)) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task -File path '$fileArgument' does not match the bound target '$target'."
                    continue
                }

                if ([System.IO.Path]::GetFileName($target) -ieq 'Invoke-LhmLogManagement.ps1') {
                    $expectedConfigPath = Join-Path $expectedWorkingDirectory 'log-management.json'
                    $configArgument = Get-LhmTaskArgumentPath -Arguments $actionArguments -Name 'ConfigPath'
                    if (-not (Test-LhmPathEqual -Left $configArgument -Right $expectedConfigPath)) {
                        Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task -ConfigPath '$configArgument' does not match '$expectedConfigPath'."
                        continue
                    }
                }
            }
            else {
                if (-not (Test-LhmPathEqual -Left $executePath -Right $target)) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task execute path '$executePath' does not match the bound target '$target'."
                    continue
                }
                if (-not [string]::IsNullOrWhiteSpace($actionArguments)) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' 'Direct executable task has undeclared arguments.'
                    continue
                }
            }

            if ($null -ne $declaredEntryPath -and (Test-LhmPathEqual -Left $target -Right $declaredEntryPath)) {
                if ([string]$task.State -eq 'Running') {
                    Add-LhmLiveCheck "task:$bindingName" 'Pass' 'Entry-point task is Running with one exact bound action and working directory.'
                }
                else {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Entry-point task state is $($task.State); expected Running."
                }
            }
            elseif ([string]$task.State -eq 'Disabled') {
                Add-LhmLiveCheck "task:$bindingName" 'Fail' 'Task is disabled.'
            }
            else {
                $info = Get-ScheduledTaskInfo -TaskPath $taskPath -TaskName $taskName -ErrorAction Stop
                $freshEnough = $info.LastRunTime -gt (Get-Date).AddHours(-26)
                $healthyResult = @(0, 267009) -contains [int64]$info.LastTaskResult
                if ($freshEnough -and $healthyResult) {
                    Add-LhmLiveCheck "task:$bindingName" 'Pass' "One exact bound action and working directory; last run $($info.LastRunTime.ToString('o')) with result $($info.LastTaskResult)."
                }
                else {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Last run $($info.LastRunTime) with result $($info.LastTaskResult); expected result 0/267009 within 26 hours."
                }
            }
        }
        catch {
            Add-LhmLiveCheck "task:$bindingName" 'Fail' $_.Exception.Message
        }
    }
}
else {
    Add-LhmLiveCheck 'tasks' 'Fail' 'Prerequisite manifests check failed.'
}

$probeAddress = $null
$probePort = $null
if ($null -ne $livePath) {
    try {
        $configPath = Join-Path $liveRoot ([System.IO.Path]::GetFileNameWithoutExtension($entryPoint) + '.config')
        if (-not [System.IO.File]::Exists($configPath)) {
            throw "Listener configuration does not exist: $configPath"
        }

        $settings = [xml](Get-Content -LiteralPath $configPath -Raw)
        $appSettings = @($settings.configuration.appSettings.add)
        $listenerIp = [string](@($appSettings | Where-Object { $_.key -eq 'listenerIp' }) | Select-Object -First 1 -ExpandProperty value)
        $probePort = [int](@($appSettings | Where-Object { $_.key -eq 'listenerPort' }) | Select-Object -First 1 -ExpandProperty value)
        if ([string]::IsNullOrWhiteSpace($listenerIp) -or $probePort -lt 1) {
            throw 'listenerIp or listenerPort is missing from the live configuration.'
        }

        if ($listenerIp -in @('0.0.0.0', '::', '+', '*')) {
            $portListeners = @(Get-NetTCPConnection -State Listen -LocalPort $probePort -ErrorAction SilentlyContinue)
            if ($portListeners.Count -eq 0) {
                throw "No listener was found on port $probePort."
            }
            $probeAddress = '127.0.0.1'
        }
        else {
            $probeAddress = $listenerIp
        }

        Add-LhmLiveCheck 'listener-config' 'Pass' "Configured listener ${probeAddress}:$probePort."
    }
    catch {
        $probeAddress = $null
        Add-LhmLiveCheck 'listener-config' 'Fail' $_.Exception.Message
    }
}
else {
    Add-LhmLiveCheck 'listener-config' 'Fail' 'Prerequisite live-root check failed.'
}

if ($null -ne $probeAddress) {
    $routeResults = @()
    $routesHealthy = $true
    foreach ($route in '/', '/data.json', '/metrics') {
        try {
            $response = Invoke-WebRequest -Uri "http://${probeAddress}:$probePort$route" -UseBasicParsing -Proxy $null -TimeoutSec $EndpointTimeoutSeconds
            $routeResults += "$route=$($response.StatusCode)"
            if ([int]$response.StatusCode -ne 200) {
                $routesHealthy = $false
            }
        }
        catch {
            $routeResults += "$route=ERROR"
            $routesHealthy = $false
        }
    }

    if ($routesHealthy) {
        Add-LhmLiveCheck 'endpoints' 'Pass' ($routeResults -join ' ')
    }
    else {
        Add-LhmLiveCheck 'endpoints' 'Fail' ($routeResults -join ' ')
    }
}
else {
    Add-LhmLiveCheck 'endpoints' 'Fail' 'Prerequisite listener-config check failed.'
}

if ($null -ne $liveRoot -and [System.IO.Directory]::Exists($liveRoot)) {
    try {
        $csvPath = Join-Path $liveRoot ('LibreHardwareMonitorLog-' + (Get-Date).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture) + '.csv')
        if (-not [System.IO.File]::Exists($csvPath)) {
            throw "Current-day CSV does not exist: $csvPath"
        }
        $growthDetail = 'Current-day CSV exists; growth check skipped.'
        if ($CsvGrowthSeconds -gt 0) {
            $firstLength = ([System.IO.FileInfo]::new($csvPath)).Length
            Start-Sleep -Seconds $CsvGrowthSeconds
            $secondLength = ([System.IO.FileInfo]::new($csvPath)).Length
            if ($secondLength -gt $firstLength) {
                $growthDetail = "Current-day CSV grew $firstLength -> $secondLength bytes over $CsvGrowthSeconds second(s)."
            }
            else {
                throw "Current-day CSV did not grow (stayed at $secondLength bytes over $CsvGrowthSeconds second(s))."
            }
        }
        $clockDetail = Get-LhmCsvClockDetail -Path $csvPath
        $csvStatus = if ($CsvGrowthSeconds -eq 0) { 'Info' } else { 'Pass' }
        Add-LhmLiveCheck 'csv' $csvStatus "$growthDetail $clockDetail"
    }
    catch {
        Add-LhmLiveCheck 'csv' 'Fail' $_.Exception.Message
    }
}
else {
    Add-LhmLiveCheck 'csv' 'Fail' 'Prerequisite live-root check failed.'
}

if ($manifestsValid) {
    try {
        $logManagementRoot = Get-LhmNormalizedPath -Path ([string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'logManagement')))
        $logArchiveRoot = Get-LhmNormalizedPath -Path ([string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'logArchive')))
        $requiredTooling = @(
            'LhmLogManagement.Common.ps1',
            'Archive-LhmLogs.ps1',
            'Clean-LhmLogArchives.ps1',
            'Invoke-LhmLogManagement.ps1',
            'log-management.json'
        )
        $missing = @($requiredTooling | Where-Object { -not [System.IO.File]::Exists((Join-Path $logManagementRoot $_)) })
        if ($missing.Count -gt 0) {
            Add-LhmLiveCheck 'log-tooling' 'Fail' ("Missing installed log tooling: " + ($missing -join ', '))
        }
        else {
            if ($null -ne $logConfigContractError) {
                throw $logConfigContractError
            }
            $logConfig = $logConfigContract
            $schemaInfo = $logConfig.PSObject.Properties['Schema']
            $versionInfo = $logConfig.PSObject.Properties['Version']
            if ($null -eq $schemaInfo -or [string]$schemaInfo.Value -cne 'sq.lhm-log-management' -or
                $null -eq $versionInfo -or [string]$versionInfo.Value -cne '1') {
                throw 'Installed log-management configuration has an unsupported schema or version.'
            }

            foreach ($property in @('SourceDirectories', 'ArchiveRoot', 'MachineName', 'RetentionDays')) {
                $propertyInfo = $logConfig.PSObject.Properties[$property]
                if ($null -eq $propertyInfo -or $null -eq $propertyInfo.Value) {
                    throw "Installed log-management configuration is missing '$property'."
                }
            }

            $sourceDirectories = @($logConfig.SourceDirectories)
            $includesLiveRoot = @($sourceDirectories | Where-Object {
                Test-LhmPathEqual -Left ([string]$_) -Right $declaredLiveRootContract
            }).Count -gt 0
            if (-not $includesLiveRoot) {
                throw "Installed log-management SourceDirectories do not include the declared live root '$declaredLiveRootContract'."
            }
            if (-not (Test-LhmPathEqual -Left ([string]$logConfig.ArchiveRoot) -Right $logArchiveRoot)) {
                throw "Installed log-management ArchiveRoot '$($logConfig.ArchiveRoot)' does not match '$logArchiveRoot'."
            }

            $declaredComputer = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('computerName'))
            if ([string]$logConfig.MachineName -ine $declaredComputer) {
                throw "Installed log-management MachineName '$($logConfig.MachineName)' does not match manifest computerName '$declaredComputer'."
            }

            $retentionDays = 0
            if (-not [int]::TryParse([string]$logConfig.RetentionDays, [ref]$retentionDays) -or
                $retentionDays -lt 1 -or $retentionDays -gt 36500) {
                throw 'Installed log-management RetentionDays must be between 1 and 36500.'
            }

            Add-LhmLiveCheck 'log-tooling' 'Pass' "Installed log tooling and configuration match the declared live and archive roots at $logManagementRoot."
        }
    }
    catch {
        Add-LhmLiveCheck 'log-tooling' 'Fail' $_.Exception.Message
    }
}
else {
    Add-LhmLiveCheck 'log-tooling' 'Fail' 'Prerequisite manifests check failed.'
}

$failed = @($checks | Where-Object { $_.Status -eq 'Fail' })
if ($Json) {
    [pscustomobject]@{
        schema = 'sq.lhm.live-state'
        version = 1
        observedUtc = (Get-Date).ToUniversalTime().ToString('o')
        stackRoot = $stackRootPath
        pass = ($failed.Count -eq 0)
        checks = $checks.ToArray()
    } | ConvertTo-Json -Depth 5
}
else {
    $checks
}

if ($failed.Count -gt 0) {
    throw "$($failed.Count) live-state check(s) failed: $(@($failed | ForEach-Object { $_.Check }) -join ', ')."
}

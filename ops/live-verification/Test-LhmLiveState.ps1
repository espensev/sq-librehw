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

function Read-LhmJsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not [System.IO.File]::Exists($Path)) {
        throw "Manifest does not exist: $Path"
    }

    return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
}

function Get-LhmManifestValue {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string[]]$PropertyPath
    )

    $current = $Object
    foreach ($segment in $PropertyPath) {
        $propertyInfo = $current.PSObject.Properties[$segment]
        if ($null -eq $propertyInfo -or $null -eq $propertyInfo.Value) {
            throw "Manifest value is missing: $($PropertyPath -join '.')"
        }
        $current = $propertyInfo.Value
    }

    return $current
}

function Test-LhmReparsePoint {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force
    return (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

$layout = $null
$consumers = $null
$live = $null
try {
    $layout = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\layout.json')
    $consumers = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\consumers.json')
    $live = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\channels\live.json')
    Add-LhmLiveCheck 'manifests' 'Pass' 'layout.json, consumers.json, and channels\live.json parsed.'
}
catch {
    Add-LhmLiveCheck 'manifests' 'Fail' $_.Exception.Message
}

if ($null -ne $layout) {
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
if ($null -ne $layout -and $null -ne $live) {
    try {
        $liveRoot = [System.IO.Path]::GetFullPath([string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'live')))
        $entryPoint = [string](Get-LhmManifestValue -Object $live -PropertyPath @('entryPoint'))
        $declaredEntryPath = Join-Path $liveRoot $entryPoint
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

if ($null -ne $consumers) {
    $taskBindings = @()
    try {
        $taskBindings = @((Get-LhmManifestValue -Object $consumers -PropertyPath @('bindings')) | Where-Object { $_.kind -eq 'scheduledTask' })
    }
    catch {
        Add-LhmLiveCheck 'tasks' 'Fail' $_.Exception.Message
    }

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
            $action = @($task.Actions)[0]
            $target = [string]$binding.target
            $actionArguments = ''
            if ($null -ne $action.PSObject.Properties['Arguments'] -and $action.Arguments) {
                $actionArguments = [string]$action.Arguments
            }
            $executePath = ([string]$action.Execute).Trim('"')
            $actionMatches = ($executePath -ieq $target) -or ($actionArguments -like ('*' + [System.Management.Automation.WildcardPattern]::Escape($target) + '*'))
            if (-not $actionMatches) {
                Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task action does not reference the bound target $target."
                continue
            }

            if ($null -ne $declaredEntryPath -and $target -ieq $declaredEntryPath) {
                if ([string]$task.State -eq 'Running') {
                    Add-LhmLiveCheck "task:$bindingName" 'Pass' 'Entry-point task is Running with the bound action.'
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
                    Add-LhmLiveCheck "task:$bindingName" 'Pass' "Last run $($info.LastRunTime.ToString('o')) with result $($info.LastTaskResult)."
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
            Add-LhmLiveCheck 'csv' 'Fail' "Current-day CSV does not exist: $csvPath"
        }
        elseif ($CsvGrowthSeconds -eq 0) {
            Add-LhmLiveCheck 'csv' 'Info' 'Current-day CSV exists; growth check skipped.'
        }
        else {
            $firstLength = ([System.IO.FileInfo]::new($csvPath)).Length
            Start-Sleep -Seconds $CsvGrowthSeconds
            $secondLength = ([System.IO.FileInfo]::new($csvPath)).Length
            if ($secondLength -gt $firstLength) {
                Add-LhmLiveCheck 'csv' 'Pass' "Current-day CSV grew $firstLength -> $secondLength bytes over $CsvGrowthSeconds second(s)."
            }
            else {
                Add-LhmLiveCheck 'csv' 'Fail' "Current-day CSV did not grow (stayed at $secondLength bytes over $CsvGrowthSeconds second(s))."
            }
        }
    }
    catch {
        Add-LhmLiveCheck 'csv' 'Fail' $_.Exception.Message
    }
}
else {
    Add-LhmLiveCheck 'csv' 'Fail' 'Prerequisite live-root check failed.'
}

if ($null -ne $layout) {
    try {
        $logManagementRoot = [System.IO.Path]::GetFullPath([string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'logManagement')))
        $requiredTooling = @(
            'LhmLogManagement.Common.ps1',
            'Archive-LhmLogs.ps1',
            'Clean-LhmLogArchives.ps1',
            'Invoke-LhmLogManagement.ps1',
            'log-management.json'
        )
        $missing = @($requiredTooling | Where-Object { -not [System.IO.File]::Exists((Join-Path $logManagementRoot $_)) })
        if ($missing.Count -eq 0) {
            Add-LhmLiveCheck 'log-tooling' 'Pass' "Installed log tooling is complete at $logManagementRoot."
        }
        else {
            Add-LhmLiveCheck 'log-tooling' 'Fail' ("Missing installed log tooling: " + ($missing -join ', '))
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

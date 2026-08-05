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

function Get-LhmNormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    while ($fullPath.Length -gt $pathRoot.Length -and
           ($fullPath.EndsWith([string][System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::Ordinal) -or
            $fullPath.EndsWith([string][System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::Ordinal))) {
        $fullPath = $fullPath.Substring(0, $fullPath.Length - 1)
    }

    return $fullPath
}

function Test-LhmPathEqual {
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )

    return [string]::Equals((Get-LhmNormalizedPath -Path $Left),
                            (Get-LhmNormalizedPath -Path $Right),
                            [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-LhmJsonContract {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$DocumentName,
        [Parameter(Mandatory)][string]$ExpectedSchema
    )

    $schema = [string](Get-LhmManifestValue -Object $Object -PropertyPath @('schema'))
    $version = Get-LhmManifestValue -Object $Object -PropertyPath @('version')
    if ($schema -cne $ExpectedSchema) {
        throw "$DocumentName schema is '$schema'; expected '$ExpectedSchema'."
    }
    if ([string]$version -cne '1') {
        throw "$DocumentName version is '$version'; expected '1'."
    }
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

    return $value
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
    $layout = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\layout.json')
    $consumers = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\consumers.json')
    $live = Read-LhmJsonFile -Path (Join-Path $stackRootPath 'manifests\channels\live.json')

    Assert-LhmJsonContract -Object $layout -DocumentName 'layout.json' -ExpectedSchema 'sq.librehw-layout'
    Assert-LhmJsonContract -Object $consumers -DocumentName 'consumers.json' -ExpectedSchema 'sq.librehw-consumers'
    Assert-LhmJsonContract -Object $live -DocumentName 'channels\live.json' -ExpectedSchema 'sq.librehw-live-channel'

    $layoutMachineId = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('machineId'))
    $consumerMachineId = [string](Get-LhmManifestValue -Object $consumers -PropertyPath @('machineId'))
    $liveMachineId = [string](Get-LhmManifestValue -Object $live -PropertyPath @('machineId'))
    if ([string]::IsNullOrWhiteSpace($layoutMachineId) -or
        -not [string]::Equals($layoutMachineId, $consumerMachineId, [System.StringComparison]::Ordinal) -or
        -not [string]::Equals($layoutMachineId, $liveMachineId, [System.StringComparison]::Ordinal)) {
        throw "Manifest machineId values do not agree: layout='$layoutMachineId', consumers='$consumerMachineId', live='$liveMachineId'."
    }

    $declaredStackRoot = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'root'))
    if (-not (Test-LhmPathEqual -Left $declaredStackRoot -Right $stackRootPath)) {
        throw "layout.json operations.root '$declaredStackRoot' does not match StackRoot '$stackRootPath'."
    }

    $declaredLiveRootContract = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'live'))
    $declaredDeploymentRoot = [string](Get-LhmManifestValue -Object $live -PropertyPath @('deploymentRoot'))
    if (-not (Test-LhmPathEqual -Left $declaredDeploymentRoot -Right $declaredLiveRootContract)) {
        throw "channels\live.json deploymentRoot '$declaredDeploymentRoot' does not match layout.json operations.live '$declaredLiveRootContract'."
    }

    $declaredEntryPointContract = [string](Get-LhmManifestValue -Object $live -PropertyPath @('entryPoint'))
    $declaredEntryTargetContract = Get-LhmNormalizedPath -Path (Join-Path $declaredLiveRootContract $declaredEntryPointContract)
    $declaredLogManagementRootContract = [string](Get-LhmManifestValue -Object $layout -PropertyPath @('operations', 'logManagement'))
    $declaredLogTaskTargetContract = Get-LhmNormalizedPath -Path (Join-Path $declaredLogManagementRootContract 'Invoke-LhmLogManagement.ps1')
    $declaredTaskBindingsContract = @((Get-LhmManifestValue -Object $consumers -PropertyPath @('bindings')) |
        Where-Object { $_.kind -eq 'scheduledTask' })
    $liveTargetCount = 0
    $logTargetCount = 0
    $observedTaskTargets = @()
    foreach ($binding in $declaredTaskBindingsContract) {
        $bindingTarget = Get-LhmNormalizedPath -Path ([string](Get-LhmManifestValue -Object $binding -PropertyPath @('target')))
        $observedTaskTargets += $bindingTarget
        if (Test-LhmPathEqual -Left $bindingTarget -Right $declaredEntryTargetContract) {
            $liveTargetCount++
        }
        if (Test-LhmPathEqual -Left $bindingTarget -Right $declaredLogTaskTargetContract) {
            $logTargetCount++
        }
    }
    if ($declaredTaskBindingsContract.Count -ne 2 -or $liveTargetCount -ne 1 -or $logTargetCount -ne 1) {
        throw ("consumers.json scheduledTask bindings must contain exactly the declared live target '$declaredEntryTargetContract' " +
               "and log-management target '$declaredLogTaskTargetContract'; found $($declaredTaskBindingsContract.Count) binding(s): " +
               ($observedTaskTargets -join ', '))
    }

    $manifestsValid = $true
    Add-LhmLiveCheck 'manifests' 'Pass' 'All three manifests parsed; schema, version, machineId, roots, and the two required scheduled-task target contracts agree.'
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
            $actualWorkingDirectory = [string]$action.WorkingDirectory
            if ([string]::IsNullOrWhiteSpace($actualWorkingDirectory) -or
                -not (Test-LhmPathEqual -Left $actualWorkingDirectory -Right $expectedWorkingDirectory)) {
                Add-LhmLiveCheck "task:$bindingName" 'Fail' "Task working directory '$actualWorkingDirectory' does not match '$expectedWorkingDirectory'."
                continue
            }

            $actionArguments = ''
            if ($null -ne $action.PSObject.Properties['Arguments'] -and $action.Arguments) {
                $actionArguments = [string]$action.Arguments
            }
            $executePath = ([string]$action.Execute).Trim('"')
            if ([System.IO.Path]::GetExtension($target) -ieq '.ps1') {
                $engineName = [System.IO.Path]::GetFileName($executePath)
                if ($engineName -notin @('powershell.exe', 'pwsh.exe')) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Script task execute path '$executePath' is not powershell.exe or pwsh.exe."
                    continue
                }

                if (-not [System.IO.Path]::IsPathRooted($executePath)) {
                    Add-LhmLiveCheck "task:$bindingName" 'Fail' "Script task execute path '$executePath' is not a full application path."
                    continue
                }
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

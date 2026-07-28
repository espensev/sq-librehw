[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$opsRoot = Join-Path $repositoryRoot 'ops\local-release'
$commonScript = Join-Path $opsRoot 'LhmLocalRelease.Common.ps1'
$installScript = Join-Path $opsRoot 'Install-LibreHardwareMonitorRelease.ps1'
$rollbackScript = Join-Path $opsRoot 'Restore-LibreHardwareMonitorRelease.ps1'
$canonicalLauncher = Join-Path $opsRoot 'Start-LibreHardwareMonitor.ps1'
$cleanupScript = Join-Path $PSScriptRoot 'Clear-LhmRepositoryBuildOutputs.ps1'
. $commonScript

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )
    if (-not $Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $MessagePattern
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $MessagePattern) {
            throw "Expected failure matching '$MessagePattern', got '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected failure matching '$MessagePattern', but the action succeeded."
}

function Get-TreeSignature {
    param([Parameter(Mandatory)][string] $Root)
    if (-not (Test-Path -LiteralPath $Root)) {
        return '<missing>'
    }

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    return (@(Get-ChildItem -LiteralPath $rootPath -Force -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($rootPath.Length).TrimStart('\')
        if ($_.PSIsContainer) {
            "D|$relative"
        }
        else {
            "F|$relative|$($_.Length)|$((Get-LhmFileSha256 -Path $_.FullName))"
        }
    } | Sort-Object) -join "`n")
}

function New-TestCandidate {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $ShortCommit
    )

    $sourceRoot = Join-Path $Root "fixture-$Name"
    $outputRoot = Join-Path $sourceRoot 'out'
    $candidateRoot = Join-Path $Root "candidate-$Name"
    [System.IO.Directory]::CreateDirectory($sourceRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($candidateRoot) | Out-Null

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>LibreHardwareMonitor.Windows.Forms</AssemblyName>
    <Version>$Version</Version>
    <FileVersion>$Version.1</FileVersion>
    <InformationalVersion>$Version+$ShortCommit.20260725</InformationalVersion>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $sourceRoot 'fixture.csproj') -Encoding UTF8
    "public static class Program { public static void Main() { System.Console.WriteLine(`"$Name`"); } }" |
        Set-Content -LiteralPath (Join-Path $sourceRoot 'Program.cs') -Encoding UTF8

    $publishOutput = @(& dotnet publish `
        (Join-Path $sourceRoot 'fixture.csproj') `
        -c Release `
        -o $outputRoot `
        --nologo)
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture publish failed for '$Name' with exit code $LASTEXITCODE.`n$($publishOutput -join "`n")"
    }

    $executablePath = Join-Path $candidateRoot $script:LhmExecutableName
    Copy-Item `
        -LiteralPath (Join-Path $outputRoot $script:LhmExecutableName) `
        -Destination $executablePath
    $productVersion =
        [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath).ProductVersion
    $commit = $ShortCommit + ('0' * (40 - $ShortCommit.Length))
    [ordered]@{
        schema = $script:LhmReleaseSchema
        releaseId = "$Name-$ShortCommit"
        commit = $commit
        version = $productVersion
        framework = 'net10.0-windows'
        runtime = 'win-x64'
        selfContained = $false
        timestamp = [DateTimeOffset]::UtcNow.ToString('o')
        sha256 = Get-LhmFileSha256 -Path $executablePath
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $candidateRoot $script:LhmReleaseManifestName) `
        -Encoding UTF8

    $null = Read-LhmReleasePayload -Directory $candidateRoot -RequireCandidateShape
    return $candidateRoot
}

function Assert-NoTransactionDebris {
    param([Parameter(Mandatory)][string] $InstallRoot)
    $debris = @(Get-ChildItem -LiteralPath $InstallRoot -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '.release-*' })
    $debrisNames = @($debris | ForEach-Object Name)
    Assert-True ($debris.Count -eq 0) "Transaction debris remains: $($debrisNames -join ', ')."
}

function New-LegacyRecoveryFixture {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $PublicShimPath,
        [Parameter(Mandatory)][string] $PublicShimSha256
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $legacyExecutable =
        'E:\SQ_HQ\Monitoring\sq-librehwdev\sq-librehw\bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe'
    $legacyWorkingDirectory = Split-Path -Parent $legacyExecutable
    $userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><URI>\LibreHardwareMonitor</URI></RegistrationInfo>
  <Principals><Principal id="Author"><UserId>$userSid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><AllowHardTerminate>false</AllowHardTerminate><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><StartWhenAvailable>true</StartWhenAvailable></Settings>
  <Triggers><LogonTrigger /></Triggers>
  <Actions Context="Author"><Exec><Command>$legacyExecutable</Command><WorkingDirectory>$legacyWorkingDirectory</WorkingDirectory></Exec></Actions>
</Task>
"@
    $taskXmlPath = Join-Path $Root 'legacy-task.xml'
    $taskXml | Set-Content -LiteralPath $taskXmlPath -Encoding Unicode
    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        for ($index = 0; $index -lt 2; $index++) {
            $shortcut = $null
            try {
                $shortcut = $shell.CreateShortcut((Join-Path $Root "$index.lnk"))
                $shortcut.TargetPath = $script:LhmLegacyShortcutTargetPath[$index]
                $shortcut.Arguments = $script:LhmLegacyShortcutArguments[$index]
                $shortcut.WorkingDirectory = $script:LhmLegacyShortcutWorkingDirectory
                $shortcut.Save()
            }
            finally {
                if ($null -ne $shortcut -and
                    [System.Runtime.InteropServices.Marshal]::IsComObject($shortcut)) {
                    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
                }
            }
        }
    }
    finally {
        if ($null -ne $shell -and
            [System.Runtime.InteropServices.Marshal]::IsComObject($shell)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }

    [ordered]@{
        schema = 'sq.librehw.legacy-cutover-recovery.v1'
        createdAt = [DateTimeOffset]::UtcNow.ToString('o')
        legacyTaskPath = '\LibreHardwareMonitor'
        legacyTaskXml = 'legacy-task.xml'
        legacyTaskXmlSha256 = Get-LhmFileSha256 -Path $taskXmlPath
        publicShimPath = $PublicShimPath
        publicShimSha256 = $PublicShimSha256
        shortcuts = @(
            [ordered]@{
                path = 'C:\Users\Sev\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LibreHardwareMonitor.Windows.Forms.lnk'
                existed = $true
                backup = '0.lnk'
                sha256 = Get-LhmFileSha256 -Path (Join-Path $Root '0.lnk')
            },
            [ordered]@{
                path = 'C:\Users\Sev\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LibreHW-No-UAC.lnk'
                existed = $true
                backup = '1.lnk'
                sha256 = Get-LhmFileSha256 -Path (Join-Path $Root '1.lnk')
            }
        )
    } | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $Root 'recovery.json') `
        -Encoding UTF8

    return $Root
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "sq-librehw-release-test-$([guid]::NewGuid().ToString('N'))"
$installRoot = Join-Path $testRoot 'install'
$dataRoot = Join-Path $testRoot 'data'
$externalRoot = Join-Path $testRoot 'external-state'
$launcherTarget = Join-Path $testRoot 'script-data\Start-LibreHardwareMonitor.ps1'
$shimPath = Join-Path $testRoot 'bin\librehw.cmd'
$initialConfig = Join-Path $testRoot 'initial.config'
$oldNugetPackages = $env:NUGET_PACKAGES
$oldDotnetNoLogo = $env:DOTNET_NOLOGO
$oldDotnetTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$oldDotnetCertificate = $env:DOTNET_GENERATE_ASPNET_CERTIFICATE

try {
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $launcherTarget)) | Out-Null
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $shimPath)) | Out-Null
    [System.IO.Directory]::CreateDirectory($externalRoot) | Out-Null
    '@echo off' | Set-Content -LiteralPath $shimPath -Encoding ASCII
    'legacy launcher' | Set-Content -LiteralPath $launcherTarget -Encoding UTF8
    '{"legacy":true}' | Set-Content `
        -LiteralPath (Join-Path $externalRoot 'managed-task.json') `
        -Encoding UTF8
    '<configuration><appSettings /></configuration>' |
        Set-Content -LiteralPath $initialConfig -Encoding UTF8
    $shimHash = Get-LhmFileSha256 -Path $shimPath
    $legacyLauncherHash = Get-LhmFileSha256 -Path $launcherTarget
    $legacyTaskHash = Get-LhmFileSha256 -Path (Join-Path $externalRoot 'managed-task.json')

    $env:NUGET_PACKAGES = Join-Path $testRoot 'nuget'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $candidate1 = New-TestCandidate `
        -Root $testRoot -Name 'one' -Version '1.0.1' -ShortCommit 'abcde01'
    $candidate2 = New-TestCandidate `
        -Root $testRoot -Name 'two' -Version '1.0.2' -ShortCommit 'abcde02'

    $allScripts = @(Get-ChildItem -LiteralPath $opsRoot, $PSScriptRoot -Filter '*.ps1' -File)
    foreach ($scriptFile in $allScripts) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            $scriptFile.FullName,
            [ref]$tokens,
            [ref]$errors)
        Assert-True ($errors.Count -eq 0) "PowerShell parser errors in '$($scriptFile.Name)'."
    }

    $windowsPowerShell = Get-Command powershell.exe -CommandType Application -ErrorAction Stop
    $launcherCompatibilityOutput = @(
        & $windowsPowerShell.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -File $canonicalLauncher `
            -ValidateScriptOnly 2>&1
    )
    $launcherCompatibilityExitCode = $LASTEXITCODE
    Assert-True (
        $launcherCompatibilityExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 launcher compatibility failed: ' +
        ($launcherCompatibilityOutput -join "`n")
    )

    $launcherNoProcessHarness = @"
function Get-Process {
    [CmdletBinding()]
    param([string] `$Name)
    return @()
}

& '$canonicalLauncher' -ValidateScriptOnly
"@
    $launcherNoProcessOutput = @(
        & $windowsPowerShell.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -Command $launcherNoProcessHarness 2>&1
    )
    $launcherNoProcessExitCode = $LASTEXITCODE
    Assert-True (
        $launcherNoProcessExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 empty-process launcher compatibility failed: ' +
        ($launcherNoProcessOutput -join "`n")
    )
    Assert-True (
        ($launcherNoProcessOutput -join "`n") -match
            'DetectedProcessCount\s*:\s*0'
    ) (
        'Windows PowerShell 5.1 empty-process launcher validation did not ' +
        'report zero detected processes.'
    )

    $cleanupCompatibilityOutput = @(
        & $windowsPowerShell.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -File $cleanupScript `
            -WhatIf 2>&1
    )
    $cleanupCompatibilityExitCode = $LASTEXITCODE
    Assert-True (
        $cleanupCompatibilityExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 default cleanup invocation failed: ' +
        ($cleanupCompatibilityOutput -join "`n")
    )

    $cleanupFixtureRoot = Join-Path $testRoot 'cleanup-repository'
    $cleanupOutsideRoot = Join-Path $testRoot 'cleanup-outside'
    $junctionPath = Join-Path $cleanupFixtureRoot 'Aga.Controls'
    $outsideBin = Join-Path $cleanupOutsideRoot 'bin'
    $outsideSentinel = Join-Path $outsideBin 'must-not-delete.txt'
    [System.IO.Directory]::CreateDirectory($cleanupFixtureRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($outsideBin) | Out-Null
    foreach ($marker in @(
        'LibreHardwareMonitor.sln',
        'Directory.Build.props',
        'Directory.Packages.props'
    )) {
        'cleanup test marker' |
            Set-Content `
                -LiteralPath (Join-Path $cleanupFixtureRoot $marker) `
                -Encoding UTF8
    }
    'outside fixture' |
        Set-Content -LiteralPath $outsideSentinel -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $junctionPath `
        -Target $cleanupOutsideRoot
    try {
        Assert-Throws -MessagePattern 'ancestry contains a reparse point' -Action {
            & $cleanupScript `
                -RepositoryRoot $cleanupFixtureRoot `
                -Confirm:$false
        }
        Assert-True (
            Test-Path -LiteralPath $outsideSentinel -PathType Leaf
        ) 'Cleanup traversed a junction parent and deleted external data.'
    }
    finally {
        if (Test-Path -LiteralPath $junctionPath) {
            Remove-Item -LiteralPath $junctionPath -Force
        }
    }

    $nestedOutputRoot = Join-Path $cleanupFixtureRoot 'Aga.Controls\bin'
    $nestedJunctionPath = Join-Path $nestedOutputRoot 'external-link'
    [System.IO.Directory]::CreateDirectory($nestedOutputRoot) | Out-Null
    $null = New-Item `
        -ItemType Junction `
        -Path $nestedJunctionPath `
        -Target $outsideBin
    try {
        $nestedCleanupCommand = (
            "& '$($cleanupScript.Replace("'", "''"))' " +
            "-RepositoryRoot '$($cleanupFixtureRoot.Replace("'", "''"))' " +
            '-Confirm:$false'
        )
        $nestedCleanupOutput = @(
            & $windowsPowerShell.Source `
                -NoLogo `
                -NoProfile `
                -ExecutionPolicy Bypass `
                -Command $nestedCleanupCommand 2>&1
        )
        $nestedCleanupExitCode = $LASTEXITCODE
        Assert-True (
            $nestedCleanupExitCode -ne 0 -and
            ($nestedCleanupOutput -join "`n") -match 'contains a reparse point'
        ) (
            'Windows PowerShell 5.1 nested-junction cleanup guard failed: ' +
            ($nestedCleanupOutput -join "`n")
        )
        Assert-True (
            Test-Path -LiteralPath $outsideSentinel -PathType Leaf
        ) 'Cleanup traversed a nested junction and deleted external data.'
    }
    finally {
        if ([System.IO.Directory]::Exists($nestedJunctionPath)) {
            [System.IO.Directory]::Delete($nestedJunctionPath, $false)
        }
    }

    Assert-Throws -MessagePattern 'must not be a filesystem root' -Action {
        & $cleanupScript `
            -RepositoryRoot ([System.IO.Path]::GetPathRoot($cleanupFixtureRoot)) `
            -WhatIf
    }

    $preStableTaskContractPath = Join-Path $testRoot 'pre-stable-task-contract.xml'
    $preStableUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $preStableWorkingDirectory =
        Split-Path -Parent $script:LhmPreStableManagedExecutablePath
    @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><URI>$($script:LhmManagedTaskPath)</URI></RegistrationInfo>
  <Principals><Principal id="Author"><UserId>$preStableUserSid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><AllowHardTerminate>false</AllowHardTerminate><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><StartWhenAvailable>true</StartWhenAvailable></Settings>
  <Triggers />
  <Actions Context="Author"><Exec><Command>$($script:LhmPreStableManagedExecutablePath)</Command><WorkingDirectory>$preStableWorkingDirectory</WorkingDirectory></Exec></Actions>
</Task>
"@ | Set-Content -LiteralPath $preStableTaskContractPath -Encoding Unicode
    Assert-LhmPreStableManagedTaskBackupContract -Path $preStableTaskContractPath

    $hostilePreStableTaskContractPath =
        Join-Path $testRoot 'pre-stable-task-contract-hostile.xml'
    (Get-Content -LiteralPath $preStableTaskContractPath -Raw -Encoding Unicode).Replace(
        $script:LhmPreStableManagedExecutablePath,
        'C:\Windows\System32\cmd.exe') |
        Set-Content -LiteralPath $hostilePreStableTaskContractPath -Encoding Unicode
    Assert-Throws -MessagePattern 'exact discovered task contract' -Action {
        Assert-LhmPreStableManagedTaskBackupContract `
            -Path $hostilePreStableTaskContractPath
    }

    $legacyFixtureRoot = New-LegacyRecoveryFixture `
        -Root (Join-Path $testRoot 'legacy-recovery-valid') `
        -PublicShimPath $shimPath `
        -PublicShimSha256 $shimHash
    $null = Read-LhmLegacyRecoveryPacket `
        -RecoveryRoot $legacyFixtureRoot `
        -ExpectedPublicShimPath $shimPath `
        -ExpectedPublicShimSha256 $shimHash `
        -NonLiveTestMode

    $tamperedPathRoot = Join-Path $testRoot 'legacy-recovery-tampered-path'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $tamperedPathRoot -Recurse
    $tamperedPathManifest =
        Get-Content -LiteralPath (Join-Path $tamperedPathRoot 'recovery.json') -Raw |
        ConvertFrom-Json
    $tamperedPathManifest.shortcuts[0].path = 'C:\Windows\System32\arbitrary.lnk'
    $tamperedPathManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $tamperedPathRoot 'recovery.json')
    Assert-Throws -MessagePattern 'shortcut record 0' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $tamperedPathRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $tamperedTypeRoot = Join-Path $testRoot 'legacy-recovery-tampered-type'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $tamperedTypeRoot -Recurse
    $tamperedTypeManifest =
        Get-Content -LiteralPath (Join-Path $tamperedTypeRoot 'recovery.json') -Raw |
        ConvertFrom-Json
    $tamperedTypeManifest.shortcuts[0].existed = 'true'
    $tamperedTypeManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $tamperedTypeRoot 'recovery.json')
    Assert-Throws -MessagePattern 'shortcut record 0' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $tamperedTypeRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $tamperedXmlRoot = Join-Path $testRoot 'legacy-recovery-tampered-xml'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $tamperedXmlRoot -Recurse
    $tamperedXmlPath = Join-Path $tamperedXmlRoot 'legacy-task.xml'
    $tamperedXmlText =
        Get-Content -LiteralPath $tamperedXmlPath -Raw -Encoding Unicode
    $tamperedXmlText.Replace(
        'E:\SQ_HQ\Monitoring\sq-librehwdev\sq-librehw\bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe',
        'C:\Windows\System32\cmd.exe') |
        Set-Content -LiteralPath $tamperedXmlPath -Encoding Unicode
    $tamperedXmlManifest =
        Get-Content -LiteralPath (Join-Path $tamperedXmlRoot 'recovery.json') -Raw |
        ConvertFrom-Json
    $tamperedXmlManifest.legacyTaskXmlSha256 = Get-LhmFileSha256 -Path $tamperedXmlPath
    $tamperedXmlManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $tamperedXmlRoot 'recovery.json')
    Assert-Throws -MessagePattern 'exact discovered legacy task contract' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $tamperedXmlRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $unexpectedEntryRoot = Join-Path $testRoot 'legacy-recovery-unexpected-entry'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $unexpectedEntryRoot -Recurse
    'unexpected' | Set-Content -LiteralPath (Join-Path $unexpectedEntryRoot 'extra.bin')
    Assert-Throws -MessagePattern 'unexpected or unsafe entries' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $unexpectedEntryRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $tamperedShortcutRoot = Join-Path $testRoot 'legacy-recovery-tampered-shortcut'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $tamperedShortcutRoot -Recurse
    $tamperedShortcutPath = Join-Path $tamperedShortcutRoot '0.lnk'
    $shell = $null
    $shortcut = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($tamperedShortcutPath)
        $shortcut.TargetPath = 'C:\Windows\System32\cmd.exe'
        $shortcut.Arguments = '/c exit'
        $shortcut.WorkingDirectory = 'C:\Windows\System32'
        $shortcut.Save()
    }
    finally {
        if ($null -ne $shortcut -and
            [System.Runtime.InteropServices.Marshal]::IsComObject($shortcut)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }
        if ($null -ne $shell -and
            [System.Runtime.InteropServices.Marshal]::IsComObject($shell)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }
    $tamperedShortcutManifest =
        Get-Content -LiteralPath (Join-Path $tamperedShortcutRoot 'recovery.json') -Raw |
        ConvertFrom-Json
    $tamperedShortcutManifest.shortcuts[0].sha256 =
        Get-LhmFileSha256 -Path $tamperedShortcutPath
    $tamperedShortcutManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $tamperedShortcutRoot 'recovery.json')
    Assert-Throws -MessagePattern 'exact discovered target contract' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $tamperedShortcutRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $junctionTargetParent = Join-Path $testRoot 'legacy-recovery-junction-target'
    [System.IO.Directory]::CreateDirectory($junctionTargetParent) | Out-Null
    Copy-Item `
        -LiteralPath $legacyFixtureRoot `
        -Destination (Join-Path $junctionTargetParent 'packet') `
        -Recurse
    $junctionParent = Join-Path $testRoot 'legacy-recovery-junction-parent'
    $null = New-Item `
        -ItemType Junction `
        -Path $junctionParent `
        -Target $junctionTargetParent
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            $null = Read-LhmLegacyRecoveryPacket `
                -RecoveryRoot (Join-Path $junctionParent 'packet') `
                -ExpectedPublicShimPath $shimPath `
                -ExpectedPublicShimSha256 $shimHash `
                -NonLiveTestMode
        }
    }
    finally {
        if ([System.IO.Directory]::Exists($junctionParent)) {
            [System.IO.Directory]::Delete($junctionParent, $false)
        }
    }

    $installRootJunctionCase = Join-Path $testRoot 'install-root-junction'
    $installRootJunctionTarget = Join-Path $testRoot 'install-root-junction-target'
    [System.IO.Directory]::CreateDirectory($installRootJunctionCase) | Out-Null
    [System.IO.Directory]::CreateDirectory($installRootJunctionTarget) | Out-Null
    $redirectedInstallRoot = Join-Path $installRootJunctionCase 'install'
    $null = New-Item `
        -ItemType Junction `
        -Path $redirectedInstallRoot `
        -Target $installRootJunctionTarget
    $installRootJunctionTargetSignature =
        Get-TreeSignature -Root $installRootJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $redirectedInstallRoot `
                -DataRoot (Join-Path $installRootJunctionCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $installRootJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $installRootJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $installRootJunctionTarget) -ceq
                $installRootJunctionTargetSignature
        ) 'Installer wrote through a pre-existing install-root junction.'
    }
    finally {
        if ([System.IO.Directory]::Exists($redirectedInstallRoot)) {
            [System.IO.Directory]::Delete($redirectedInstallRoot, $false)
        }
    }

    $dataRootJunctionCase = Join-Path $testRoot 'data-root-junction'
    $dataRootJunctionTarget = Join-Path $testRoot 'data-root-junction-target'
    [System.IO.Directory]::CreateDirectory($dataRootJunctionCase) | Out-Null
    [System.IO.Directory]::CreateDirectory($dataRootJunctionTarget) | Out-Null
    $redirectedDataRoot = Join-Path $dataRootJunctionCase 'data'
    $null = New-Item `
        -ItemType Junction `
        -Path $redirectedDataRoot `
        -Target $dataRootJunctionTarget
    $dataRootJunctionTargetSignature =
        Get-TreeSignature -Root $dataRootJunctionTarget
    $dataJunctionInstallRoot = Join-Path $dataRootJunctionCase 'install'
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $dataJunctionInstallRoot `
                -DataRoot $redirectedDataRoot `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $dataRootJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $dataRootJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $dataRootJunctionTarget) -ceq
                $dataRootJunctionTargetSignature
        ) 'Installer wrote through a pre-existing data-root junction.'
        Assert-True (
            -not (Test-Path -LiteralPath $dataJunctionInstallRoot)
        ) 'Data-root preflight created the install root before rejecting the junction.'
    }
    finally {
        if ([System.IO.Directory]::Exists($redirectedDataRoot)) {
            [System.IO.Directory]::Delete($redirectedDataRoot, $false)
        }
    }

    $installJunctionRoot = Join-Path $testRoot 'install-recovery-parent-junction'
    $installJunctionInstallRoot = Join-Path $installJunctionRoot 'install'
    $installJunctionDataRoot = Join-Path $installJunctionRoot 'data'
    $installJunctionExternalRoot = Join-Path $installJunctionRoot 'external'
    $installJunctionLauncher =
        Join-Path $installJunctionRoot 'script-data\Start-LibreHardwareMonitor.ps1'
    $installJunctionShim = Join-Path $installJunctionRoot 'bin\librehw.cmd'
    $installJunctionConfig = Join-Path $installJunctionRoot 'initial.config'
    $installJunctionTarget = Join-Path $testRoot 'install-recovery-junction-target'
    [System.IO.Directory]::CreateDirectory($installJunctionDataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($installJunctionTarget) | Out-Null
    [System.IO.Directory]::CreateDirectory(
        (Split-Path -Parent $installJunctionShim)) | Out-Null
    '@echo off' | Set-Content -LiteralPath $installJunctionShim -Encoding ASCII
    '<configuration />' |
        Set-Content -LiteralPath $installJunctionConfig -Encoding UTF8
    $installJunctionRecoveryParent = Join-Path $installJunctionDataRoot 'release-recovery'
    $null = New-Item `
        -ItemType Junction `
        -Path $installJunctionRecoveryParent `
        -Target $installJunctionTarget
    $installJunctionTargetSignature = Get-TreeSignature -Root $installJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $installJunctionInstallRoot `
                -DataRoot $installJunctionDataRoot `
                -InitialConfigSource $installJunctionConfig `
                -LauncherTargetPath $installJunctionLauncher `
                -PublicShimPath $installJunctionShim `
                -TestExternalStateRoot $installJunctionExternalRoot `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $installJunctionTarget) -ceq
                $installJunctionTargetSignature
        ) 'Installer wrote through a recovery-parent junction before rejecting it.'
    }
    finally {
        if ([System.IO.Directory]::Exists($installJunctionRecoveryParent)) {
            [System.IO.Directory]::Delete($installJunctionRecoveryParent, $false)
        }
    }
    Assert-True (
        -not (Test-Path `
            -LiteralPath (Join-Path $installJunctionInstallRoot $script:LhmExecutableName) `
            -PathType Leaf)
    ) 'Recovery-parent junction rejection left an installed executable.'
    Assert-NoTransactionDebris -InstallRoot $installJunctionInstallRoot

    $finalizerJunctionDataRoot = Join-Path $testRoot 'finalizer-junction-data'
    $finalizerJunctionTarget = Join-Path $testRoot 'finalizer-junction-target'
    [System.IO.Directory]::CreateDirectory($finalizerJunctionDataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($finalizerJunctionTarget) | Out-Null
    $finalizerJunctionRecoveryParent =
        Join-Path $finalizerJunctionDataRoot 'release-recovery'
    $null = New-Item `
        -ItemType Junction `
        -Path $finalizerJunctionRecoveryParent `
        -Target $finalizerJunctionTarget
    $finalizerJunctionTargetSignature =
        Get-TreeSignature -Root $finalizerJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            $null = New-LhmSafeRecoveryPreparationDirectory `
                -DataRoot $finalizerJunctionDataRoot `
                -RecoveryParent $finalizerJunctionRecoveryParent `
                -PreparationPath (Join-Path `
                    $finalizerJunctionRecoveryParent `
                    ".legacy-root-task-cutover-$([guid]::NewGuid().ToString('N'))") `
                -RequiredLeafPrefix '.legacy-root-task-cutover-'
        }
        Assert-True (
            (Get-TreeSignature -Root $finalizerJunctionTarget) -ceq
                $finalizerJunctionTargetSignature
        ) 'Finalizer preparation wrote through a recovery-parent junction before rejecting it.'
    }
    finally {
        if ([System.IO.Directory]::Exists($finalizerJunctionRecoveryParent)) {
            [System.IO.Directory]::Delete($finalizerJunctionRecoveryParent, $false)
        }
    }

    $launcherJunctionCase = Join-Path $testRoot 'launcher-junction-case'
    $launcherJunctionInstallRoot = Join-Path $launcherJunctionCase 'install'
    $launcherJunctionDataRoot = Join-Path $launcherJunctionCase 'data'
    $launcherJunctionParent = Join-Path $launcherJunctionCase 'script-data'
    $launcherJunctionTarget = Join-Path $testRoot 'launcher-junction-target'
    [System.IO.Directory]::CreateDirectory($launcherJunctionCase) | Out-Null
    [System.IO.Directory]::CreateDirectory($launcherJunctionTarget) | Out-Null
    'launcher junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $launcherJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $launcherJunctionParent `
        -Target $launcherJunctionTarget
    $launcherJunctionTargetSignature = Get-TreeSignature -Root $launcherJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $launcherJunctionInstallRoot `
                -DataRoot $launcherJunctionDataRoot `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $launcherJunctionParent `
                    'Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $launcherJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $launcherJunctionTarget) -ceq
                $launcherJunctionTargetSignature
        ) 'Installer wrote through a launcher-parent junction.'
        Assert-True (
            -not (Test-Path `
                -LiteralPath (Join-Path `
                    $launcherJunctionInstallRoot `
                    $script:LhmExecutableName) `
                -PathType Leaf)
        ) 'Launcher-parent junction rejection installed an executable.'
    }
    finally {
        if ([System.IO.Directory]::Exists($launcherJunctionParent)) {
            [System.IO.Directory]::Delete($launcherJunctionParent, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $launcherJunctionInstallRoot

    $logJunctionCase = Join-Path $testRoot 'log-junction-case'
    $logJunctionDataRoot = Join-Path $logJunctionCase 'data'
    $logJunctionTarget = Join-Path $testRoot 'log-junction-target'
    $logJunctionPath = Join-Path $logJunctionDataRoot 'logs'
    [System.IO.Directory]::CreateDirectory($logJunctionDataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($logJunctionTarget) | Out-Null
    'log junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $logJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $logJunctionPath `
        -Target $logJunctionTarget
    $logJunctionTargetSignature = Get-TreeSignature -Root $logJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot (Join-Path $logJunctionCase 'install') `
                -DataRoot $logJunctionDataRoot `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $logJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $logJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $logJunctionTarget) -ceq
                $logJunctionTargetSignature
        ) 'Installer wrote through a log-directory junction.'
    }
    finally {
        if ([System.IO.Directory]::Exists($logJunctionPath)) {
            [System.IO.Directory]::Delete($logJunctionPath, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot (Join-Path $logJunctionCase 'install')

    $settingsReparseCase = Join-Path $testRoot 'settings-reparse-case'
    $settingsReparseDataRoot = Join-Path $settingsReparseCase 'data'
    $settingsReparseTargetRoot = Join-Path $testRoot 'settings-reparse-target'
    [System.IO.Directory]::CreateDirectory($settingsReparseDataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($settingsReparseTargetRoot) | Out-Null
    $settingsReparseTarget = Join-Path $settingsReparseTargetRoot 'outside.config'
    'settings reparse sentinel' |
        Set-Content -LiteralPath $settingsReparseTarget -Encoding UTF8
    $settingsReparseLink =
        Join-Path $settingsReparseDataRoot $script:LhmSettingsFileName
    $null = New-Item `
        -ItemType SymbolicLink `
        -Path $settingsReparseLink `
        -Target $settingsReparseTarget
    $settingsReparseTargetSignature =
        Get-TreeSignature -Root $settingsReparseTargetRoot
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot (Join-Path $settingsReparseCase 'install') `
                -DataRoot $settingsReparseDataRoot `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $settingsReparseCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $settingsReparseCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $settingsReparseTargetRoot) -ceq
                $settingsReparseTargetSignature
        ) 'Installer wrote through a settings-file reparse point.'
    }
    finally {
        if (Test-Path -LiteralPath $settingsReparseLink) {
            [System.IO.File]::Delete($settingsReparseLink)
        }
    }
    Assert-NoTransactionDebris -InstallRoot (Join-Path $settingsReparseCase 'install')

    $candidateJunctionCase = Join-Path $testRoot 'candidate-junction-case'
    $candidateJunctionTarget = Join-Path $testRoot 'candidate-junction-target'
    Copy-Item -LiteralPath $candidate1 -Destination $candidateJunctionTarget -Recurse
    'candidate junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $candidateJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $candidateJunctionPath = Join-Path $testRoot 'candidate-junction-link'
    $null = New-Item `
        -ItemType Junction `
        -Path $candidateJunctionPath `
        -Target $candidateJunctionTarget
    $candidateJunctionTargetSignature =
        Get-TreeSignature -Root $candidateJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidateJunctionPath `
                -InstallRoot (Join-Path $candidateJunctionCase 'install') `
                -DataRoot (Join-Path $candidateJunctionCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $candidateJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $candidateJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $candidateJunctionTarget) -ceq
                $candidateJunctionTargetSignature
        ) 'Candidate-directory junction validation changed its external target.'
        Assert-True (
            -not (Test-Path `
                -LiteralPath (Join-Path `
                    $candidateJunctionCase `
                    "install\$($script:LhmExecutableName)") `
                -PathType Leaf)
        ) 'Candidate-directory junction validation installed an executable.'
    }
    finally {
        if ([System.IO.Directory]::Exists($candidateJunctionPath)) {
            [System.IO.Directory]::Delete($candidateJunctionPath, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot (Join-Path $candidateJunctionCase 'install')

    $candidateFileReparseCase = Join-Path $testRoot 'candidate-file-reparse-case'
    $candidateFileReparseRoot = Join-Path $testRoot 'candidate-file-reparse'
    $candidateFileReparseTarget = Join-Path $testRoot 'candidate-file-reparse-target'
    Copy-Item -LiteralPath $candidate1 -Destination $candidateFileReparseRoot -Recurse
    [System.IO.Directory]::CreateDirectory($candidateFileReparseTarget) | Out-Null
    $candidateFileLink =
        Join-Path $candidateFileReparseRoot $script:LhmExecutableName
    $candidateFileTarget =
        Join-Path $candidateFileReparseTarget $script:LhmExecutableName
    Move-Item -LiteralPath $candidateFileLink -Destination $candidateFileTarget
    'candidate file sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $candidateFileReparseTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $null = New-Item `
        -ItemType SymbolicLink `
        -Path $candidateFileLink `
        -Target $candidateFileTarget
    $candidateFileReparseTargetSignature =
        Get-TreeSignature -Root $candidateFileReparseTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidateFileReparseRoot `
                -InstallRoot (Join-Path $candidateFileReparseCase 'install') `
                -DataRoot (Join-Path $candidateFileReparseCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $candidateFileReparseCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $candidateFileReparseCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $candidateFileReparseTarget) -ceq
                $candidateFileReparseTargetSignature
        ) 'Candidate-file reparse validation changed its external target.'
        Assert-True (
            -not (Test-Path `
                -LiteralPath (Join-Path `
                    $candidateFileReparseCase `
                    "install\$($script:LhmExecutableName)") `
                -PathType Leaf)
        ) 'Candidate-file reparse validation installed an executable.'
    }
    finally {
        if (Test-Path -LiteralPath $candidateFileLink) {
            [System.IO.File]::Delete($candidateFileLink)
        }
    }
    Assert-NoTransactionDebris -InstallRoot (Join-Path $candidateFileReparseCase 'install')

    $installRollbackJunctionCase = Join-Path $testRoot 'install-rollback-junction-case'
    $installRollbackJunctionRoot = Join-Path $installRollbackJunctionCase 'install'
    $installRollbackJunctionTarget =
        Join-Path $testRoot 'install-rollback-junction-target'
    [System.IO.Directory]::CreateDirectory($installRollbackJunctionRoot) | Out-Null
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate1 `
        -DestinationDirectory $installRollbackJunctionRoot
    $installRollbackCurrentHash =
        Get-LhmFileSha256 -Path (
            Join-Path $installRollbackJunctionRoot $script:LhmExecutableName)
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate2 `
        -DestinationDirectory $installRollbackJunctionTarget
    'install rollback sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $installRollbackJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $installRollbackJunction =
        Join-Path $installRollbackJunctionRoot 'rollback'
    $null = New-Item `
        -ItemType Junction `
        -Path $installRollbackJunction `
        -Target $installRollbackJunctionTarget
    $installRollbackJunctionTargetSignature =
        Get-TreeSignature -Root $installRollbackJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate2 `
                -InstallRoot $installRollbackJunctionRoot `
                -DataRoot (Join-Path $installRollbackJunctionCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $installRollbackJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $installRollbackJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $installRollbackJunctionTarget) -ceq
                $installRollbackJunctionTargetSignature
        ) 'Installer rollback-junction validation changed its external target.'
        Assert-True (
            (Get-LhmFileSha256 -Path (
                Join-Path $installRollbackJunctionRoot $script:LhmExecutableName)) -ceq
                $installRollbackCurrentHash
        ) 'Installer rollback-junction validation changed the current payload.'
    }
    finally {
        if ([System.IO.Directory]::Exists($installRollbackJunction)) {
            [System.IO.Directory]::Delete($installRollbackJunction, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $installRollbackJunctionRoot

    $transactionJunctionCase = Join-Path $testRoot 'transaction-junction-case'
    $transactionJunctionInstallRoot = Join-Path $transactionJunctionCase 'install'
    $transactionJunctionTarget = Join-Path $testRoot 'transaction-junction-target'
    [System.IO.Directory]::CreateDirectory($transactionJunctionInstallRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($transactionJunctionTarget) | Out-Null
    'transaction junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $transactionJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $transactionJunction =
        Join-Path $transactionJunctionInstallRoot '.release-transaction'
    $null = New-Item `
        -ItemType Junction `
        -Path $transactionJunction `
        -Target $transactionJunctionTarget
    $transactionJunctionTargetSignature =
        Get-TreeSignature -Root $transactionJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $transactionJunctionInstallRoot `
                -DataRoot (Join-Path $transactionJunctionCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $transactionJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $transactionJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $transactionJunctionTarget) -ceq
                $transactionJunctionTargetSignature
        ) 'Transaction-junction validation changed its external target.'
    }
    finally {
        if ([System.IO.Directory]::Exists($transactionJunction)) {
            [System.IO.Directory]::Delete($transactionJunction, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $transactionJunctionInstallRoot

    $restoreInstallJunctionCase = Join-Path $testRoot 'restore-install-junction-case'
    $restoreInstallJunctionTarget =
        Join-Path $testRoot 'restore-install-junction-target'
    [System.IO.Directory]::CreateDirectory($restoreInstallJunctionCase) | Out-Null
    [System.IO.Directory]::CreateDirectory($restoreInstallJunctionTarget) | Out-Null
    'restore install sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $restoreInstallJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $restoreInstallJunction = Join-Path $restoreInstallJunctionCase 'install'
    $null = New-Item `
        -ItemType Junction `
        -Path $restoreInstallJunction `
        -Target $restoreInstallJunctionTarget
    $restoreInstallJunctionTargetSignature =
        Get-TreeSignature -Root $restoreInstallJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $rollbackScript `
                -InstallRoot $restoreInstallJunction `
                -DataRoot (Join-Path $restoreInstallJunctionCase 'data') `
                -LauncherTargetPath (Join-Path `
                    $restoreInstallJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $restoreInstallJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $restoreInstallJunctionTarget) -ceq
                $restoreInstallJunctionTargetSignature
        ) 'Restore install-root junction validation changed its external target.'
    }
    finally {
        if ([System.IO.Directory]::Exists($restoreInstallJunction)) {
            [System.IO.Directory]::Delete($restoreInstallJunction, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $restoreInstallJunction

    $restoreRollbackJunctionCase =
        Join-Path $testRoot 'restore-rollback-junction-case'
    $restoreRollbackJunctionRoot =
        Join-Path $restoreRollbackJunctionCase 'install'
    $restoreRollbackJunctionTarget =
        Join-Path $testRoot 'restore-rollback-junction-target'
    [System.IO.Directory]::CreateDirectory($restoreRollbackJunctionRoot) | Out-Null
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate1 `
        -DestinationDirectory $restoreRollbackJunctionRoot
    $restoreRollbackCurrentHash =
        Get-LhmFileSha256 -Path (
            Join-Path $restoreRollbackJunctionRoot $script:LhmExecutableName)
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate2 `
        -DestinationDirectory $restoreRollbackJunctionTarget
    'restore rollback sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $restoreRollbackJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $restoreRollbackJunction =
        Join-Path $restoreRollbackJunctionRoot 'rollback'
    $null = New-Item `
        -ItemType Junction `
        -Path $restoreRollbackJunction `
        -Target $restoreRollbackJunctionTarget
    $restoreRollbackJunctionTargetSignature =
        Get-TreeSignature -Root $restoreRollbackJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $rollbackScript `
                -InstallRoot $restoreRollbackJunctionRoot `
                -DataRoot (Join-Path $restoreRollbackJunctionCase 'data') `
                -LauncherTargetPath (Join-Path `
                    $restoreRollbackJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $restoreRollbackJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $restoreRollbackJunctionTarget) -ceq
                $restoreRollbackJunctionTargetSignature
        ) 'Restore rollback-junction validation changed its external target.'
        Assert-True (
            (Get-LhmFileSha256 -Path (
                Join-Path $restoreRollbackJunctionRoot $script:LhmExecutableName)) -ceq
                $restoreRollbackCurrentHash
        ) 'Restore rollback-junction validation changed the current payload.'
    }
    finally {
        if ([System.IO.Directory]::Exists($restoreRollbackJunction)) {
            [System.IO.Directory]::Delete($restoreRollbackJunction, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $restoreRollbackJunctionRoot

    $ownedDirectoryParent = Join-Path $testRoot 'owned-directory-guards'
    $ownedDirectoryTarget = Join-Path $testRoot 'owned-directory-target'
    [System.IO.Directory]::CreateDirectory($ownedDirectoryParent) | Out-Null
    [System.IO.Directory]::CreateDirectory($ownedDirectoryTarget) | Out-Null
    'owned target sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $ownedDirectoryTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $ownedDirectoryJunction =
        Join-Path `
            $ownedDirectoryParent `
            ".release-preparing-$([guid]::NewGuid().ToString('N'))"
    $null = New-Item `
        -ItemType Junction `
        -Path $ownedDirectoryJunction `
        -Target $ownedDirectoryTarget
    $ownedDirectoryTargetSignature = Get-TreeSignature -Root $ownedDirectoryTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            Remove-LhmOwnedDirectory `
                -Path $ownedDirectoryJunction `
                -ExpectedParent $ownedDirectoryParent `
                -RequiredLeafPrefix '.release-preparing-'
        }
        Assert-True (
            (Get-TreeSignature -Root $ownedDirectoryTarget) -ceq
                $ownedDirectoryTargetSignature
        ) 'Owned-directory target-junction validation changed its external target.'
    }
    finally {
        if ([System.IO.Directory]::Exists($ownedDirectoryJunction)) {
            [System.IO.Directory]::Delete($ownedDirectoryJunction, $false)
        }
    }

    $ownedDirectoryWithChild =
        Join-Path `
            $ownedDirectoryParent `
            ".release-preparing-$([guid]::NewGuid().ToString('N'))"
    $ownedDirectoryChildTarget = Join-Path $testRoot 'owned-directory-child-target'
    [System.IO.Directory]::CreateDirectory($ownedDirectoryWithChild) | Out-Null
    [System.IO.Directory]::CreateDirectory($ownedDirectoryChildTarget) | Out-Null
    'owned child sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $ownedDirectoryChildTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $ownedDirectoryChildJunction =
        Join-Path $ownedDirectoryWithChild 'redirected-child'
    $null = New-Item `
        -ItemType Junction `
        -Path $ownedDirectoryChildJunction `
        -Target $ownedDirectoryChildTarget
    $ownedDirectoryChildTargetSignature =
        Get-TreeSignature -Root $ownedDirectoryChildTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            Remove-LhmOwnedDirectory `
                -Path $ownedDirectoryWithChild `
                -ExpectedParent $ownedDirectoryParent `
                -RequiredLeafPrefix '.release-preparing-'
        }
        Assert-True (
            (Get-TreeSignature -Root $ownedDirectoryChildTarget) -ceq
                $ownedDirectoryChildTargetSignature
        ) 'Owned-directory child-junction validation changed its external target.'
        Assert-True (
            Test-Path -LiteralPath $ownedDirectoryWithChild -PathType Container
        ) 'Owned-directory child-junction validation removed its guarded root.'
    }
    finally {
        if ([System.IO.Directory]::Exists($ownedDirectoryChildJunction)) {
            [System.IO.Directory]::Delete($ownedDirectoryChildJunction, $false)
        }
        if ([System.IO.Directory]::Exists($ownedDirectoryWithChild)) {
            [System.IO.Directory]::Delete($ownedDirectoryWithChild, $true)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $ownedDirectoryParent

    $beforeWhatIf = Get-TreeSignature -Root $testRoot
    & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -InitialConfigSource $initialConfig `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -WhatIf
    Assert-True ((Get-TreeSignature -Root $testRoot) -ceq $beforeWhatIf) '-WhatIf changed temporary state.'

    Assert-Throws -MessagePattern 'AfterTaskStage' -Action {
        & $installScript `
            -CandidateDirectory $candidate1 `
            -InstallRoot $installRoot `
            -DataRoot $dataRoot `
            -InitialConfigSource $initialConfig `
            -LauncherTargetPath $launcherTarget `
            -PublicShimPath $shimPath `
            -TestExternalStateRoot $externalRoot `
            -NonLiveTestMode `
            -TestFailurePoint AfterTaskStage `
            -Confirm:$false
    }
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot $script:LhmExecutableName))) 'Failed first install left an EXE.'
    Assert-True ((Get-LhmFileSha256 -Path $launcherTarget) -ceq $legacyLauncherHash) 'Failed first install did not restore launcher.'
    Assert-True ((Get-LhmFileSha256 -Path (Join-Path $externalRoot 'managed-task.json')) -ceq $legacyTaskHash) 'Failed first install did not restore task state.'
    Assert-NoTransactionDebris -InstallRoot $installRoot

    Assert-Throws -MessagePattern 'AfterHealth' -Action {
        & $installScript `
            -CandidateDirectory $candidate1 `
            -InstallRoot $installRoot `
            -DataRoot $dataRoot `
            -InitialConfigSource $initialConfig `
            -LauncherTargetPath $launcherTarget `
            -PublicShimPath $shimPath `
            -TestExternalStateRoot $externalRoot `
            -NonLiveTestMode `
            -TestFailurePoint AfterHealth `
            -Confirm:$false
    }
    $preStableRecoveryRoot = Join-Path $dataRoot 'release-recovery\pre-stable-startup'
    Assert-True (Test-Path -LiteralPath $preStableRecoveryRoot -PathType Container) 'AfterHealth failure did not retain bounded pre-stable recovery.'
    $preStableRecoverySignature = Get-TreeSignature -Root $preStableRecoveryRoot
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot $script:LhmExecutableName))) 'AfterHealth first-install recovery left an EXE.'
    Assert-True ((Get-LhmFileSha256 -Path $launcherTarget) -ceq $legacyLauncherHash) 'AfterHealth first-install recovery did not restore launcher.'
    Assert-True ((Get-LhmFileSha256 -Path (Join-Path $externalRoot 'managed-task.json')) -ceq $legacyTaskHash) 'AfterHealth first-install recovery did not restore task state.'
    Assert-NoTransactionDebris -InstallRoot $installRoot

    $null = Read-LhmPreStableRecoveryPacket `
        -RecoveryRoot $preStableRecoveryRoot `
        -ExpectedLauncherTargetPath $launcherTarget `
        -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
        -ExpectedPublicShimPath $shimPath `
        -ExpectedPublicShimSha256 $shimHash `
        -ExpectedLauncherBackupSha256 $legacyLauncherHash `
        -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
        -NonLiveTestMode `
        -TestExternalStateRoot $externalRoot `
        -RequireExternalStateMatch

    $tamperedPreStableLauncherRoot =
        Join-Path $testRoot 'pre-stable-recovery-tampered-launcher'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $tamperedPreStableLauncherRoot `
        -Recurse
    $tamperedPreStableLauncherPath =
        Join-Path $tamperedPreStableLauncherRoot 'launcher-backup.ps1'
    'Start-Process C:\Windows\System32\cmd.exe' |
        Set-Content -LiteralPath $tamperedPreStableLauncherPath -Encoding UTF8
    $tamperedPreStableLauncherManifest =
        Get-Content `
            -LiteralPath (Join-Path $tamperedPreStableLauncherRoot 'recovery.json') `
            -Raw |
        ConvertFrom-Json
    $tamperedPreStableLauncherManifest.launcherSha256 =
        Get-LhmFileSha256 -Path $tamperedPreStableLauncherPath
    $tamperedPreStableLauncherManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $tamperedPreStableLauncherRoot 'recovery.json')
    Assert-Throws -MessagePattern 'trusted content' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $tamperedPreStableLauncherRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $tamperedPreStableTaskRoot =
        Join-Path $testRoot 'pre-stable-recovery-tampered-task'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $tamperedPreStableTaskRoot `
        -Recurse
    $tamperedPreStableTaskPath =
        Join-Path $tamperedPreStableTaskRoot 'managed-task.test.json'
    '{"taskPath":"\\SevGrp\\AdminTask\\LibreHW-No-UAC","execute":"C:\\Windows\\System32\\cmd.exe"}' |
        Set-Content -LiteralPath $tamperedPreStableTaskPath -Encoding UTF8
    $tamperedPreStableTaskManifest =
        Get-Content `
            -LiteralPath (Join-Path $tamperedPreStableTaskRoot 'recovery.json') `
            -Raw |
        ConvertFrom-Json
    $tamperedPreStableTaskManifest.managedTaskSha256 =
        Get-LhmFileSha256 -Path $tamperedPreStableTaskPath
    $tamperedPreStableTaskManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $tamperedPreStableTaskRoot 'recovery.json')
    Assert-Throws -MessagePattern 'trusted content' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $tamperedPreStableTaskRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $tamperedPreStablePathRoot =
        Join-Path $testRoot 'pre-stable-recovery-tampered-path'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $tamperedPreStablePathRoot `
        -Recurse
    $tamperedPreStablePathManifest =
        Get-Content `
            -LiteralPath (Join-Path $tamperedPreStablePathRoot 'recovery.json') `
            -Raw |
        ConvertFrom-Json
    $tamperedPreStablePathManifest.launcherBackup = '..\outside.ps1'
    $tamperedPreStablePathManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $tamperedPreStablePathRoot 'recovery.json')
    Assert-Throws -MessagePattern 'launcher backup' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $tamperedPreStablePathRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $tamperedPreStableTypeRoot =
        Join-Path $testRoot 'pre-stable-recovery-tampered-type'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $tamperedPreStableTypeRoot `
        -Recurse
    $tamperedPreStableTypeManifest =
        Get-Content `
            -LiteralPath (Join-Path $tamperedPreStableTypeRoot 'recovery.json') `
            -Raw |
        ConvertFrom-Json
    $tamperedPreStableTypeManifest.managedTaskExisted = 'true'
    $tamperedPreStableTypeManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $tamperedPreStableTypeRoot 'recovery.json')
    Assert-Throws -MessagePattern 'JSON Boolean' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $tamperedPreStableTypeRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $unexpectedPreStableEntryRoot =
        Join-Path $testRoot 'pre-stable-recovery-unexpected-entry'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $unexpectedPreStableEntryRoot `
        -Recurse
    'unexpected' | Set-Content `
        -LiteralPath (Join-Path $unexpectedPreStableEntryRoot 'extra.bin')
    Assert-Throws -MessagePattern 'unexpected or unsafe entries' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $unexpectedPreStableEntryRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $null = & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -InitialConfigSource $initialConfig `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    Assert-True (
        (Get-TreeSignature -Root $preStableRecoveryRoot) -ceq $preStableRecoverySignature
    ) 'First-install retry replaced or changed the reusable pre-stable recovery packet.'
    $release1 = Read-LhmReleasePayload -Directory $installRoot
    Assert-True ($release1.Manifest.releaseId -ceq 'one-abcde01') 'First release was not installed.'
    Assert-True ((Get-LhmFileSha256 -Path $launcherTarget) -ceq (Get-LhmFileSha256 -Path $canonicalLauncher)) 'Canonical launcher was not installed.'
    Assert-True ((Get-LhmFileSha256 -Path $shimPath) -ceq $shimHash) 'Public shim changed.'
    $runtimePath = Join-Path $installRoot $script:LhmRuntimeConfigName
    $runtimeHash = Get-LhmFileSha256 -Path $runtimePath
    $configHash = Get-LhmFileSha256 -Path (Join-Path $dataRoot $script:LhmSettingsFileName)
    'do-not-delete' | Set-Content -LiteralPath (Join-Path $dataRoot 'logs\sentinel.csv')

    $crashInstallRoot = Join-Path $testRoot 'crash-install'
    $crashDataRoot = Join-Path $testRoot 'crash-data'
    $crashExternalRoot = Join-Path $testRoot 'crash-external-state'
    $crashLauncherTarget = Join-Path $testRoot 'crash-script-data\Start-LibreHardwareMonitor.ps1'
    $crashShimPath = Join-Path $testRoot 'crash-bin\librehw.cmd'
    $crashConfig = Join-Path $testRoot 'crash-initial.config'
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $crashLauncherTarget)) | Out-Null
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $crashShimPath)) | Out-Null
    [System.IO.Directory]::CreateDirectory($crashExternalRoot) | Out-Null
    'crash legacy launcher' | Set-Content -LiteralPath $crashLauncherTarget
    '@echo off' | Set-Content -LiteralPath $crashShimPath -Encoding ASCII
    '{"crashLegacy":true}' | Set-Content `
        -LiteralPath (Join-Path $crashExternalRoot 'managed-task.json')
    '<configuration />' | Set-Content -LiteralPath $crashConfig

    Assert-Throws -MessagePattern 'AfterHealth' -Action {
        & $installScript `
            -CandidateDirectory $candidate1 `
            -InstallRoot $crashInstallRoot `
            -DataRoot $crashDataRoot `
            -InitialConfigSource $crashConfig `
            -LauncherTargetPath $crashLauncherTarget `
            -PublicShimPath $crashShimPath `
            -TestExternalStateRoot $crashExternalRoot `
            -NonLiveTestMode `
            -TestFailurePoint AfterHealth `
            -TestSimulateCrash `
            -Confirm:$false
    }
    Assert-True (
        Test-Path -LiteralPath (Join-Path $crashInstallRoot '.release-transaction') -PathType Container
    ) 'Simulated crash did not leave the transaction journal.'
    $crashRecoveryRoot = Join-Path $crashDataRoot 'release-recovery\pre-stable-startup'
    Assert-True (
        Test-Path -LiteralPath $crashRecoveryRoot -PathType Container
    ) 'Simulated crash did not retain pre-stable recovery.'
    $crashRecoverySignature = Get-TreeSignature -Root $crashRecoveryRoot

    $null = & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $crashInstallRoot `
        -DataRoot $crashDataRoot `
        -InitialConfigSource $crashConfig `
        -LauncherTargetPath $crashLauncherTarget `
        -PublicShimPath $crashShimPath `
        -TestExternalStateRoot $crashExternalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    $crashInstalled = Read-LhmReleasePayload -Directory $crashInstallRoot
    Assert-True ($crashInstalled.Manifest.releaseId -ceq 'one-abcde01') 'Crash retry did not complete the first installation.'
    Assert-True (
        (Get-TreeSignature -Root $crashRecoveryRoot) -ceq $crashRecoverySignature
    ) 'Crash retry replaced or changed the reusable recovery packet.'
    Assert-NoTransactionDebris -InstallRoot $crashInstallRoot

    $null = & $installScript `
        -CandidateDirectory $candidate2 `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    $release2 = Read-LhmReleasePayload -Directory $installRoot
    $rollback1 = Read-LhmReleasePayload -Directory (Join-Path $installRoot 'rollback') -RequireCandidateShape
    Assert-True ($release2.Manifest.releaseId -ceq 'two-abcde02') 'Second release was not installed.'
    Assert-True ($rollback1.Manifest.releaseId -ceq 'one-abcde01') 'Exactly one prior release was not retained.'
    Assert-True ((Get-LhmFileSha256 -Path $runtimePath) -ceq $runtimeHash) 'Runtime descriptor changed during promotion.'
    Assert-True ((Get-LhmFileSha256 -Path (Join-Path $dataRoot $script:LhmSettingsFileName)) -ceq $configHash) 'Config changed during promotion.'
    Assert-True (Test-Path -LiteralPath (Join-Path $dataRoot 'logs\sentinel.csv')) 'Log sentinel was deleted.'

    $stableBaseline = Get-TreeSignature -Root $installRoot
    $externalBaseline = Get-TreeSignature -Root $externalRoot
    $launcherBaseline = Get-LhmFileSha256 -Path $launcherTarget

    $recoveryPreparation =
        Join-Path $installRoot ".release-preparing-$([guid]::NewGuid().ToString('N'))"
    [System.IO.Directory]::CreateDirectory($recoveryPreparation) | Out-Null
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $installRoot `
        -DestinationDirectory (Join-Path $recoveryPreparation 'previous-current')
    $null = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $installRoot 'rollback') `
        -DestinationDirectory (Join-Path $recoveryPreparation 'previous-rollback')
    Copy-Item `
        -LiteralPath $launcherTarget `
        -Destination (Join-Path $recoveryPreparation 'launcher-backup.ps1')
    Copy-Item `
        -LiteralPath (Join-Path $externalRoot 'managed-task.json') `
        -Destination (Join-Path $recoveryPreparation 'managed-task.test.json')
    $interruptedState = @{
        operation = 'promote'
        phase = 'candidate-installed'
        installRoot = $installRoot
        launcherTargetPath = $launcherTarget
        publicShimPath = $shimPath
        publicShimSha256 = $shimHash
        hadCurrent = $true
        hadRollback = $true
        hadLauncher = $true
        managedTaskExisted = $true
        createdAt = [DateTimeOffset]::UtcNow.ToString('o')
    }
    Write-LhmTransactionState `
        -TransactionRoot $recoveryPreparation `
        -State $interruptedState
    Move-Item `
        -LiteralPath $recoveryPreparation `
        -Destination (Join-Path $installRoot '.release-transaction')

    Clear-LhmPayloadPair -Directory $installRoot
    Clear-LhmPayloadPair -Directory (Join-Path $installRoot 'rollback')
    $null = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $installRoot '.release-transaction\previous-rollback') `
        -DestinationDirectory $installRoot `
        -DestinationMayContainOtherEntries
    $null = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $installRoot '.release-transaction\previous-current') `
        -DestinationDirectory (Join-Path $installRoot 'rollback')
    'interrupted launcher' | Set-Content -LiteralPath $launcherTarget
    '{"interrupted":true}' | Set-Content `
        -LiteralPath (Join-Path $externalRoot 'managed-task.json')

    $interruptedWhatIfBaseline = Get-TreeSignature -Root $testRoot
    & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -WhatIf
    Assert-True (
        (Get-TreeSignature -Root $testRoot) -ceq $interruptedWhatIfBaseline
    ) '-WhatIf changed an interrupted transaction or its external state.'

    $repairLauncherParent = Split-Path -Parent $launcherTarget
    $repairLauncherParentBackup = "$repairLauncherParent.normal"
    $repairLauncherJunctionTarget = Join-Path $testRoot 'repair-launcher-junction-target'
    Move-Item -LiteralPath $repairLauncherParent -Destination $repairLauncherParentBackup
    [System.IO.Directory]::CreateDirectory($repairLauncherJunctionTarget) | Out-Null
    'repair launcher junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $repairLauncherJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $repairLauncherParent `
        -Target $repairLauncherJunctionTarget
    $repairLauncherJunctionTargetSignature =
        Get-TreeSignature -Root $repairLauncherJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            $null = Repair-LhmInterruptedTransaction `
                -InstallRoot $installRoot `
                -LauncherTargetPath $launcherTarget `
                -PublicShimPath $shimPath `
                -NonLiveTestMode `
                -TestExternalStateRoot $externalRoot
        }
        Assert-True (
            (Get-TreeSignature -Root $repairLauncherJunctionTarget) -ceq
                $repairLauncherJunctionTargetSignature
        ) 'Interrupted repair wrote through a launcher-parent junction.'
        Assert-True (
            Test-Path `
                -LiteralPath (Join-Path $installRoot '.release-transaction') `
                -PathType Container
        ) 'Rejected launcher-junction repair removed its transaction journal.'
    }
    finally {
        if ([System.IO.Directory]::Exists($repairLauncherParent)) {
            [System.IO.Directory]::Delete($repairLauncherParent, $false)
        }
        Move-Item `
            -LiteralPath $repairLauncherParentBackup `
            -Destination $repairLauncherParent
    }

    $null = Repair-LhmInterruptedTransaction `
        -InstallRoot $installRoot `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -NonLiveTestMode `
        -TestExternalStateRoot $externalRoot
    Assert-True ((Get-TreeSignature -Root $installRoot) -ceq $stableBaseline) 'Interrupted transaction recovery did not restore payload state.'
    Assert-True ((Get-TreeSignature -Root $externalRoot) -ceq $externalBaseline) 'Interrupted transaction recovery did not restore task state.'
    Assert-True ((Get-LhmFileSha256 -Path $launcherTarget) -ceq $launcherBaseline) 'Interrupted transaction recovery did not restore launcher state.'
    Assert-NoTransactionDebris -InstallRoot $installRoot

    foreach ($failurePoint in @('AfterPreviousRemoval', 'AfterCandidateInstall', 'AfterTaskStage', 'AfterHealth')) {
        Assert-Throws -MessagePattern $failurePoint -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $installRoot `
                -DataRoot $dataRoot `
                -LauncherTargetPath $launcherTarget `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot $externalRoot `
                -NonLiveTestMode `
                -TestFailurePoint $failurePoint `
                -Confirm:$false
        }
        Assert-True ((Get-TreeSignature -Root $installRoot) -ceq $stableBaseline) "Promotion recovery failed at $failurePoint."
        Assert-True ((Get-TreeSignature -Root $externalRoot) -ceq $externalBaseline) "Task recovery failed at $failurePoint."
        Assert-NoTransactionDebris -InstallRoot $installRoot
    }

    $invalidCandidate = Join-Path $testRoot 'candidate-invalid'
    Copy-Item -LiteralPath $candidate1 -Destination $invalidCandidate -Recurse
    Add-Content `
        -LiteralPath (Join-Path $invalidCandidate $script:LhmExecutableName) `
        -Value 'tamper'
    Assert-Throws -MessagePattern 'hash mismatch' -Action {
        & $installScript `
            -CandidateDirectory $invalidCandidate `
            -InstallRoot $installRoot `
            -DataRoot $dataRoot `
            -LauncherTargetPath $launcherTarget `
            -PublicShimPath $shimPath `
            -TestExternalStateRoot $externalRoot `
            -NonLiveTestMode `
            -Confirm:$false
    }
    Assert-True ((Get-TreeSignature -Root $installRoot) -ceq $stableBaseline) 'Invalid candidate changed installed state.'

    'junk' | Set-Content -LiteralPath (Join-Path $installRoot 'unexpected.txt')
    Assert-Throws -MessagePattern 'unexpected entries' -Action {
        & $installScript `
            -CandidateDirectory $candidate1 `
            -InstallRoot $installRoot `
            -DataRoot $dataRoot `
            -LauncherTargetPath $launcherTarget `
            -PublicShimPath $shimPath `
            -TestExternalStateRoot $externalRoot `
            -NonLiveTestMode `
            -Confirm:$false
    }
    Remove-Item -LiteralPath (Join-Path $installRoot 'unexpected.txt')

    $null = & $rollbackScript `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    $rolledBack = Read-LhmReleasePayload -Directory $installRoot
    $postRollbackSlot = Read-LhmReleasePayload `
        -Directory (Join-Path $installRoot 'rollback') `
        -RequireCandidateShape
    Assert-True ($rolledBack.Manifest.releaseId -ceq 'one-abcde01') 'Rollback did not restore release one.'
    Assert-True ($postRollbackSlot.Manifest.releaseId -ceq 'two-abcde02') 'Rollback did not retain release two as the one slot.'

    $rollbackBaseline = Get-TreeSignature -Root $installRoot
    foreach ($failurePoint in @('AfterPreviousRemoval', 'AfterCandidateInstall', 'AfterTaskStage', 'AfterHealth')) {
        Assert-Throws -MessagePattern $failurePoint -Action {
            & $rollbackScript `
                -InstallRoot $installRoot `
                -DataRoot $dataRoot `
                -LauncherTargetPath $launcherTarget `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot $externalRoot `
                -NonLiveTestMode `
                -TestFailurePoint $failurePoint `
                -Confirm:$false
        }
        Assert-True ((Get-TreeSignature -Root $installRoot) -ceq $rollbackBaseline) "Rollback recovery failed at $failurePoint."
        Assert-NoTransactionDebris -InstallRoot $installRoot
    }

    Assert-True ((Get-LhmFileSha256 -Path $runtimePath) -ceq $runtimeHash) 'Runtime descriptor changed during rollback tests.'
    Assert-True ((Get-LhmFileSha256 -Path $shimPath) -ceq $shimHash) 'Public shim changed during tests.'
    Assert-True (Test-Path -LiteralPath (Join-Path $dataRoot 'logs\sentinel.csv')) 'Log sentinel was deleted during tests.'
    Assert-LhmInstalledRootInventory -InstallRoot $installRoot

    [pscustomobject]@{
        Result = 'PASS'
        TestRoot = $testRoot
        CurrentRelease = $rolledBack.Manifest.releaseId
        RollbackRelease = $postRollbackSlot.Manifest.releaseId
        FailureInjectionCases = 12
        HostileRecoveryManifestCases = 16
        HostileReparseCases = 12
        WindowsPowerShellLauncherCompatibility = $true
        WindowsPowerShellCleanupCompatibility = $true
        JunctionParentCleanupGuard = $true
        NestedJunctionCleanupGuard = $true
        FilesystemRootCleanupGuard = $true
        RuntimeDescriptorPreserved = $true
        PublicShimPreserved = $true
        LogsPreserved = $true
    }
}
finally {
    $env:NUGET_PACKAGES = $oldNugetPackages
    $env:DOTNET_NOLOGO = $oldDotnetNoLogo
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $oldDotnetTelemetry
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = $oldDotnetCertificate
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = Resolve-LhmFullPath -Path $testRoot
        $resolvedTempRoot = Resolve-LhmFullPath -Path ([System.IO.Path]::GetTempPath())
        if (-not (Test-LhmPathWithin -Path $resolvedTestRoot -Root $resolvedTempRoot) -or
            -not (Split-Path -Leaf $resolvedTestRoot).StartsWith('sq-librehw-release-test-', [System.StringComparison]::Ordinal)) {
            throw "Refusing to clean unexpected test root '$resolvedTestRoot'."
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}

[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ConfigPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedConfig = [System.IO.Path]::GetFullPath($ConfigPath)
if (-not [System.IO.File]::Exists($resolvedConfig)) {
    throw "Log-management configuration does not exist: $resolvedConfig"
}

$config = Get-Content -LiteralPath $resolvedConfig -Raw | ConvertFrom-Json
$expectedSchema = 'sq.lhm-log-management'
if ($config.Schema -ne $expectedSchema -or [int]$config.Version -ne 1) {
    throw 'Log-management configuration has an unsupported schema or version.'
}

$required = @('SourceDirectories', 'ArchiveRoot', 'MachineName', 'RetentionDays')
foreach ($property in $required) {
    if ($null -eq $config.$property) {
        throw "Log-management configuration is missing '$property'."
    }
}

if (@($config.SourceDirectories).Count -eq 0) {
    throw 'Log-management configuration requires at least one source directory.'
}

$retentionDays = [int]$config.RetentionDays
if ($retentionDays -lt 1 -or $retentionDays -gt 36500) {
    throw 'Log-management RetentionDays must be between 1 and 36500.'
}

$archiveScript = Join-Path $PSScriptRoot 'Archive-LhmLogs.ps1'
$cleanScript = Join-Path $PSScriptRoot 'Clean-LhmLogArchives.ps1'
$archiveResults = @(& $archiveScript -SourceDirectory @($config.SourceDirectories) -ArchiveRoot $config.ArchiveRoot -MachineName $config.MachineName)
$retentionResults = @(& $cleanScript -ArchiveRoot $config.ArchiveRoot -MachineName $config.MachineName -RetentionDays $retentionDays -Confirm:$false)

$archiveResults
$retentionResults

$failed = @($archiveResults | Where-Object { $_.Status -eq 'Failed' })
if ($failed.Count -gt 0) {
    throw "$($failed.Count) log file(s) failed archival verification. Sources were retained."
}

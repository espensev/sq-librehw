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
$schemaInfo = $config.PSObject.Properties['Schema']
$versionInfo = $config.PSObject.Properties['Version']
if ($null -eq $schemaInfo -or $schemaInfo.Value -ne $expectedSchema -or $null -eq $versionInfo -or [int]$versionInfo.Value -ne 1) {
    throw 'Log-management configuration has an unsupported schema or version.'
}

$required = @('SourceDirectories', 'ArchiveRoot', 'MachineName', 'RetentionDays')
foreach ($property in $required) {
    $propertyInfo = $config.PSObject.Properties[$property]
    if ($null -eq $propertyInfo -or $null -eq $propertyInfo.Value) {
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
$archiveResults = @(& $archiveScript -SourceDirectory @($config.SourceDirectories) -ArchiveRoot $config.ArchiveRoot -MachineName $config.MachineName -Confirm:$false)
$retentionResults = @(& $cleanScript -ArchiveRoot $config.ArchiveRoot -MachineName $config.MachineName -RetentionDays $retentionDays -Confirm:$false)

$archiveResults
$retentionResults

$problems = @()
$failed = @($archiveResults | Where-Object { $_.Status -eq 'Failed' })
if ($failed.Count -gt 0) {
    $problems += "$($failed.Count) log file(s) failed archival verification. Sources were retained."
}

$retentionFailed = @($retentionResults | Where-Object { $_.Status -eq 'Failed' })
if ($retentionFailed.Count -gt 0) {
    $problems += "$($retentionFailed.Count) retention operation(s) failed."
}

$conflicts = @($archiveResults | Where-Object { $_.Status -eq 'ArchivedConflict' })
if ($conflicts.Count -gt 0) {
    $problems += "$($conflicts.Count) log file(s) were archived under conflict names; the mismatched archives were retained for review."
}

$invalid = @($retentionResults | Where-Object { $_.Status -eq 'RetainedInvalid' })
if ($invalid.Count -gt 0) {
    $problems += "$($invalid.Count) expired archive(s) failed validation and were retained."
}

if ($problems.Count -gt 0) {
    throw ($problems -join ' ')
}

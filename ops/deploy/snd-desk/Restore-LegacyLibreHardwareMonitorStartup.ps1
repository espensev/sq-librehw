[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
$null = Assert-LhmVerifiedMachineIdentity

throw @'
This recovery entry point is RETIRED and made no changes. The accepted
2026-07-25 repository build-output cleanup removed the Release payload
referenced by the old scheduler-root task and startup shortcuts. Restoring
those definitions would create broken startup entries.

Use Restore-LibreHardwareMonitorRelease.ps1 for an installed stable rollback.
The legacy cutover packet remains historical audit evidence only. See
docs\repository-build-output-cleanup.md.
'@

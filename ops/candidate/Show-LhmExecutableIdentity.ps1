[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Path,

    [string]$ReleaseRoot,

    [string]$StackRoot,

    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmRelease.Common.ps1')
. (Join-Path $PSScriptRoot '..\live-verification\LhmLiveManifest.Common.ps1')

function Test-LhmIdentityPathWithin {
    param(
        [Parameter(Mandatory)][string]$Candidate,
        [Parameter(Mandatory)][string]$Parent
    )

    $candidatePath = Resolve-LhmReleaseFullPath -Path $Candidate
    $parentPath = Resolve-LhmReleaseFullPath -Path $Parent
    return $candidatePath.Equals(
        $parentPath,
        [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-LhmReleasePathWithin -Path $candidatePath -Parent $parentPath)
}

function Get-LhmIdentityCheckout {
    param([Parameter(Mandatory)][string]$ExecutablePath)

    $directory = [System.IO.Path]::GetDirectoryName($ExecutablePath)
    $previousErrorPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 promotes native stderr to a terminating error
        # under Stop. A path outside Git is an expected classification result.
        $ErrorActionPreference = 'Continue'
        $rootOutput = @(& git -C $directory rev-parse --show-toplevel 2>$null)
        $rootExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }
    if ($rootExitCode -ne 0 -or $rootOutput.Count -ne 1) {
        return $null
    }

    $root = Resolve-LhmReleaseFullPath -Path ([string]$rootOutput[0])
    $commitOutput = @(& git -C $root rev-parse HEAD 2>$null)
    $branchOutput = @(& git -C $root rev-parse --abbrev-ref HEAD 2>$null)
    $changes = @(& git -C $root status --porcelain=v1 --untracked-files=no 2>$null)
    if ($LASTEXITCODE -ne 0 -or
        $commitOutput.Count -ne 1 -or
        $branchOutput.Count -ne 1) {
        throw "Unable to inspect the Git checkout containing '$ExecutablePath'."
    }

    return [ordered]@{
        root = $root
        commit = ([string]$commitOutput[0]).Trim()
        branch = ([string]$branchOutput[0]).Trim()
        trackedDirty = $changes.Count -gt 0
    }
}

function Get-LhmIdentityCandidateMatches {
    param(
        [Parameter(Mandatory)][string]$ExecutableSha256,
        [string]$CandidateReleaseRoot
    )

    $matches = [System.Collections.Generic.List[object]]::new()
    $diagnostics = [System.Collections.Generic.List[object]]::new()
    if ([string]::IsNullOrWhiteSpace($CandidateReleaseRoot)) {
        return [pscustomobject][ordered]@{
            matches = [object[]]@()
            diagnostics = [object[]]@()
        }
    }

    $root = Resolve-LhmReleaseFullPath -Path $CandidateReleaseRoot
    Assert-LhmReleasePathHasNoReparseAncestor -Path $root
    $candidateRoot = Join-Path $root 'candidates'
    if (-not [System.IO.Directory]::Exists($candidateRoot)) {
        return [pscustomobject][ordered]@{
            matches = [object[]]@()
            diagnostics = [object[]]@()
        }
    }

    foreach ($directory in @(Get-ChildItem -LiteralPath $candidateRoot -Directory -Force)) {
        try {
            Assert-LhmReleasePathHasNoReparseAncestor -Path $directory.FullName
            $manifestPath = Join-Path $directory.FullName 'release-manifest.json'
            if (-not [System.IO.File]::Exists($manifestPath)) {
                continue
            }
            $manifestItem = Get-Item -LiteralPath $manifestPath -Force
            if ($manifestItem.Length -gt 4MB) {
                throw "Candidate manifest exceeds the 4 MiB identity limit: $manifestPath"
            }

            $manifest = ConvertFrom-LhmReleaseJson -Json (
                Get-Content -Raw -LiteralPath $manifestPath)
            foreach ($package in @($manifest.packages)) {
                $entryPoint = [string]$package.entryPoint
                $entryRecords = @($package.files | Where-Object {
                    [string]$_.path -ceq $entryPoint
                })
                if ($entryRecords.Count -ne 1 -or
                    ([string]$entryRecords[0].sha256).ToLowerInvariant() -cne $ExecutableSha256) {
                    continue
                }

                $validationStatus = 'INVALID'
                $validationError = $null
                try {
                    $validation = Test-LhmReleaseCandidate -CandidatePath $directory.FullName
                    $validationStatus = [string]$validation.Status
                }
                catch {
                    $validationError = $_.Exception.Message
                }

                $matches.Add([ordered]@{
                    releaseId = [string]$manifest.releaseId
                    targetFramework = [string]$package.targetFramework
                    sourceCommit = [string]$manifest.source.commit
                    promotable = [bool]$manifest.promotable
                    archive = [string]$package.archive
                    validationStatus = $validationStatus
                    validationError = $validationError
                })
            }
        }
        catch {
            $diagnostics.Add([ordered]@{
                candidatePath = Resolve-LhmReleaseFullPath -Path $directory.FullName
                error = $_.Exception.Message
            })
        }
    }

    return [pscustomobject][ordered]@{
        matches = [object[]]@($matches)
        diagnostics = [object[]]@($diagnostics)
    }
}

function Get-LhmIdentityRollbackEvidence {
    param(
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][string]$ExecutableSha256,
        [Parameter(Mandatory)][string]$ProductVersion,
        [Parameter(Mandatory)][string]$RollbackRoot,
        [Parameter(Mandatory)][string]$ExpectedMachineId
    )

    $rollbackPath = Resolve-LhmReleaseFullPath -Path $RollbackRoot
    $relativePath = $ExecutablePath.Substring(
        (Get-LhmReleasePathPrefix -Path $rollbackPath).Length)
    $segments = @($relativePath -split '[\\/]')
    if ($segments.Count -lt 2 -or [string]::IsNullOrWhiteSpace($segments[0])) {
        throw "Rollback executable is not inside a packet directory: $ExecutablePath"
    }

    $packetRoot = Resolve-LhmReleaseFullPath -Path (Join-Path $rollbackPath $segments[0])
    Assert-LhmReleasePathHasNoReparseAncestor -Path $packetRoot
    $manifestPath = Join-Path $packetRoot 'packet-manifest.json'
    if (-not [System.IO.File]::Exists($manifestPath)) {
        throw "Rollback packet manifest does not exist: $manifestPath"
    }
    if ((Get-Item -LiteralPath $manifestPath -Force).Length -gt 4MB) {
        throw "Rollback packet manifest exceeds the 4 MiB identity limit: $manifestPath"
    }

    $manifest = ConvertFrom-LhmReleaseJson -Json (
        Get-Content -Raw -LiteralPath $manifestPath)
    if ([string]$manifest.Schema -cne 'sq.librehw-rollback-packet') {
        throw "Rollback packet schema is '$($manifest.Schema)'; expected 'sq.librehw-rollback-packet'."
    }
    if ([string]$manifest.Version -cne '1') {
        throw "Rollback packet version is '$($manifest.Version)'; expected '1'."
    }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.MachineId) -or
        [string]$manifest.MachineId -cne $ExpectedMachineId) {
        throw "Rollback packet machineId '$($manifest.MachineId)' does not match stack machineId '$ExpectedMachineId'."
    }

    Assert-LhmDirectoryTreeHasNoReparsePoints -Path $packetRoot
    $fileRecords = @($manifest.Files)
    if ([int]$manifest.FileCount -ne $fileRecords.Count) {
        throw "Rollback packet FileCount '$($manifest.FileCount)' does not match its $($fileRecords.Count) file record(s)."
    }

    $recordPaths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $targetRecords = [System.Collections.Generic.List[object]]::new()
    foreach ($record in $fileRecords) {
        $recordRelativePath = [string]$record.RelativePath
        if ([string]::IsNullOrWhiteSpace($recordRelativePath) -or
            [System.IO.Path]::IsPathRooted($recordRelativePath)) {
            throw "Rollback packet contains an invalid relative path: '$recordRelativePath'."
        }

        $recordPath = Resolve-LhmReleaseFullPath -Path (
            Join-Path $packetRoot ($recordRelativePath.Replace('/', '\')))
        if (-not (Test-LhmIdentityPathWithin -Candidate $recordPath -Parent $packetRoot) -or
            $recordPath.Equals($packetRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Rollback packet record escapes its packet root: '$recordRelativePath'."
        }
        if (-not $recordPaths.Add($recordPath)) {
            throw "Rollback packet contains a duplicate or case-colliding file record: '$recordRelativePath'."
        }
        if (-not [System.IO.File]::Exists($recordPath)) {
            throw "Rollback packet file does not exist: $recordPath"
        }
        $recordItem = Get-Item -LiteralPath $recordPath -Force
        if ([long]$record.Length -ne $recordItem.Length) {
            throw "Rollback packet length mismatch for '$recordRelativePath'."
        }
        $recordSha256 = Get-LhmReleaseFileSha256 -Path $recordPath
        if ($recordSha256 -cne ([string]$record.Sha256).ToLowerInvariant()) {
            throw "Rollback packet SHA-256 mismatch for '$recordRelativePath'."
        }
        if ($recordPath.Equals($ExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $targetRecords.Add($record)
        }
    }

    $manifestFullPath = Resolve-LhmReleaseFullPath -Path $manifestPath
    $actualPayloadFiles = @(
        Get-ChildItem -LiteralPath $packetRoot -File -Recurse -Force |
            Where-Object {
                -not (Resolve-LhmReleaseFullPath -Path $_.FullName).Equals(
                    $manifestFullPath,
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
    )
    foreach ($actualFile in $actualPayloadFiles) {
        $actualPath = Resolve-LhmReleaseFullPath -Path $actualFile.FullName
        if (-not $recordPaths.Contains($actualPath)) {
            $actualRelativePath = $actualPath.Substring(
                (Get-LhmReleasePathPrefix -Path $packetRoot).Length)
            throw "Rollback packet contains an undeclared file: '$actualRelativePath'."
        }
    }
    if ($actualPayloadFiles.Count -ne $recordPaths.Count) {
        throw (
            "Rollback packet manifest declares $($recordPaths.Count) unique file(s), " +
            "but the packet contains $($actualPayloadFiles.Count) payload file(s)."
        )
    }

    if ($targetRecords.Count -ne 1) {
        throw "Rollback packet must contain exactly one record for executable '$ExecutablePath'; found $($targetRecords.Count)."
    }
    if ($ExecutableSha256 -cne ([string]$manifest.RestoresToEntryPointSha256).ToLowerInvariant()) {
        throw 'Rollback packet RestoresToEntryPointSha256 does not match the executable SHA-256.'
    }
    if ($ProductVersion -cne [string]$manifest.RestoresToProductVersion) {
        throw 'Rollback packet RestoresToProductVersion does not match the executable product version.'
    }

    return [pscustomobject][ordered]@{
        packetRoot = $packetRoot
        manifestPath = Resolve-LhmReleaseFullPath -Path $manifestPath
        operationId = [string]$manifest.OperationId
    }
}

$executablePath = Resolve-LhmReleaseFullPath -Path $Path
Assert-LhmReleasePathHasNoReparseAncestor -Path $executablePath
if (-not [System.IO.File]::Exists($executablePath)) {
    throw "Executable does not exist: $executablePath"
}
if ([System.IO.Path]::GetFileName($executablePath) -cne
    'LibreHardwareMonitor.Windows.Forms.exe') {
    throw "Expected LibreHardwareMonitor.Windows.Forms.exe, got: $executablePath"
}

if ([string]::IsNullOrWhiteSpace($ReleaseRoot) -and
    -not [string]::IsNullOrWhiteSpace($StackRoot)) {
    $ReleaseRoot = Join-Path $StackRoot 'releases'
}

$sha256 = Get-LhmReleaseFileSha256 -Path $executablePath
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
$fileVersion = [string]$versionInfo.FileVersion
$productVersion = [string]$versionInfo.ProductVersion
$checkout = Get-LhmIdentityCheckout -ExecutablePath $executablePath
$candidateScanResult = Get-LhmIdentityCandidateMatches `
    -ExecutableSha256 $sha256 `
    -CandidateReleaseRoot $ReleaseRoot
$candidateMatches = @($candidateScanResult.matches)
$candidateDiagnostics = @($candidateScanResult.diagnostics)

$buildCommit = $null
$buildDirty = $null
$versionMatch = [System.Text.RegularExpressions.Regex]::Match(
    $productVersion,
    '\+(?<commit>[0-9a-fA-F]{7}|nogit)(?<dirty>-dirty)?(?:\.|$)')
if ($versionMatch.Success) {
    $buildCommit = $versionMatch.Groups['commit'].Value.ToLowerInvariant()
    $buildDirty = $versionMatch.Groups['dirty'].Success
}

$matchesCheckoutCommit = $null
if ($null -ne $checkout -and
    -not [string]::IsNullOrWhiteSpace($buildCommit) -and
    $buildCommit -cne 'nogit') {
    $matchesCheckoutCommit = ([string]$checkout.commit).StartsWith(
        $buildCommit,
        [System.StringComparison]::OrdinalIgnoreCase)
}

$role = 'unknown'
$authority = 'none'
$verifiedCandidateMatches = @($candidateMatches | Where-Object {
    $_.validationStatus -ceq 'VERIFIED'
})
if ($verifiedCandidateMatches.Count -gt 0) {
    $role = 'candidate-payload'
    $authority = 'candidate-manifest'
}
elseif ($candidateMatches.Count -gt 0) {
    $role = 'candidate-payload-invalid'
    $authority = 'none'
}

if ($null -ne $checkout) {
    $relative = $executablePath.Substring(([string]$checkout.root).Length).TrimStart(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
    if ($relative -match '^LibreHardwareMonitor\.Tests/.+/(?:bin|obj)/') {
        $role = 'test-copy'
        $authority = 'none'
    }
    elseif ($relative -match '(^|/)obj/') {
        $role = 'intermediate-output'
        $authority = 'none'
    }
    elseif ($relative -match '^bin/') {
        $role = 'development-build'
        $authority = 'none'
    }
    elseif ($role -eq 'unknown') {
        $role = 'checkout-copy'
    }
}

$liveManifestMatch = $false
$sourceCandidate = $null
$liveEvidence = [ordered]@{
    status = 'not-requested'
    manifestPath = $null
    error = $null
}
$rollbackEvidence = [ordered]@{
    status = 'not-applicable'
    packetRoot = $null
    manifestPath = $null
    operationId = $null
    error = $null
}
if (-not [string]::IsNullOrWhiteSpace($StackRoot)) {
    $stack = Resolve-LhmReleaseFullPath -Path $StackRoot
    Assert-LhmReleasePathHasNoReparseAncestor -Path $stack
    $expectedLivePath = Resolve-LhmReleaseFullPath -Path (
        Join-Path $stack 'deployments\current\LibreHardwareMonitor.Windows.Forms.exe')
    $liveManifestPath = Join-Path $stack 'manifests\channels\live.json'
    $liveEvidence.manifestPath = Resolve-LhmReleaseFullPath -Path $liveManifestPath
    $liveContract = $null
    try {
        $liveContract = Get-LhmLiveManifestContract `
            -StackRoot $stack `
            -RequireCurrentComputer
        $liveEvidence.status = 'not-applicable'
        if ($executablePath.Equals(
            $liveContract.EntryTarget,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            $sourceCandidate = [string]$liveContract.Live.sourceCandidate
            $liveManifestMatch = $sha256 -ceq ([string]$liveContract.Live.sha256).ToLowerInvariant() -and
                $productVersion -ceq [string]$liveContract.Live.productVersion
            $role = if ($liveManifestMatch) { 'live' } else { 'live-mismatch' }
            $authority = if ($liveManifestMatch) { 'live-manifest' } else { 'none' }
            $liveEvidence.status = if ($liveManifestMatch) { 'verified' } else { 'mismatch' }
            if (-not $liveManifestMatch) {
                $liveEvidence.error = 'Executable SHA-256 or product version does not match the valid live manifest.'
            }
        }
        elseif ($executablePath.Equals(
            $expectedLivePath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            $role = 'live-mismatch'
            $authority = 'none'
            $liveEvidence.status = 'mismatch'
            $liveEvidence.error = 'The canonical live executable path is not the entry target declared by the valid stack manifests.'
        }
    }
    catch {
        $liveEvidence.status = 'invalid'
        $liveEvidence.error = $_.Exception.Message
        if ($executablePath.Equals(
            $expectedLivePath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            $role = 'live-mismatch'
            $authority = 'none'
        }
    }

    if ($role -notin @('live', 'live-mismatch')) {
        $rollbackRoot = Join-Path $stack 'deployments\rollback'
        if ([System.IO.Directory]::Exists($rollbackRoot) -and
            (Test-LhmIdentityPathWithin -Candidate $executablePath -Parent $rollbackRoot)) {
            $role = 'rollback-payload-invalid'
            $authority = 'none'
            try {
                if ($null -eq $liveContract) {
                    throw 'Rollback packet authority requires a valid machine-bound stack manifest contract.'
                }
                $verifiedRollback = Get-LhmIdentityRollbackEvidence `
                    -ExecutablePath $executablePath `
                    -ExecutableSha256 $sha256 `
                    -ProductVersion $productVersion `
                    -RollbackRoot $rollbackRoot `
                    -ExpectedMachineId $liveContract.MachineId
                $role = 'rollback-payload'
                $authority = 'rollback-packet'
                $rollbackEvidence.status = 'verified'
                $rollbackEvidence.packetRoot = $verifiedRollback.packetRoot
                $rollbackEvidence.manifestPath = $verifiedRollback.manifestPath
                $rollbackEvidence.operationId = $verifiedRollback.operationId
            }
            catch {
                $rollbackEvidence.status = 'invalid'
                $rollbackEvidence.error = $_.Exception.Message
            }
        }
    }
}

$processIds = @()
$processQuery = [ordered]@{
    status = 'complete'
    error = $null
}
try {
    $processIds = @(
        Get-CimInstance Win32_Process -Property ProcessId, ExecutablePath -ErrorAction Stop |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
                (Resolve-LhmReleaseFullPath -Path ([string]$_.ExecutablePath)).Equals(
                    $executablePath,
                    [System.StringComparison]::OrdinalIgnoreCase)
            } |
            ForEach-Object { [int]$_.ProcessId }
    )
}
catch {
    $processIds = @()
    $processQuery.status = 'failed'
    $processQuery.error = $_.Exception.Message
}

$identityStatus = 'IDENTIFIED'
if ($candidateDiagnostics.Count -gt 0 -or
    $liveEvidence.status -ceq 'invalid' -or
    $rollbackEvidence.status -ceq 'invalid' -or
    $processQuery.status -ceq 'failed') {
    $identityStatus = 'INCOMPLETE'
}

$result = [ordered]@{
    schema = 'sq.lhm-executable-identity'
    version = 2
    status = $identityStatus
    path = $executablePath
    role = $role
    authority = $authority
    sha256 = $sha256
    fileVersion = $fileVersion
    productVersion = $productVersion
    buildCommit = $buildCommit
    buildDirty = $buildDirty
    checkout = $checkout
    matchesCheckoutCommit = $matchesCheckoutCommit
    candidateMatches = [object[]]@($candidateMatches)
    candidateScan = [ordered]@{
        status = if ($candidateDiagnostics.Count -eq 0) { 'complete' } else { 'incomplete' }
        diagnostics = [object[]]@($candidateDiagnostics)
    }
    liveManifestMatch = $liveManifestMatch
    sourceCandidate = $sourceCandidate
    liveEvidence = $liveEvidence
    rollbackEvidence = $rollbackEvidence
    processQuery = $processQuery
    runningProcessIds = [int[]]@($processIds)
}

if ($Json) {
    $result | ConvertTo-Json -Depth 10
}
else {
    [pscustomobject]$result
}

Set-StrictMode -Version Latest

function Resolve-LhmFullPath {
    param([Parameter(Mandatory)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Get-LhmSafeMachineName {
    param([Parameter(Mandatory)][string]$MachineName)

    $safe = $MachineName.Trim()
    foreach ($character in [System.IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace([string]$character, '_')
    }

    $safe = $safe.Replace('/', '_').Replace('\', '_')
    $safe = $safe -replace '^[. ]+|[. ]+$', ''
    if ([string]::IsNullOrWhiteSpace($safe)) {
        throw 'MachineName must contain at least one filesystem-safe character.'
    }

    return $safe
}

function Resolve-LhmChildPath {
    param(
        [Parameter(Mandatory)][string]$Parent,
        [Parameter(Mandatory)][string]$Child
    )

    $parentPath = Resolve-LhmFullPath -Path $Parent
    $candidate = Resolve-LhmFullPath -Path (Join-Path $parentPath $Child)
    $prefix = $parentPath
    if (-not $prefix.EndsWith([string][System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::Ordinal)) {
        $prefix += [System.IO.Path]::DirectorySeparatorChar
    }

    if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved child path escapes its parent: $candidate"
    }

    return $candidate
}

function Get-LhmStreamSha256 {
    param([Parameter(Mandatory)][System.IO.Stream]$Stream)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha256.ComputeHash($Stream)
        return ([System.BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-LhmFileSha256 {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::Open($Path,
                                    [System.IO.FileMode]::Open,
                                    [System.IO.FileAccess]::Read,
                                    [System.IO.FileShare]::Read)
    try {
        return Get-LhmStreamSha256 -Stream $stream
    }
    finally {
        $stream.Dispose()
    }
}

function Test-LhmFileReady {
    param([Parameter(Mandatory)][string]$Path)

    try {
        $stream = [System.IO.File]::Open($Path,
                                        [System.IO.FileMode]::Open,
                                        [System.IO.FileAccess]::Read,
                                        [System.IO.FileShare]::None)
        $stream.Dispose()
        return $true
    }
    catch [System.IO.IOException] {
        return $false
    }
    catch [System.UnauthorizedAccessException] {
        return $false
    }
}

function Test-LhmZipArchive {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$ExpectedEntryName,
        [Nullable[long]]$ExpectedLength,
        [string]$ExpectedHash
    )

    try {
        Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
        try {
            if ($archive.Entries.Count -ne 1) {
                return [pscustomobject]@{ Valid = $false; Reason = 'entry-count'; EntryName = $null; Length = $null; Hash = $null }
            }

            $entry = $archive.Entries[0]
            if ($ExpectedEntryName -and $entry.FullName -cne $ExpectedEntryName) {
                return [pscustomobject]@{ Valid = $false; Reason = 'entry-name'; EntryName = $entry.FullName; Length = $entry.Length; Hash = $null }
            }

            if ($null -ne $ExpectedLength -and $entry.Length -ne [long]$ExpectedLength) {
                return [pscustomobject]@{ Valid = $false; Reason = 'entry-length'; EntryName = $entry.FullName; Length = $entry.Length; Hash = $null }
            }

            $entryStream = $entry.Open()
            try {
                $hash = Get-LhmStreamSha256 -Stream $entryStream
            }
            finally {
                $entryStream.Dispose()
            }

            if ($ExpectedHash -and $hash -cne $ExpectedHash.ToLowerInvariant()) {
                return [pscustomobject]@{ Valid = $false; Reason = 'entry-hash'; EntryName = $entry.FullName; Length = $entry.Length; Hash = $hash }
            }

            return [pscustomobject]@{ Valid = $true; Reason = 'verified'; EntryName = $entry.FullName; Length = $entry.Length; Hash = $hash }
        }
        finally {
            $archive.Dispose()
        }
    }
    catch {
        return [pscustomobject]@{ Valid = $false; Reason = ('unreadable: ' + $_.Exception.Message); EntryName = $null; Length = $null; Hash = $null }
    }
}

function New-LhmLogResult {
    param(
        [Parameter(Mandatory)][string]$Action,
        [Parameter(Mandatory)][string]$Status,
        [string]$Source,
        [string]$Destination,
        [string]$Message
    )

    return [pscustomobject]@{
        Action = $Action
        Status = $Status
        Source = $Source
        Destination = $Destination
        Message = $Message
    }
}

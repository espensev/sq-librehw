Set-StrictMode -Version Latest

function Resolve-LhmReleaseFullPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'A non-empty filesystem path is required.'
    }

    return [System.IO.Path]::GetFullPath($Path)
}

function Get-LhmReleasePathPrefix {
    param([Parameter(Mandatory)][string]$Path)

    return (Resolve-LhmReleaseFullPath -Path $Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
}

function Test-LhmReleasePathWithin {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $fullPath = Resolve-LhmReleaseFullPath -Path $Path
    $fullParent = Resolve-LhmReleaseFullPath -Path $Parent
    if ($fullPath.Equals($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    return $fullPath.StartsWith(
        (Get-LhmReleasePathPrefix -Path $fullParent),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-LhmReleasePathHasNoReparseAncestor {
    param([Parameter(Mandatory)][string]$Path)

    $current = Resolve-LhmReleaseFullPath -Path $Path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if ([System.IO.File]::Exists($current) -or [System.IO.Directory]::Exists($current)) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release paths cannot traverse reparse points: $current"
            }
        }

        $trimmed = $current.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
        $parent = [System.IO.Path]::GetDirectoryName($trimmed)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            $parent.Equals($current, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $current = $parent
    }
}

function Assert-LhmDirectoryTreeHasNoReparsePoints {
    param([Parameter(Mandatory)][string]$Path)

    $root = Resolve-LhmReleaseFullPath -Path $Path
    if (-not [System.IO.Directory]::Exists($root)) {
        return
    }

    Assert-LhmReleasePathHasNoReparseAncestor -Path $root
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entryPath in [System.IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $entry = Get-Item -LiteralPath $entryPath -Force
            if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release cleanup cannot traverse reparse points: $entryPath"
            }
            if (($entry.Attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
                $pending.Push($entry.FullName)
            }
        }
    }
}

function Remove-LhmOwnedReleaseDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $target = Resolve-LhmReleaseFullPath -Path $Path
    $parentPath = Resolve-LhmReleaseFullPath -Path $Parent
    if (-not (Test-LhmReleasePathWithin -Path $target -Parent $parentPath) -or
        $target.Equals($parentPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean owned release directory outside its parent: $target"
    }
    if ([System.IO.File]::Exists($target)) {
        throw "Owned release cleanup target is unexpectedly a file: $target"
    }
    if (-not [System.IO.Directory]::Exists($target)) {
        return
    }

    Assert-LhmReleasePathHasNoReparseAncestor -Path $parentPath
    Assert-LhmDirectoryTreeHasNoReparsePoints -Path $target
    Remove-Item -LiteralPath $target -Recurse -Force
    if ([System.IO.Directory]::Exists($target)) {
        throw "Owned release cleanup did not remove: $target"
    }
}

function Assert-LhmDisjointReleaseRoot {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$ReleaseRoot
    )

    $repositoryPath = Resolve-LhmReleaseFullPath -Path $RepositoryRoot
    $releasePath = Resolve-LhmReleaseFullPath -Path $ReleaseRoot

    if ((Test-LhmReleasePathWithin -Path $releasePath -Parent $repositoryPath) -or
        (Test-LhmReleasePathWithin -Path $repositoryPath -Parent $releasePath)) {
        throw "Release root and repository must be disjoint. Repository='$repositoryPath', ReleaseRoot='$releasePath'."
    }

    Assert-LhmReleasePathHasNoReparseAncestor -Path $repositoryPath
    Assert-LhmReleasePathHasNoReparseAncestor -Path $releasePath
    return $releasePath
}

function Get-LhmDefaultReleaseRoot {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $repositoryPath = Resolve-LhmReleaseFullPath -Path $RepositoryRoot
    $parent = [System.IO.Path]::GetDirectoryName($repositoryPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar))
    $name = [System.IO.Path]::GetFileName($repositoryPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar))
    return Resolve-LhmReleaseFullPath -Path (Join-Path $parent ($name + '-releases'))
}

function Assert-LhmSafeReleaseId {
    param([Parameter(Mandatory)][string]$ReleaseId)

    if ($ReleaseId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
        throw "ReleaseId must be 1-128 filesystem-safe characters: $ReleaseId"
    }
}

function ConvertTo-LhmSafeRepositoryIdentifier {
    param([AllowNull()][string]$RemoteUrl)

    if ([string]::IsNullOrWhiteSpace($RemoteUrl)) {
        return $null
    }

    $value = $RemoteUrl.Trim()
    $uri = $null
    if ([System.Uri]::TryCreate($value, [System.UriKind]::Absolute, [ref]$uri)) {
        if ($uri.IsFile -or [string]::IsNullOrWhiteSpace($uri.Host)) {
            return $null
        }

        $authority = $uri.DnsSafeHost.ToLowerInvariant()
        if (-not $uri.IsDefaultPort) {
            $authority += ':' + [string]$uri.Port
        }
        $path = $uri.AbsolutePath.Trim('/').Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($path)) {
            return $authority
        }
        return $authority + '/' + $path
    }

    $scpMatch = [System.Text.RegularExpressions.Regex]::Match(
        $value,
        '^(?:[^@/:]+@)?(?<host>[^:/?#]+):(?<path>[^?#]+)$')
    if ($scpMatch.Success) {
        $hostName = $scpMatch.Groups['host'].Value.ToLowerInvariant()
        $repositoryPath = $scpMatch.Groups['path'].Value.Trim('/').Replace('\', '/')
        if (-not [string]::IsNullOrWhiteSpace($repositoryPath)) {
            return $hostName + '/' + $repositoryPath
        }
    }

    return $null
}

function Get-LhmReleaseVerificationCommands {
    return [string[]]@(
        'git diff --check',
        'node --check console.js',
        'node --check workspace.js',
        'node webtests/selftest.node.js',
        'node --test console.tests.js workspace.tests.js',
        'dotnet test LibreHardwareMonitor.Tests (x64)')
}

function ConvertFrom-LhmReleaseJson {
    param([Parameter(Mandatory)][string]$Json)

    $convertCommand = Get-Command -Name ConvertFrom-Json -CommandType Cmdlet
    if ($convertCommand.Parameters.ContainsKey('DateKind')) {
        return $Json | ConvertFrom-Json -DateKind String
    }
    return $Json | ConvertFrom-Json
}

function Invoke-LhmReleaseCommand {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [string]$Description = $FilePath
    )

    Write-Host "==> $Description"
    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $FilePath @ArgumentList
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "$Description failed with exit code $exitCode."
        }
    }
    finally {
        Pop-Location
    }
}

function Get-LhmRepositoryBuildOutputPaths {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $repositoryPath = Resolve-LhmReleaseFullPath -Path $RepositoryRoot
    $paths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    [void]$paths.Add((Join-Path $repositoryPath 'bin'))

    $insideWorkTree = @(& git -C $repositoryPath rev-parse --is-inside-work-tree 2>$null)
    $isGitCheckout = $LASTEXITCODE -eq 0 -and
        $insideWorkTree.Count -eq 1 -and
        ([string]$insideWorkTree[0]).Trim() -ceq 'true'

    if (-not $isGitCheckout) {
        throw "Build-output discovery requires a valid Git checkout: $repositoryPath"
    }

    $trackedProjects = @(
        & git -C $repositoryPath -c core.quotePath=false ls-files -- '*.csproj' 2>$null
    )
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate tracked project files for build-output cleanup.'
    }

    foreach ($relativeProject in $trackedProjects) {
        $canonical = ([string]$relativeProject).Replace('\', '/')
        Assert-LhmSafeManifestPath -Path $canonical
        $projectPath = Resolve-LhmReleaseFullPath -Path (
            Join-Path $repositoryPath ($canonical.Replace('/', '\')))
        if (-not (Test-LhmReleasePathWithin -Path $projectPath -Parent $repositoryPath)) {
            throw "Tracked project path escapes the repository: $canonical"
        }
        $projectDirectory = [System.IO.Path]::GetDirectoryName($projectPath)
        [void]$paths.Add((Join-Path $projectDirectory 'bin'))
        [void]$paths.Add((Join-Path $projectDirectory 'obj'))
    }

    return @($paths | ForEach-Object { Resolve-LhmReleaseFullPath -Path $_ } | Sort-Object)
}

function Assert-LhmRepositoryBuildOutputPath {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Path
    )

    $repositoryPath = Resolve-LhmReleaseFullPath -Path $RepositoryRoot
    $candidate = Resolve-LhmReleaseFullPath -Path $Path
    if (-not (Test-LhmReleasePathWithin -Path $candidate -Parent $repositoryPath) -or
        $candidate.Equals($repositoryPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Build-output cleanup target escapes the repository: $candidate"
    }

    $leaf = [System.IO.Path]::GetFileName($candidate.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar))
    if ($leaf -notin @('bin', 'obj')) {
        throw "Build-output cleanup target is not an exact bin/obj directory: $candidate"
    }

    Assert-LhmReleasePathHasNoReparseAncestor -Path $candidate
    if ([System.IO.Directory]::Exists($candidate)) {
        $item = Get-Item -LiteralPath $candidate -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to clean reparse-backed build output: $candidate"
        }
    }

    return $candidate
}

function Get-LhmDirectorySummary {
    param([Parameter(Mandatory)][string]$Path)

    $files = @()
    if ([System.IO.Directory]::Exists($Path)) {
        $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse -Force)
    }

    $bytes = 0L
    if ($files.Count -gt 0) {
        $bytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
    }

    return [pscustomobject]@{
        Path = Resolve-LhmReleaseFullPath -Path $Path
        Files = $files.Count
        Bytes = $bytes
    }
}

function Clear-LhmRepositoryBuildOutput {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $repositoryPath = Resolve-LhmReleaseFullPath -Path $RepositoryRoot
    $targets = @(
        Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $repositoryPath |
            ForEach-Object {
                Assert-LhmRepositoryBuildOutputPath -RepositoryRoot $repositoryPath -Path $_
            }
    )

    $repositoryPrefix = Get-LhmReleasePathPrefix -Path $repositoryPath
    $trackedRepositoryFiles = @(
        & git -C $repositoryPath -c core.quotePath=false ls-files
    )
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate tracked files before build-output cleanup.'
    }
    foreach ($target in $targets) {
        $relative = $target.Substring($repositoryPrefix.Length).Replace('\', '/').TrimEnd('/') + '/'
        $trackedChildren = @(
            $trackedRepositoryFiles |
                Where-Object {
                    ([string]$_).StartsWith(
                        $relative,
                        [System.StringComparison]::OrdinalIgnoreCase)
                }
        )
        if ($trackedChildren.Count -gt 0) {
            throw "Refusing to clean build output containing tracked files: $($trackedChildren -join ', ')"
        }

        & git -C $repositoryPath check-ignore -q -- $relative
        if ($LASTEXITCODE -ne 0) {
            throw "Refusing to clean build output that Git does not ignore: $relative"
        }
    }

    $existingTargets = @($targets | Where-Object { [System.IO.Directory]::Exists($_) })
    if ($existingTargets.Count -eq 0) {
        return @()
    }

    $loadedFromTargets = @()
    foreach ($process in Get-Process) {
        try {
            $processPath = $process.Path
            if ([string]::IsNullOrWhiteSpace($processPath)) {
                continue
            }

            foreach ($target in $existingTargets) {
                if (Test-LhmReleasePathWithin -Path $processPath -Parent $target) {
                    $loadedFromTargets += "$($process.ProcessName)[$($process.Id)] -> $processPath"
                }
            }
        }
        catch {
            # Protected processes may not expose Path to a non-elevated caller.
        }
    }

    if ($loadedFromTargets.Count -gt 0) {
        throw "Refusing to clean build output loaded by a process: $($loadedFromTargets -join '; ')"
    }

    foreach ($target in $existingTargets) {
        Assert-LhmDirectoryTreeHasNoReparsePoints -Path $target
    }
    $summaries = @($existingTargets | ForEach-Object { Get-LhmDirectorySummary -Path $_ })
    foreach ($target in $existingTargets) {
        $resolvedTarget = Assert-LhmRepositoryBuildOutputPath -RepositoryRoot $repositoryPath -Path $target
        Assert-LhmDirectoryTreeHasNoReparsePoints -Path $resolvedTarget
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
        if ([System.IO.Directory]::Exists($resolvedTarget)) {
            throw "Build-output cleanup did not remove: $resolvedTarget"
        }
    }

    return $summaries
}

function Assert-LhmRepositoryBuildOutputEmpty {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $remaining = @(
        Get-LhmRepositoryBuildOutputPaths -RepositoryRoot $RepositoryRoot |
            Where-Object { [System.IO.Directory]::Exists($_) }
    )
    if ($remaining.Count -gt 0) {
        throw "Repository build output was recreated: $($remaining -join ', ')"
    }
}

function Get-LhmReleaseStreamSha256 {
    param([Parameter(Mandatory)][System.IO.Stream]$Stream)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($Stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-LhmReleaseFileSha256 {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        return Get-LhmReleaseStreamSha256 -Stream $stream
    }
    finally {
        $stream.Dispose()
    }
}

function Get-LhmGitSourceFingerprint {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $repositoryPath = Resolve-LhmReleaseFullPath -Path $RepositoryRoot
    $trackedAndUntracked = @(
        & git -C $repositoryPath -c core.quotePath=false ls-files --cached --others --exclude-standard
    )
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate repository source files for fingerprinting.'
    }

    $paths = [string[]]@($trackedAndUntracked | ForEach-Object { [string]$_ })
    [System.Array]::Sort($paths, [System.StringComparer]::Ordinal)
    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $paths) {
        $canonical = $relativePath.Replace('\', '/')
        Assert-LhmSafeManifestPath -Path $canonical
        $fullPath = Resolve-LhmReleaseFullPath -Path (
            Join-Path $repositoryPath ($canonical.Replace('/', '\')))
        if (-not (Test-LhmReleasePathWithin -Path $fullPath -Parent $repositoryPath)) {
            throw "Repository source path escapes the checkout: $canonical"
        }

        if ([System.IO.File]::Exists($fullPath)) {
            $item = Get-Item -LiteralPath $fullPath -Force
            $records.Add(
                $canonical + "`0" +
                [string][long]$item.Length + "`0" +
                (Get-LhmReleaseFileSha256 -Path $fullPath))
        }
        else {
            $records.Add($canonical + "`0MISSING")
        }
    }

    $payload = [System.Text.Encoding]::UTF8.GetBytes(
        [string]::Join("`n", $records))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fingerprint = ([System.BitConverter]::ToString(
            $sha256.ComputeHash($payload))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }

    return [pscustomobject]@{
        Sha256 = $fingerprint
        FileCount = $paths.Count
    }
}

function ConvertTo-LhmReleaseRelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    $rootPath = Resolve-LhmReleaseFullPath -Path $Root
    $fullPath = Resolve-LhmReleaseFullPath -Path $Path
    if (-not (Test-LhmReleasePathWithin -Path $fullPath -Parent $rootPath) -or
        $fullPath.Equals($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected root: $fullPath"
    }

    $relative = $fullPath.Substring((Get-LhmReleasePathPrefix -Path $rootPath).Length)
    return $relative.Replace('\', '/')
}

function Assert-LhmSafeManifestPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        [System.IO.Path]::IsPathRooted($Path) -or
        $Path.Contains('\') -or
        $Path.Contains(':')) {
        throw "Manifest path is not a canonical relative path: $Path"
    }

    $segments = @($Path.Split('/'))
    if ($segments.Count -eq 0 -or
        @($segments | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
        throw "Manifest path contains an unsafe segment: $Path"
    }
}

function Get-LhmPayloadFileEntries {
    param([Parameter(Mandatory)][string]$PayloadRoot)

    $rootPath = Resolve-LhmReleaseFullPath -Path $PayloadRoot
    if (-not [System.IO.Directory]::Exists($rootPath)) {
        throw "Payload directory does not exist: $rootPath"
    }

    $rootItem = Get-Item -LiteralPath $rootPath -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Payload root cannot be a reparse point: $rootPath"
    }

    $items = @(Get-ChildItem -LiteralPath $rootPath -Force -Recurse)
    foreach ($item in $items) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Payload cannot contain reparse points: $($item.FullName)"
        }
    }

    return @(
        $items |
            Where-Object { -not $_.PSIsContainer } |
            ForEach-Object {
                $relative = ConvertTo-LhmReleaseRelativePath -Root $rootPath -Path $_.FullName
                Assert-LhmSafeManifestPath -Path $relative
                [ordered]@{
                    path = $relative
                    length = [long]$_.Length
                    sha256 = Get-LhmReleaseFileSha256 -Path $_.FullName
                }
            } |
            Sort-Object { $_.path }
    )
}

function New-LhmReleaseZip {
    param(
        [Parameter(Mandatory)][string]$PayloadRoot,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][datetime]$TimestampUtc
    )

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

    $payloadPath = Resolve-LhmReleaseFullPath -Path $PayloadRoot
    $destinationPath = Resolve-LhmReleaseFullPath -Path $Destination
    if ([System.IO.File]::Exists($destinationPath)) {
        throw "Release archive already exists: $destinationPath"
    }

    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destinationPath)) | Out-Null
    $temporaryPath = $destinationPath + '.tmp-' + [guid]::NewGuid().ToString('N')
    $entries = @(Get-LhmPayloadFileEntries -PayloadRoot $payloadPath)
    if ($entries.Count -eq 0) {
        throw "Release payload is empty: $payloadPath"
    }

    $archiveStream = $null
    $archive = $null
    try {
        $archiveStream = [System.IO.File]::Open(
            $temporaryPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $archive = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        $entryTimestamp = [System.DateTimeOffset]::new($TimestampUtc.ToUniversalTime())

        foreach ($entryMetadata in $entries) {
            $sourcePath = Join-Path $payloadPath ($entryMetadata.path.Replace('/', '\'))
            $entry = $archive.CreateEntry(
                $entryMetadata.path,
                [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $entryTimestamp
            $sourceStream = [System.IO.File]::Open(
                $sourcePath,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::Read)
            $entryStream = $entry.Open()
            try {
                $sourceStream.CopyTo($entryStream)
            }
            finally {
                $entryStream.Dispose()
                $sourceStream.Dispose()
            }
        }

        $archive.Dispose()
        $archive = $null
        $archiveStream.Dispose()
        $archiveStream = $null
        [System.IO.File]::Move($temporaryPath, $destinationPath)
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
        if ($null -ne $archiveStream) {
            $archiveStream.Dispose()
        }
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
    }

    return $entries
}

function Write-LhmReleaseJsonAtomically {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    $destination = Resolve-LhmReleaseFullPath -Path $Path
    if ([System.IO.File]::Exists($destination)) {
        throw "Refusing to overwrite release metadata: $destination"
    }

    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
    $temporaryPath = $destination + '.tmp-' + [guid]::NewGuid().ToString('N')
    try {
        $json = $Value | ConvertTo-Json -Depth 16
        $encoding = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($temporaryPath, $json + [Environment]::NewLine, $encoding)
        [System.IO.File]::Move($temporaryPath, $destination)
    }
    finally {
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
    }
}

function Assert-LhmReleaseObjectProperty {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object -or $Name -notin @($Object.PSObject.Properties.Name)) {
        throw "Release manifest is missing required property '$Name'."
    }
}

function Assert-LhmReleaseJsonString {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$Name,
        [switch]$AllowEmpty
    )

    if (-not ($Value -is [string]) -or
        ((-not $AllowEmpty) -and [string]::IsNullOrWhiteSpace([string]$Value))) {
        throw "Release manifest property '$Name' must be a string$(
            if ($AllowEmpty) { '' } else { ' with a value' })."
    }
}

function Assert-LhmReleaseJsonBoolean {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$Name
    )

    if (-not ($Value -is [bool])) {
        throw "Release manifest property '$Name' must be a Boolean."
    }
}

function Assert-LhmReleaseJsonArray {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$Name
    )

    if (-not ($Value -is [System.Array])) {
        throw "Release manifest property '$Name' must be an array."
    }
}

function Test-LhmReleaseJsonInteger {
    param([AllowNull()]$Value)

    return $Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64]
}

function Get-LhmZipEntryVersionInfo {
    param(
        [Parameter(Mandatory)]
        $Entry
    )

    $tempRoot = Resolve-LhmReleaseFullPath -Path ([System.IO.Path]::GetTempPath())
    $tempPath = Resolve-LhmReleaseFullPath -Path (
        Join-Path $tempRoot ('sq-lhm-version-' + [guid]::NewGuid().ToString('N') + '.exe'))
    if (-not (Test-LhmReleasePathWithin -Path $tempPath -Parent $tempRoot)) {
        throw "Generated version-inspection path escaped the system temp root: $tempPath"
    }

    $source = $null
    $destination = $null
    try {
        $source = $Entry.Open()
        $destination = [System.IO.File]::Open(
            $tempPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $source.CopyTo($destination)
        $destination.Dispose()
        $destination = $null
        $source.Dispose()
        $source = $null

        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($tempPath)
        if ([string]::IsNullOrWhiteSpace($versionInfo.FileVersion) -or
            [string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
            throw "Release entry point has no file/product version metadata: $($Entry.FullName)"
        }
        return [pscustomobject]@{
            FileVersion = [string]$versionInfo.FileVersion
            ProductVersion = [string]$versionInfo.ProductVersion
        }
    }
    finally {
        if ($null -ne $destination) {
            $destination.Dispose()
        }
        if ($null -ne $source) {
            $source.Dispose()
        }
        if ([System.IO.File]::Exists($tempPath)) {
            [System.IO.File]::Delete($tempPath)
        }
    }
}

function Test-LhmReleaseCandidate {
    param([Parameter(Mandatory)][string]$CandidatePath)

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

    $candidate = Resolve-LhmReleaseFullPath -Path $CandidatePath
    if (-not [System.IO.Directory]::Exists($candidate)) {
        throw "Release candidate directory does not exist: $candidate"
    }
    Assert-LhmReleasePathHasNoReparseAncestor -Path $candidate

    $candidateItems = @(Get-ChildItem -LiteralPath $candidate -Force)
    foreach ($item in $candidateItems) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Release candidate cannot contain reparse points: $($item.FullName)"
        }
    }
    $manifestItems = @(
        $candidateItems |
            Where-Object { -not $_.PSIsContainer -and $_.Name -ceq 'release-manifest.json' }
    )
    $packageDirectories = @(
        $candidateItems |
            Where-Object { $_.PSIsContainer -and $_.Name -ceq 'packages' }
    )
    if ($candidateItems.Count -ne 2 -or
        $manifestItems.Count -ne 1 -or
        $packageDirectories.Count -ne 1) {
        throw 'Release candidate root must contain only packages/ and release-manifest.json.'
    }

    $packageItems = @(Get-ChildItem -LiteralPath $packageDirectories[0].FullName -Force)
    if (@($packageItems | Where-Object {
        $_.PSIsContainer -or
        (($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
    }).Count -gt 0) {
        throw 'Release packages/ must contain only regular archive files.'
    }
    $actualArchivePaths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($packageItem in $packageItems) {
        [void]$actualArchivePaths.Add('packages/' + $packageItem.Name)
    }

    $manifestPath = Join-Path $candidate 'release-manifest.json'
    if (-not [System.IO.File]::Exists($manifestPath)) {
        throw "Release candidate has no manifest: $manifestPath"
    }
    $manifestItem = Get-Item -LiteralPath $manifestPath -Force
    if ($manifestItem.Length -gt 4MB) {
        throw "Release manifest exceeds the 4 MiB validation limit: $manifestPath"
    }

    $manifest = ConvertFrom-LhmReleaseJson -Json (
        Get-Content -Raw -LiteralPath $manifestPath)
    foreach ($property in @(
        'schema',
        'version',
        'releaseId',
        'createdUtc',
        'baseVersion',
        'sdkVersion',
        'configuration',
        'platform',
        'source',
        'verification',
        'promotable',
        'packages')) {
        Assert-LhmReleaseObjectProperty -Object $manifest -Name $property
    }
    Assert-LhmReleaseJsonString -Value $manifest.schema -Name 'schema'
    if (-not (Test-LhmReleaseJsonInteger -Value $manifest.version) -or
        $manifest.schema -cne 'sq.lhm-release' -or [int64]$manifest.version -ne 1) {
        throw "Unsupported release manifest schema/version: $($manifest.schema) v$($manifest.version)"
    }
    Assert-LhmReleaseJsonString -Value $manifest.releaseId -Name 'releaseId'
    Assert-LhmSafeReleaseId -ReleaseId ([string]$manifest.releaseId)
    if ([System.IO.Path]::GetFileName($candidate.TrimEnd('\', '/')) -cne [string]$manifest.releaseId) {
        throw "Candidate directory name does not match ReleaseId '$($manifest.releaseId)'."
    }

    Assert-LhmReleaseJsonString -Value $manifest.createdUtc -Name 'createdUtc'
    $createdUtc = [System.DateTimeOffset]::MinValue
    if (-not [System.DateTimeOffset]::TryParse(
        [string]$manifest.createdUtc,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$createdUtc) -or
        $createdUtc.Offset -ne [System.TimeSpan]::Zero) {
        throw "Release candidate creation time must be an unambiguous UTC timestamp: $($manifest.createdUtc)"
    }

    Assert-LhmReleaseJsonString -Value $manifest.baseVersion -Name 'baseVersion'
    if ([string]$manifest.baseVersion -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
        throw "Release candidate base version is invalid: $($manifest.baseVersion)"
    }
    Assert-LhmReleaseJsonString -Value $manifest.sdkVersion -Name 'sdkVersion'
    Assert-LhmReleaseJsonString -Value $manifest.configuration -Name 'configuration'
    Assert-LhmReleaseJsonString -Value $manifest.platform -Name 'platform'
    Assert-LhmReleaseJsonBoolean -Value $manifest.promotable -Name 'promotable'
    if ([string]$manifest.configuration -cne 'Release' -or [string]$manifest.platform -cne 'x64') {
        throw "Release candidate configuration/platform must be Release/x64."
    }
    if ([string]$manifest.sdkVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "Release candidate must record a stable SDK version: $($manifest.sdkVersion)"
    }

    foreach ($property in @(
        'commit',
        'branch',
        'repository',
        'clean',
        'changes',
        'fingerprintSha256',
        'fileCount')) {
        Assert-LhmReleaseObjectProperty -Object $manifest.source -Name $property
    }
    Assert-LhmReleaseJsonString -Value $manifest.source.commit -Name 'source.commit'
    Assert-LhmReleaseJsonString -Value $manifest.source.branch -Name 'source.branch'
    Assert-LhmReleaseJsonBoolean -Value $manifest.source.clean -Name 'source.clean'
    Assert-LhmReleaseJsonArray -Value $manifest.source.changes -Name 'source.changes'
    Assert-LhmReleaseJsonString -Value $manifest.source.fingerprintSha256 -Name 'source.fingerprintSha256'
    if ([string]$manifest.source.commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Release candidate source commit is invalid: $($manifest.source.commit)"
    }
    if ([string]$manifest.source.fingerprintSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'Release candidate source fingerprint must be a canonical SHA-256 digest.'
    }
    if (-not (Test-LhmReleaseJsonInteger -Value $manifest.source.fileCount) -or
        [int64]$manifest.source.fileCount -lt 1) {
        throw 'Release candidate source fileCount must be a positive integer.'
    }
    if ($null -ne $manifest.source.repository) {
        Assert-LhmReleaseJsonString -Value $manifest.source.repository -Name 'source.repository'
        $repositoryIdentifier = [string]$manifest.source.repository
        $authorityEnd = $repositoryIdentifier.IndexOf('/')
        $authority = if ($authorityEnd -ge 0) {
            $repositoryIdentifier.Substring(0, $authorityEnd)
        }
        else {
            $repositoryIdentifier
        }
        if ($repositoryIdentifier.Contains('://') -or
            $repositoryIdentifier.Contains('\') -or
            $repositoryIdentifier.Contains('?') -or
            $repositoryIdentifier.Contains('#') -or
            $authority.Contains('@') -or
            $repositoryIdentifier.IndexOfAny([char[]]@("`r", "`n", "`0")) -ge 0) {
            throw 'Release candidate source.repository is not a credential-safe identifier.'
        }
    }

    $sourceChanges = @($manifest.source.changes)
    foreach ($change in $sourceChanges) {
        Assert-LhmReleaseJsonString -Value $change -Name 'source.changes[]'
    }
    if ([bool]$manifest.source.clean -ne ($sourceChanges.Count -eq 0)) {
        throw 'Release candidate source clean state does not match its recorded changes.'
    }
    $hasDirtySuffix = ([string]$manifest.releaseId).EndsWith(
        '-dirty',
        [System.StringComparison]::Ordinal)
    if ([bool]$manifest.source.clean -eq $hasDirtySuffix) {
        throw 'Release candidate dirty state and ReleaseId -dirty suffix do not agree.'
    }

    foreach ($property in @('completed', 'commands')) {
        Assert-LhmReleaseObjectProperty -Object $manifest.verification -Name $property
    }
    Assert-LhmReleaseJsonBoolean -Value $manifest.verification.completed -Name 'verification.completed'
    Assert-LhmReleaseJsonArray -Value $manifest.verification.commands -Name 'verification.commands'
    $verificationCommands = @($manifest.verification.commands)
    $verificationCommandSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($command in $verificationCommands) {
        Assert-LhmReleaseJsonString -Value $command -Name 'verification.commands[]'
        if (-not $verificationCommandSet.Add([string]$command)) {
            throw "Release candidate contains a duplicate verification command: $command"
        }
    }
    if ([bool]$manifest.verification.completed) {
        $requiredCommands = @(Get-LhmReleaseVerificationCommands)
        if ($verificationCommandSet.Count -ne $requiredCommands.Count -or
            @($requiredCommands | Where-Object {
                -not $verificationCommandSet.Contains($_)
            }).Count -gt 0) {
            throw 'Completed release verification must record the complete required command set.'
        }
    }
    elseif ($verificationCommands.Count -ne 0) {
        throw 'Incomplete release verification cannot record completed commands.'
    }

    if ([bool]$manifest.promotable -and
        ((-not [bool]$manifest.source.clean) -or (-not [bool]$manifest.verification.completed))) {
        throw 'A promotable release must have clean source and completed verification.'
    }
    if ([bool]$manifest.promotable -and
        [string]::IsNullOrWhiteSpace([string]$manifest.source.repository)) {
        throw 'A promotable release must record a credential-safe source repository identifier.'
    }

    Assert-LhmReleaseJsonArray -Value $manifest.packages -Name 'packages'
    $packages = @($manifest.packages)
    if ($packages.Count -ne 2) {
        throw "Release candidate must contain exactly two packages; found $($packages.Count)."
    }
    $expectedFrameworks = @('net10.0-windows', 'net472')
    $actualFrameworks = @($packages | ForEach-Object { [string]$_.targetFramework } | Sort-Object -Unique)
    if (($actualFrameworks -join '|') -cne (($expectedFrameworks | Sort-Object) -join '|')) {
        throw "Release candidate frameworks are incomplete or duplicated: $($actualFrameworks -join ', ')"
    }

    $seenArchives = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $declaredArchivePaths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $fileVersions = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $productVersions = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($package in $packages) {
        foreach ($property in @(
            'targetFramework',
            'platform',
            'runtimeIdentifier',
            'selfContained',
            'entryPoint',
            'fileVersion',
            'productVersion',
            'archive',
            'length',
            'sha256',
            'files')) {
            Assert-LhmReleaseObjectProperty -Object $package -Name $property
        }
        foreach ($stringProperty in @(
            'targetFramework',
            'platform',
            'runtimeIdentifier',
            'entryPoint',
            'fileVersion',
            'productVersion',
            'archive',
            'sha256')) {
            Assert-LhmReleaseJsonString `
                -Value $package.$stringProperty `
                -Name "packages[].$stringProperty"
        }
        Assert-LhmReleaseJsonBoolean `
            -Value $package.selfContained `
            -Name 'packages[].selfContained'
        Assert-LhmReleaseJsonArray -Value $package.files -Name 'packages[].files'
        if (-not (Test-LhmReleaseJsonInteger -Value $package.length) -or
            [int64]$package.length -lt 0) {
            throw 'Release package length must be a non-negative integer.'
        }
        if ([string]$package.platform -cne 'x64') {
            throw "Unsupported release platform: $($package.platform)"
        }
        if ([string]$package.runtimeIdentifier -cne 'win-x64') {
            throw "Unsupported release runtimeIdentifier; expected win-x64: $($package.runtimeIdentifier)"
        }
        if ([bool]$package.selfContained) {
            throw 'Release packages must remain framework-dependent (selfContained false).'
        }
        $fileVersionMatch = [System.Text.RegularExpressions.Regex]::Match(
            [string]$package.fileVersion,
            '^(?<numeric>\d+\.\d+\.\d+)(?:\.\d+)?(?:\s.*)?$')
        $productVersionMatch = [System.Text.RegularExpressions.Regex]::Match(
            [string]$package.productVersion,
            '^(?<numeric>\d+\.\d+\.\d+)(?:[.+-].*)?(?:\s.*)?$')
        if (-not $fileVersionMatch.Success -or -not $productVersionMatch.Success) {
            throw "Release package versions are invalid for $($package.targetFramework)."
        }
        $baseNumericVersion = [System.Text.RegularExpressions.Regex]::Match(
            [string]$manifest.baseVersion,
            '^\d+\.\d+\.\d+').Value
        if ($fileVersionMatch.Groups['numeric'].Value -cne $baseNumericVersion -or
            $productVersionMatch.Groups['numeric'].Value -cne $baseNumericVersion) {
            throw "Release package versions do not match base version $($manifest.baseVersion)."
        }
        [void]$fileVersions.Add([string]$package.fileVersion)
        [void]$productVersions.Add([string]$package.productVersion)

        $archiveRelative = [string]$package.archive
        Assert-LhmSafeManifestPath -Path $archiveRelative
        if (-not $archiveRelative.StartsWith('packages/', [System.StringComparison]::Ordinal)) {
            throw "Release archive must be under packages/: $archiveRelative"
        }
        if (-not $seenArchives.Add($archiveRelative)) {
            throw "Duplicate release archive path: $archiveRelative"
        }
        [void]$declaredArchivePaths.Add($archiveRelative)

        $archivePath = Join-Path $candidate ($archiveRelative.Replace('/', '\'))
        if (-not [System.IO.File]::Exists($archivePath)) {
            throw "Release archive is missing: $archivePath"
        }
        $archiveItem = Get-Item -LiteralPath $archivePath -Force
        if (($archiveItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Release archive cannot be a reparse point: $archivePath"
        }
        if ([long]$archiveItem.Length -ne [long]$package.length) {
            throw "Release archive length mismatch: $archiveRelative"
        }
        if ([string]$package.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Release archive SHA-256 is not canonical: $archiveRelative"
        }
        if ((Get-LhmReleaseFileSha256 -Path $archivePath) -cne ([string]$package.sha256).ToLowerInvariant()) {
            throw "Release archive hash mismatch: $archiveRelative"
        }

        $expectedFiles = @($package.files)
        if ($expectedFiles.Count -eq 0 -or $expectedFiles.Count -gt 4096) {
            throw "Release package payload count is outside the 1-4096 limit: $archiveRelative"
        }
        $fileMap = @{}
        $totalPayloadBytes = 0L
        foreach ($file in $expectedFiles) {
            foreach ($property in @('path', 'length', 'sha256')) {
                Assert-LhmReleaseObjectProperty -Object $file -Name $property
            }
            Assert-LhmReleaseJsonString -Value $file.path -Name 'packages[].files[].path'
            Assert-LhmReleaseJsonString -Value $file.sha256 -Name 'packages[].files[].sha256'
            if (-not (Test-LhmReleaseJsonInteger -Value $file.length)) {
                throw "Payload length must be an integer: $($file.path)"
            }
            $relative = [string]$file.path
            Assert-LhmSafeManifestPath -Path $relative
            if ($relative.StartsWith(
                'runtimes/',
                [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "RID-specific release payload cannot contain a runtimes subtree: $relative"
            }
            $key = $relative.ToLowerInvariant()
            if ($fileMap.ContainsKey($key)) {
                throw "Duplicate or case-colliding payload path: $relative"
            }
            if ([long]$file.length -lt 0 -or [string]$file.sha256 -notmatch '^[0-9a-f]{64}$') {
                throw "Payload metadata is not canonical: $relative"
            }
            $totalPayloadBytes += [long]$file.length
            if ($totalPayloadBytes -gt 2GB) {
                throw "Release package exceeds the 2 GiB payload validation limit: $archiveRelative"
            }
            $fileMap[$key] = $file
        }

        $entryPoint = [string]$package.entryPoint
        Assert-LhmSafeManifestPath -Path $entryPoint
        if (-not $fileMap.ContainsKey($entryPoint.ToLowerInvariant()) -or
            [System.IO.Path]::GetFileName($entryPoint) -cne 'LibreHardwareMonitor.Windows.Forms.exe') {
            throw "Release entry point is missing or invalid: $entryPoint"
        }

        $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            $zipFiles = @($archive.Entries)
            $directoryEntries = @(
                $zipFiles |
                    Where-Object { [string]::IsNullOrEmpty($_.Name) }
            )
            if ($directoryEntries.Count -gt 0) {
                throw "Release archive contains undeclared directory entries: $archiveRelative"
            }
            if ($zipFiles.Count -ne $fileMap.Count) {
                throw "Release archive entry count mismatch: $archiveRelative"
            }

            $seenEntries = [System.Collections.Generic.HashSet[string]]::new(
                [System.StringComparer]::OrdinalIgnoreCase)
            $entryPointVersionInfo = $null
            foreach ($entry in $zipFiles) {
                $entryPath = $entry.FullName.Replace('\', '/')
                Assert-LhmSafeManifestPath -Path $entryPath
                if (-not $seenEntries.Add($entryPath)) {
                    throw "Duplicate or case-colliding ZIP entry: $entryPath"
                }
                $key = $entryPath.ToLowerInvariant()
                if (-not $fileMap.ContainsKey($key)) {
                    throw "Unexpected ZIP entry: $entryPath"
                }

                $expected = $fileMap[$key]
                if ([string]$expected.path -cne $entryPath) {
                    throw "ZIP entry path casing does not match its manifest path: $entryPath"
                }
                if ([long]$entry.Length -ne [long]$expected.length) {
                    throw "ZIP entry length mismatch: $entryPath"
                }
                $entryStream = $entry.Open()
                try {
                    $entryHash = Get-LhmReleaseStreamSha256 -Stream $entryStream
                }
                finally {
                    $entryStream.Dispose()
                }
                if ($entryHash -cne ([string]$expected.sha256).ToLowerInvariant()) {
                    throw "ZIP entry hash mismatch: $entryPath"
                }
                if ($entryPath -ceq $entryPoint) {
                    $entryPointVersionInfo = Get-LhmZipEntryVersionInfo -Entry $entry
                }
            }

            if ($null -eq $entryPointVersionInfo -or
                $entryPointVersionInfo.FileVersion -cne [string]$package.fileVersion -or
                $entryPointVersionInfo.ProductVersion -cne [string]$package.productVersion) {
                throw "Release entry-point version metadata does not match the archive: $archiveRelative"
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    if ($fileVersions.Count -ne 1 -or $productVersions.Count -ne 1) {
        throw 'Release framework entry points do not identify the same source generation.'
    }
    if ($actualArchivePaths.Count -ne $declaredArchivePaths.Count) {
        throw 'Release candidate contains undeclared or missing package files.'
    }
    foreach ($actualArchivePath in $actualArchivePaths) {
        if (-not $declaredArchivePaths.Contains($actualArchivePath)) {
            throw "Release candidate contains an undeclared package file: $actualArchivePath"
        }
    }

    return [pscustomobject]@{
        Status = 'VERIFIED'
        CandidatePath = $candidate
        ReleaseId = [string]$manifest.releaseId
        CreatedUtc = $createdUtc.UtcDateTime
        SourceCommit = [string]$manifest.source.commit
        SourceBranch = [string]$manifest.source.branch
        SourceClean = [bool]$manifest.source.clean
        SourceChanges = [string[]]@($sourceChanges)
        SourceFingerprintSha256 = [string]$manifest.source.fingerprintSha256
        SourceFileCount = [int64]$manifest.source.fileCount
        Promotable = [bool]$manifest.promotable
        Packages = $packages.Count
    }
}

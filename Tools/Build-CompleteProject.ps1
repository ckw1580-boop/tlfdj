[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$projectPath = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $OutputPath) {
    $OutputPath = Join-Path $projectPath ('Build/Recovery/tlfdj-unity-complete-{0}.zip' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$archivePath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $archivePath) { throw ('Refusing to overwrite existing archive: ' + $archivePath) }
& (Join-Path $PSScriptRoot 'Test-ProjectResources.ps1') -ProjectRoot $projectPath

# Explicit allowlist: never package credentials, original staging software, .git or Unity caches.
$files = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($directory in @('Assets', 'Packages', 'ProjectSettings', 'Tools', 'Docs')) {
    $sourcePath = Join-Path $projectPath $directory
    if (Test-Path -LiteralPath $sourcePath) {
        $prefix = [IO.Path]::GetFullPath($sourcePath) + [IO.Path]::DirectorySeparatorChar
        if ($archivePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The output ZIP must be outside the packaged source directories.'
        }
        foreach ($file in Get-ChildItem -LiteralPath $sourcePath -Recurse -Force -File) { $files.Add($file) }
    }
}
foreach ($name in @('README.md', '.gitattributes', '.gitignore')) {
    $path = Join-Path $projectPath $name
    if (Test-Path -LiteralPath $path) { $files.Add((Get-Item -LiteralPath $path -Force)) }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($archivePath)) | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$partialPath = $archivePath + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
$manifestFiles = [Collections.Generic.List[object]]::new()
$stream = [IO.File]::Open($partialPath, [IO.FileMode]::CreateNew)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($projectPath.Length + 1).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            $manifestFiles.Add([ordered]@{ path = $relative; size = $file.Length; sha256 = $hash })
        }
        $manifest = [ordered]@{
            schemaVersion = 1
            createdAtUtc = [DateTime]::UtcNow.ToString('o')
            unityVersion = (Get-Content -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt') -Encoding UTF8 | Select-Object -First 1)
            files = $manifestFiles.ToArray()
        } | ConvertTo-Json -Depth 5
        $entry = $zip.CreateEntry('RESTORE-MANIFEST.json')
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($manifest) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }

# Read every compressed entry back before giving the archive its final .zip name.
Write-Host ('Verifying {0} compressed files...' -f $manifestFiles.Count)
$zip = [IO.Compression.ZipFile]::OpenRead($partialPath)
try {
    foreach ($record in $manifestFiles) {
        $entry = $zip.GetEntry($record.path)
        if ($null -eq $entry -or $entry.Length -ne $record.size) { throw ('ZIP entry missing/truncated: ' + $record.path) }
        $entryStream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose(); $entryStream.Dispose() }
        if ($hash -ne $record.sha256) { throw ('ZIP hash mismatch (source may have changed while packing): ' + $record.path) }
    }
} finally { $zip.Dispose() }
# Both paths are files in the same explicitly selected output directory; no recursive moves.
Move-Item -LiteralPath $partialPath -Destination $archivePath
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
($archiveHash + '  ' + [IO.Path]::GetFileName($archivePath)) | Set-Content -LiteralPath ($archivePath + '.sha256') -Encoding ASCII
Write-Host ('PASS: complete recovery ZIP created and verified: ' + $archivePath)

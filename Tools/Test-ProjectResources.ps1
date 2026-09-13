[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$projectPath = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$scannerSource = Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets/ElectricalSim/Runtime/ProjectResourceFiles.cs'
if (-not ('ElectricalSim.ProjectResourceFiles' -as [type])) {
    Add-Type -TypeDefinition (Get-Content -LiteralPath $scannerSource -Raw -Encoding UTF8)
}
$problems = [ElectricalSim.ProjectResourceFiles]::FindProblems($projectPath)
$manifestPath = Join-Path $projectPath 'RESTORE-MANIFEST.json'
$verifiedFiles = 0
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or @($manifest.files).Count -eq 0) {
        throw 'Invalid recovery manifest.'
    }
    foreach ($entry in $manifest.files) {
        $filePath = [IO.Path]::GetFullPath((Join-Path $projectPath $entry.path))
        if (-not $filePath.StartsWith($projectPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw ('Manifest path escapes the project: ' + $entry.path)
        }
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            $problems.Add('Manifest file missing: ' + $entry.path)
        } elseif ((Get-Item -LiteralPath $filePath).Length -ne $entry.size -or
            (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash -ne $entry.sha256) {
            $problems.Add('Manifest size/hash mismatch: ' + $entry.path)
        } else { $verifiedFiles++ }
    }
}
if ($ReportPath) {
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFullPath)) | Out-Null
    [ordered]@{
        project = $projectPath
        checkedAtUtc = [DateTime]::UtcNow.ToString('o')
        passed = ($problems.Count -eq 0)
        manifestFilesVerified = $verifiedFiles
        problems = @($problems.ToArray())
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportFullPath -Encoding UTF8
}
if ($problems.Count -gt 0) {
    $problems | Select-Object -First 20 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw ('Resource validation failed: {0} problem(s). Obtain a complete ZIP or run git lfs pull in a Git clone. See Docs/project-recovery.md.' -f $problems.Count)
}
Write-Host ('PASS: no LFS pointers or missing required files/.meta. Manifest files verified: {0}.' -f $verifiedFiles)

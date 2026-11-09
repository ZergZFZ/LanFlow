# Build the optional OCR bundle (runtime + models + VC++ CRT + manifest) as a zip.
# ASCII-only source: Windows PowerShell 5.1 reads non-BOM UTF-8 as ANSI and breaks on CJK.
[CmdletBinding()]
param(
    # Empty means: read <Version> from native/FlickD.Desktop/FlickD.Desktop.csproj.
    # Never hard-code a release version here - it silently produces a wrong asset name.
    [string]$AppVersion = '',
    [string]$BundleVersion = '1.0.0',
    # Lowest main-program version this bundle is compatible with. This is the version in
    # which the IOcrEngine contract last changed - NOT the version being packaged. The app
    # rejects bundles whose MinAppVersion is newer than itself. Keep 1.6.0 until the
    # IOcrEngine contract changes.
    [string]$MinAppVersion = '1.6.0',
    # Stable component id. Keep the historical id: already published 2.0.0 bundles use it,
    # and the runtime validates paths/hashes/minAppVersion, not this string.
    [string]$BundleId = 'lanflow-ocr-win-x64',
    [string]$AssetsDir = '',
    [string]$VcRuntimeDir = 'C:\Windows\System32',
    [string]$OutDir = '',
    [string]$StagingDir = '',
    # Reuse an existing zip when the bundle content (runtime files, models, CRT) is unchanged.
    [switch]$SkipPublish,
    [switch]$ForceRebuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    $desktopProject = Join-Path $root 'native\FlickD.Desktop\FlickD.Desktop.csproj'
    if (-not (Test-Path $desktopProject)) { throw "missing desktop project: $desktopProject" }
    [xml]$projectXml = Get-Content $desktopProject -Raw
    $versionNodes = $projectXml.SelectNodes('//Version')
    if ($versionNodes.Count -eq 0) { throw "cannot read <Version> from $desktopProject" }
    $AppVersion = $versionNodes[0].InnerText.Trim()
    if ([string]::IsNullOrWhiteSpace($AppVersion)) { throw "empty <Version> in $desktopProject" }
    Write-Host "[0/6] AppVersion from csproj: $AppVersion"
}

if ([string]::IsNullOrWhiteSpace($AssetsDir)) { $AssetsDir = Join-Path $root '.build\ocr-assets' }
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $root 'artifacts\ocr' }
if ([string]::IsNullOrWhiteSpace($StagingDir)) { $StagingDir = Join-Path $env:TEMP 'flickd-ocr-staging' }

$pluginProject = Join-Path $root 'native\FlickD.Ocr\FlickD.Ocr.csproj'
$publishDir = Join-Path $env:TEMP 'flickd-ocr-publish'

Write-Host "[1/6] publish plugin"
if (-not $SkipPublish) {
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    & dotnet publish $pluginProject -c Release -o $publishDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
}

Write-Host "[2/6] assemble layout at $StagingDir"
if (Test-Path $StagingDir) { Remove-Item -Recurse -Force $StagingDir }
New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null
$runtimeDir = Join-Path $StagingDir 'runtime'
$modelsDir = Join-Path $StagingDir 'models'
$vcDir = Join-Path $StagingDir 'vcredist'
New-Item -ItemType Directory -Force -Path $runtimeDir, $modelsDir, $vcDir | Out-Null

$runtimeFiles = @(
    @{ From = (Join-Path $publishDir 'FlickD.Ocr.dll'); To = 'FlickD.Ocr.dll' },
    @{ From = (Join-Path $publishDir 'FlickD.Ocr.deps.json'); To = 'FlickD.Ocr.deps.json' },
    @{ From = (Join-Path $publishDir 'Microsoft.ML.OnnxRuntime.dll'); To = 'Microsoft.ML.OnnxRuntime.dll' },
    @{ From = (Join-Path $publishDir 'runtimes\win-x64\native\onnxruntime.dll'); To = 'onnxruntime.dll' },
    @{ From = (Join-Path $publishDir 'runtimes\win-x64\native\onnxruntime_providers_shared.dll'); To = 'onnxruntime_providers_shared.dll' }
)
foreach ($f in $runtimeFiles) {
    if (-not (Test-Path $f.From)) { throw "missing runtime file: $($f.From)" }
    Copy-Item $f.From (Join-Path $runtimeDir $f.To) -Force
}

$vcFiles = @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll', 'msvcp140_1.dll')
foreach ($name in $vcFiles) {
    $source = Join-Path $VcRuntimeDir $name
    if (-not (Test-Path $source)) { throw "missing VC++ runtime: $source" }
    Copy-Item $source (Join-Path $vcDir $name) -Force
}

$modelFiles = @('ppocrv5_mobile_det.onnx', 'ppocrv5_mobile_rec.onnx', 'ppocr_keys.txt')
foreach ($name in $modelFiles) {
    $source = Join-Path $AssetsDir $name
    if (-not (Test-Path $source)) { throw "missing model asset: $source" }
    Copy-Item $source (Join-Path $modelsDir $name) -Force
}

Write-Host "[3/6] hash files and write manifest"
$entries = New-Object System.Collections.Generic.List[object]
# Fixed three-folder layout: build relative paths from folder names only, so 8.3 short
# paths (ADMINI~1 / LANFLO~1) can never leak a truncated prefix into the manifest.
foreach ($folder in @('runtime', 'models', 'vcredist')) {
    $folderPath = Join-Path $StagingDir $folder
    foreach ($file in (Get-ChildItem $folderPath -File | Sort-Object Name)) {
        $relative = "$folder/$($file.Name)"
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $entries.Add([ordered]@{
                path   = $relative
                sha256 = $hash
                size   = $file.Length
            })
    }
}

$manifest = [ordered]@{
    bundleId     = $BundleId
    version      = $BundleVersion
    minAppVersion = $MinAppVersion
    files        = $entries
}

# Content fingerprint: SHA-256 over the path|sha256 pairs of the *runtime* payload only.
# manifest.json itself is excluded so that re-stamping minAppVersion/app version does not
# change the fingerprint - the fingerprint answers "did the OCR payload really change?".
$fingerprintSource = ($entries | ForEach-Object { "$($_.path)|$($_.sha256)" }) -join "`n"
$contentHash = (Get-FileHash -InputStream ([System.IO.MemoryStream]::new(
        [System.Text.Encoding]::UTF8.GetBytes($fingerprintSource))) -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest['contentHash'] = $contentHash

$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $StagingDir 'manifest.json'), $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ("[3/6] content fingerprint: {0}" -f $contentHash)

Write-Host "[4/6] reuse check"
$indexPath = Join-Path $OutDir 'index.json'
$index = @{}
if (Test-Path $indexPath) {
    $loaded = Get-Content $indexPath -Raw | ConvertFrom-Json
    foreach ($p in $loaded.PSObject.Properties) { $index[$p.Name] = $p.Value }
}

if (-not $ForceRebuild -and $index.ContainsKey($contentHash)) {
    $existing = Join-Path $OutDir $index[$contentHash]
    if (Test-Path $existing) {
        $reusedHash = (Get-FileHash $existing -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "[4/6] bundle content unchanged - reusing $($index[$contentHash])"
        Write-Host ("[5/6] sha256: {0}" -f $reusedHash)
        Write-Host ("[6/6] reused: {0}" -f $existing)
        Write-Host ("bundle: {0}" -f $existing)
        Write-Host ("contentHash: {0}" -f $contentHash)
        Write-Host ("reused: true")
        return
    }
    Write-Host "[4/6] index entry missing on disk - rebuilding"
}

Write-Host "[4/6] create zip"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zipPath = Join-Path $OutDir ("FlickD-{0}-ocr.zip" -f $AppVersion)
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($StagingDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "[5/6] hash zip"
$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @("$zipHash  FlickD-$AppVersion-ocr.zip")
[System.IO.File]::WriteAllText("$zipPath.sha256", ($lines -join "`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[6/6] done"
$size = (Get-Item $zipPath).Length
Write-Host ("bundle: {0}" -f $zipPath)
Write-Host ("size:   {0:F2} MB" -f ($size / 1MB))
Write-Host ("sha256: {0}" -f $zipHash)
Write-Host ("files:  {0}" -f $entries.Count)

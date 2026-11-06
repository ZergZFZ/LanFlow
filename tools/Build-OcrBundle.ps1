# Build the optional OCR bundle (runtime + models + VC++ CRT + manifest) as a zip.
# ASCII-only source: Windows PowerShell 5.1 reads non-BOM UTF-8 as ANSI and breaks on CJK.
[CmdletBinding()]
param(
    [string]$AppVersion = '1.6.0',
    [string]$BundleVersion = '1.0.0',
    [string]$AssetsDir = '',
    [string]$VcRuntimeDir = 'C:\Windows\System32',
    [string]$OutDir = '',
    [string]$StagingDir = '',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AssetsDir)) { $AssetsDir = Join-Path $root '.build\ocr-assets' }
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $root 'artifacts\ocr' }
if ([string]::IsNullOrWhiteSpace($StagingDir)) { $StagingDir = Join-Path $env:TEMP 'lanflow-ocr-staging' }

$pluginProject = Join-Path $root 'native\LanFlow.Ocr\LanFlow.Ocr.csproj'
$publishDir = Join-Path $env:TEMP 'lanflow-ocr-publish'

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
    @{ From = (Join-Path $publishDir 'LanFlow.Ocr.dll'); To = 'LanFlow.Ocr.dll' },
    @{ From = (Join-Path $publishDir 'LanFlow.Ocr.deps.json'); To = 'LanFlow.Ocr.deps.json' },
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
    bundleId     = 'lanflow-ocr-win-x64'
    version      = $BundleVersion
    minAppVersion = $AppVersion
    files        = $entries
}
$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $StagingDir 'manifest.json'), $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[4/6] create zip"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zipPath = Join-Path $OutDir ("LanFlow-{0}-ocr.zip" -f $AppVersion)
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($StagingDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "[5/6] hash zip"
$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @("$zipHash  LanFlow-$AppVersion-ocr.zip")
[System.IO.File]::WriteAllText("$zipPath.sha256", ($lines -join "`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[6/6] done"
$size = (Get-Item $zipPath).Length
Write-Host ("bundle: {0}" -f $zipPath)
Write-Host ("size:   {0:F2} MB" -f ($size / 1MB))
Write-Host ("sha256: {0}" -f $zipHash)
Write-Host ("files:  {0}" -f $entries.Count)

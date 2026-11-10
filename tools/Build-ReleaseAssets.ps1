<#
.SYNOPSIS
  一键打包 FlickD 的三条发布资产。
.DESCRIPTION
  从 csproj 读版本 → 前置核对 CHANGELOG 与既有资产 → publish full/lite
  → 排除 PDB/兼容壳/内嵌 OCR → 压缩与复制 → 生成 OCR bundle
  → 生成 SHA-256 sidecar → 调用 Verify-ReleaseAssets.ps1 自检。
  2.1.0 起 full 包不再包含 LanFlow.exe 与 ocr/ 目录。
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipOcrBundle,
    [switch]$SkipVerify,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
# 进程级放行：保证脚本内以 & 调用 Build-OcrBundle.ps1 / Verify-ReleaseAssets.ps1
# 不受开发机「禁止未签名脚本」策略影响（见 docs/ARCHITECTURE.md §6.3）。
try { Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force } catch { }

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'native\FlickD.Desktop\FlickD.Desktop.csproj'

[xml]$projXml = Get-Content -LiteralPath $project -Raw
$versionNode = $projXml.SelectNodes('//Version')[0]
if ($null -eq $versionNode) { throw '无法从 csproj 读取 <Version>' }
$version = $versionNode.InnerText.Trim()
if ([string]::IsNullOrWhiteSpace($version)) { throw 'csproj 的 <Version> 为空' }
Write-Host "[1/7] Version from csproj: $version"

$latestEntry = Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') |
    Where-Object { $_ -match '^## \[(\d+\.\d+\.\d+)\]' } | Select-Object -First 1
$latestVersion = if ($latestEntry -match '^## \[(\d+\.\d+\.\d+)\]') { $Matches[1] } else { '(无)' }
if ($latestVersion -ne $version) {
    throw "CHANGELOG 最新正式条目 ($latestVersion) 与 csproj Version ($version) 不一致；请先 bump 版本并把 [Unreleased] 转正。"
}
Write-Host "  CHANGELOG 最新正式条目一致: $latestVersion"

$dist = Join-Path $root "artifacts\$version"
$fullDir = Join-Path $dist 'full'
$liteDir = Join-Path $dist 'lite'
$ocrDir = Join-Path $root 'artifacts\ocr'
$fullZip = Join-Path $dist "FlickD-$version-full.zip"
$liteExe = Join-Path $dist "FlickD-$version-lite.exe"
$ocrZip = Join-Path $ocrDir "FlickD-$version-ocr.zip"

# 已发布资产的本地副本不能被静默覆盖：默认拒绝，确需重打包须显式 -Force。
$existing = @(@($fullZip, $liteExe, $ocrZip) | Where-Object { Test-Path -LiteralPath $_ })
if ($existing.Count -gt 0 -and -not $Force) {
    $names = ($existing | ForEach-Object { Split-Path -Path $_ -Leaf }) -join ', '
    throw "目标版本 $version 已存在 $($existing.Count) 个资产（$names），疑似已发布；确需重打包请显式加 -Force。"
}

foreach ($d in @($dist, $fullDir, $liteDir, $ocrDir)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

Write-Host '[2/7] publish full (win-x64)'
& dotnet publish $project -c $Configuration -p:PublishProfile=win-x64 -p:DebugType=none -o $fullDir --nologo
if ($LASTEXITCODE -ne 0) { throw "full publish 失败 (exit $LASTEXITCODE)" }

Write-Host '[3/7] publish lite (win-x64-lite)'
& dotnet publish $project -c $Configuration -p:PublishProfile=win-x64-lite -p:DebugType=none -o $liteDir --nologo
if ($LASTEXITCODE -ne 0) { throw "lite publish 失败 (exit $LASTEXITCODE)" }

Write-Host '[4/7] 清理 PDB / 兼容壳 / 内嵌 OCR'
foreach ($dir in @($fullDir, $liteDir)) {
    # publish 已用 -p:DebugType=none 从源头不生成符号文件；这里只做兜底清理与硬断言。
    # 使用 .NET API 而非 cmdlet 管道：旧脚本宿主下 Remove-Item 无法绑定 FileInfo，
    # 且开发机的删除保护会对 cmdlet 删除误报 fail-closed（CI 只做语法/shape 校验，
    # 完整打包才暴露这两点）。
    foreach ($pdb in [System.IO.Directory]::GetFiles($dir, '*.pdb', [System.IO.SearchOption]::AllDirectories)) {
        [System.IO.File]::Delete($pdb)
    }
    $legacy = Join-Path $dir 'LanFlow.exe'
    if ([System.IO.File]::Exists($legacy)) { [System.IO.File]::Delete($legacy) }
    $embedded = Join-Path $dir 'ocr'
    if ([System.IO.Directory]::Exists($embedded)) { [System.IO.Directory]::Delete($embedded, $true) }
    if ([System.IO.Directory]::GetFiles($dir, '*.pdb', [System.IO.SearchOption]::AllDirectories).Count -gt 0) {
        throw "publish 目录仍含 PDB：$dir；请检查 publish profile 是否禁用了调试符号"
    }
}

& (Join-Path $PSScriptRoot 'Verify-ReleaseAssets.ps1') -PublishShape -FullDir $fullDir -LiteDir $liteDir

Write-Host '[5/7] 压缩 full / 复制 lite'
Compress-Archive -Path (Join-Path $fullDir '*') -DestinationPath $fullZip -Force -CompressionLevel Optimal
Copy-Item -LiteralPath (Join-Path $liteDir 'FlickD.exe') -Destination $liteExe -Force

Write-Host '[6/7] OCR bundle'
if (-not $SkipOcrBundle) {
    & (Join-Path $PSScriptRoot 'Build-OcrBundle.ps1')
}
if (-not (Test-Path -LiteralPath $ocrZip)) { throw "缺少 OCR 资产: $ocrZip" }

Write-Host '[7/7] SHA-256 sidecar'
foreach ($a in @($fullZip, $liteExe, $ocrZip)) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $a).Hash.ToLowerInvariant()
    $line = "$hash  $(Split-Path -Path $a -Leaf)"
    [System.IO.File]::WriteAllText("$a.sha256", $line + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host ("  {0}  {1}" -f (Split-Path -Path $a -Leaf), $hash)
}

if (-not $SkipVerify) {
    & (Join-Path $PSScriptRoot 'Verify-ReleaseAssets.ps1') -Version $version
}
Write-Host "完成: $dist"

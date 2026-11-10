<#
.SYNOPSIS
  发布资产校验。
.DESCRIPTION
  Release 模式（-Version）：校验当前待发布版本（须与 csproj / CHANGELOG 一致）的版本一致性、
  三条资产、sidecar、full ZIP 根目录、OCR manifest 与 PE 版本；不适用于 2.0.x 历史资产形状。
  Shape 模式（-PublishShape）：只校验 publish 目录形状，供 CI 与 Build-ReleaseAssets.ps1 前置调用，
  不需要 OCR 素材与网络，把「无 PDB / 无兼容壳 / 无内嵌 OCR」变成硬闸门。
#>
[CmdletBinding(DefaultParameterSetName = 'Release')]
param(
    [Parameter(ParameterSetName = 'Release', Mandatory = $true)][string]$Version,
    [Parameter(ParameterSetName = 'Shape', Mandatory = $true)][switch]$PublishShape,
    [Parameter(ParameterSetName = 'Shape', Mandatory = $true)][string]$FullDir,
    [Parameter(ParameterSetName = 'Shape', Mandatory = $true)][string]$LiteDir
)

$ErrorActionPreference = 'Stop'
$script:Checks = 0

function Assert-Check {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "[FAIL] $Message" }
    $script:Checks++
    Write-Host "  [OK] $Message"
}

function Test-PublishShape {
    param([string]$Dir, [string]$Label)
    Assert-Check (Test-Path -LiteralPath $Dir) "$Label 发布目录存在: $Dir"
    $exe = Join-Path $Dir 'FlickD.exe'
    $exeOk = (Test-Path -LiteralPath $exe) -and (Get-Item -LiteralPath $exe).Length -gt 0
    Assert-Check $exeOk "$Label 含非空 FlickD.exe"
    $pdb = @(Get-ChildItem -LiteralPath $Dir -Filter '*.pdb' -File -Recurse)
    Assert-Check ($pdb.Count -eq 0) "$Label 不含 PDB（实际 $($pdb.Count) 个）"
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $Dir 'LanFlow.exe'))) "$Label 不含兼容壳 LanFlow.exe"
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $Dir 'ocr'))) "$Label 不含内嵌 ocr/ 目录"
}

if ($PublishShape) {
    Write-Host '== 发布目录形状校验（CI / 打包前置）=='
    Test-PublishShape -Dir $FullDir -Label 'full'
    Test-PublishShape -Dir $LiteDir -Label 'lite'
    $markers = @('PresentationNative_cor3.dll', 'wpfgfx_cor3.dll', 'vcruntime140_cor3.dll')
    $hit = @($markers | Where-Object { Test-Path -LiteralPath (Join-Path $FullDir $_) })
    Assert-Check ($hit.Count -gt 0) "full 含 WPF 原生依赖标记（$($hit -join ', ')）"
    Write-Host "通过 $script:Checks 项校验。"
    return
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'native\FlickD.Desktop\FlickD.Desktop.csproj'
[xml]$projXml = Get-Content -LiteralPath $project -Raw
$csprojVersion = $projXml.SelectNodes('//Version')[0].InnerText.Trim()
Assert-Check ($csprojVersion -eq $Version) "csproj Version ($csprojVersion) 与待校验版本 ($Version) 一致"

$latestEntry = Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') |
    Where-Object { $_ -match '^## \[(\d+\.\d+\.\d+)\]' } | Select-Object -First 1
$latestVersion = if ($latestEntry -match '^## \[(\d+\.\d+\.\d+)\]') { $Matches[1] } else { '(无)' }
Assert-Check ($latestVersion -eq $Version) "CHANGELOG 最新正式条目 ($latestVersion) 与待校验版本 ($Version) 一致"

$dist = Join-Path $root "artifacts\$Version"
$ocrDir = Join-Path $root 'artifacts\ocr'
$fullZip = Join-Path $dist "FlickD-$Version-full.zip"
$liteExe = Join-Path $dist "FlickD-$Version-lite.exe"
$ocrZip = Join-Path $ocrDir "FlickD-$Version-ocr.zip"

foreach ($a in @($fullZip, $liteExe, $ocrZip)) {
    Assert-Check (Test-Path -LiteralPath $a) "资产存在: $(Split-Path -Path $a -Leaf)"
}

foreach ($a in @($fullZip, $liteExe, $ocrZip)) {
    $side = "$a.sha256"
    Assert-Check (Test-Path -LiteralPath $side) "sidecar 存在: $(Split-Path -Path $side -Leaf)"
    $expected = ((Get-Content -LiteralPath $side -Raw).Trim() -split '\s+')[0]
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $a).Hash
    Assert-Check ($expected.ToLowerInvariant() -eq $actual.ToLowerInvariant()) "SHA-256 与 sidecar 一致: $(Split-Path -Path $a -Leaf)"
}

$fs = [System.IO.File]::OpenRead($liteExe)
try { $b0 = $fs.ReadByte(); $b1 = $fs.ReadByte() } finally { $fs.Dispose() }
Assert-Check ($b0 -eq 77 -and $b1 -eq 90) 'lite 是合法 PE（MZ）'

if (-not ([System.Management.Automation.PSTypeName]'System.IO.Compression.ZipFile').Type) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

$zip = [System.IO.Compression.ZipFile]::OpenRead($fullZip)
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
    Assert-Check ($names -contains 'FlickD.exe') 'full ZIP 根目录含 FlickD.exe'
    Assert-Check (-not ($names -contains 'LanFlow.exe')) 'full ZIP 不含兼容壳 LanFlow.exe'
    $pdbEntries = @($names | Where-Object { $_ -like '*.pdb' })
    Assert-Check ($pdbEntries.Count -eq 0) "full ZIP 不含 PDB（实际 $($pdbEntries.Count) 个）"
    $ocrEntries = @($names | Where-Object { $_ -like 'ocr/*' -or $_ -like 'ocr\*' })
    Assert-Check ($ocrEntries.Count -eq 0) "full ZIP 不含内嵌 ocr/（实际 $($ocrEntries.Count) 个）"
    Assert-Check ($names.Count -gt 5) "full ZIP 含完整发布依赖（$($names.Count) 个条目）"
} finally { $zip.Dispose() }

$ocrArchive = [System.IO.Compression.ZipFile]::OpenRead($ocrZip)
try {
    $manifestEntry = $ocrArchive.GetEntry('manifest.json')
    Assert-Check ($null -ne $manifestEntry) 'OCR 包含 manifest.json'
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }

    Assert-Check ($manifest.files.Count -gt 0) "OCR manifest 列出 $($manifest.files.Count) 个文件"
    # 旧脚本宿主下 ZipArchive.GetEntry 对带 '/' 的条目名匹配不稳定（会把全部条目误判为缺失）；
    # 改为按 FullName 建索引并统一路径分隔符，逐文件校验仍按 manifest 的 sha256/size 做硬比对。
    $entryMap = @{}
    foreach ($entry in $ocrArchive.Entries) {
        $entryMap[([string]$entry.FullName).Replace('\', '/')] = $entry
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bad = @()
    foreach ($f in $manifest.files) {
        $path = [string]$f.path
        if (-not ($path -match '^(runtime|models|vcredist)/')) { $bad += "$($path):前缀非法"; continue }
        $key = $path.Replace('\', '/')
        $e = $entryMap[$key]
        if ($null -eq $e) { $bad += "$($key):缺少（索引 $($entryMap.Count) 条）"; continue }
        $s = $e.Open()
        try {
            $h = [System.BitConverter]::ToString($sha.ComputeHash($s)).Replace('-', '').ToLowerInvariant()
            if ($h -ne $f.sha256.ToLowerInvariant()) { $bad += "$($key):哈希不符" }
            if ($e.Length -ne $f.size) { $bad += "$($key):大小不符" }
        } finally { $s.Dispose() }
    }
    $sha.Dispose()
    Assert-Check ($bad.Count -eq 0) "OCR manifest 逐文件校验通过（异常: $($bad -join '; ')）"

    $concat = ($manifest.files | ForEach-Object { "$($_.path)|$($_.sha256)" }) -join "`n"
    $sha2 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $contentHash = [System.BitConverter]::ToString(
            $sha2.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($concat))
        ).Replace('-', '').ToLowerInvariant()
    } finally { $sha2.Dispose() }
    Assert-Check ($contentHash -eq $manifest.contentHash.ToLowerInvariant()) 'OCR manifest contentHash 一致'
} finally { $ocrArchive.Dispose() }

$fv = (Get-Item -LiteralPath $liteExe).VersionInfo.FileVersion
Assert-Check ($fv -eq "$Version.0") "lite PE FileVersion 为 $Version.0（实际 $fv）"

Write-Host "通过 $script:Checks 项校验；三条资产可用于发布。"

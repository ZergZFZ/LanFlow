[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [System.IO.Path]::GetFullPath($OutputPath)
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$outputDriveRoot = [System.IO.Path]::GetPathRoot($outputRoot)

if ($outputRoot.Equals($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath must be outside the private development repository.'
}

if ($outputRoot.Equals($outputDriveRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath must not be a drive root.'
}

if (Test-Path -LiteralPath $outputRoot) {
    if (-not $Force) {
        throw "OutputPath already exists: $outputRoot. Re-run with -Force only after confirming it is a disposable export directory."
    }
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$allowList = @(
    '.gitignore',
    'LICENSE',
    'README.md',
    'native/LanFlow.Core/LanFlow.Core.csproj',
    'native/LanFlow.Core/Models',
    'native/LanFlow.Core/Services',
    'native/LanFlow.Core/ViewModels',
    'native/LanFlow.Desktop/App.xaml',
    'native/LanFlow.Desktop/App.xaml.cs',
    'native/LanFlow.Desktop/AssemblyInfo.cs',
    'native/LanFlow.Desktop/GlobalUsings.cs',
    'native/LanFlow.Desktop/LanFlow.Desktop.csproj',
    'native/LanFlow.Desktop/MainWindow.xaml',
    'native/LanFlow.Desktop/MainWindow.xaml.cs',
    'native/LanFlow.Desktop/Assets',
    'native/LanFlow.Desktop/Properties/PublishProfiles',
    'native/LanFlow.Desktop/Services',
    'native/LanFlow.Desktop/Views'
)

foreach ($relativePath in $allowList) {
    $sourcePath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Required public-release path is missing: $relativePath"
    }

    $destinationPath = Join-Path $outputRoot $relativePath
    $destinationParent = Split-Path -Parent $destinationPath
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Recurse -Force
}

$approvedPathPatterns = @(
    '^\.gitignore$',
    '^LICENSE$',
    '^README\.md$',
    '^native/LanFlow\.Core/LanFlow\.Core\.csproj$',
    '^native/LanFlow\.Core/(Models|Services|ViewModels)/.+$',
    '^native/LanFlow\.Desktop/(App\.xaml|App\.xaml\.cs|AssemblyInfo\.cs|GlobalUsings\.cs|LanFlow\.Desktop\.csproj|MainWindow\.xaml|MainWindow\.xaml\.cs)$',
    '^native/LanFlow\.Desktop/(Assets|Properties/PublishProfiles|Services|Views)/.+$'
)

$exportedFiles = Get-ChildItem -LiteralPath $outputRoot -File -Recurse -Force
foreach ($file in $exportedFiles) {
    $relativePath = $file.FullName.Substring($outputRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Replace([System.IO.Path]::DirectorySeparatorChar, '/')
    if (-not ($approvedPathPatterns | Where-Object { $relativePath -match $_ })) {
        throw "Export contains a non-whitelisted file: $relativePath"
    }
}

$forbiddenFileNames = @(
    '^\.env(\..*)?$',
    '^config\.json(\.tmp)?$',
    '^launcher\.json$',
    '^appsettings(\..+)?\.json$',
    '^secrets\.json$'
)

foreach ($file in $exportedFiles) {
    if ($forbiddenFileNames | Where-Object { $file.Name -match $_ }) {
        throw "Export contains a forbidden local configuration file: $($file.FullName)"
    }
}

$credentialPattern = '(?i)(BEGIN (RSA|OPENSSH|EC|PRIVATE)|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|C:\\Users\\)'
$textExtensions = @('.cs', '.csproj', '.json', '.md', '.pubxml', '.xaml', '.xml', '.gitignore')
foreach ($file in $exportedFiles | Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() -or $_.Name -eq '.gitignore' }) {
    $matches = Select-String -LiteralPath $file.FullName -Pattern $credentialPattern -AllMatches
    if ($matches) {
        throw "Export contains blocked credential or local-path content: $($file.FullName)"
    }
}

Write-Output "Public release export verified: $outputRoot"
Write-Output "Files exported: $($exportedFiles.Count)"
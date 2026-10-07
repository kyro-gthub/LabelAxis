<#
    Makes a complete LabelAxis release in one folder, ready to publish for automatic updates:

      LabelAxis-Setup-<v>.exe           small installer (gets the .NET runtime from Microsoft if needed)
      LabelAxis-Setup-<v>-Offline.exe   installer with everything inside
      LabelAxis-<v>-portable.zip        runs from any folder or USB stick
      update.json                       what LabelAxis reads to find this version
      SHA256SUMS.txt                    fingerprints of the files above

    To publish: attach the folder's files to a new GitHub release (see docs/BUILD.md), or copy
    them to another update address (a web folder reached by
    https://, or a shared network folder). Copies of LabelAxis that look there find the new
    version within a day, or at once with Help › Check for updates.

    What's new goes in build\release-notes\<v>.en.txt (and .ar.txt / .ckb.txt for Arabic and
    Central Kurdish); the English text is used where a language has none.

    Usage:
      powershell -ExecutionPolicy Bypass -File build\release.ps1 [-OutputDir <folder>; default: artifacts\out] [-UpdateUrl https://example.com/labelaxis/]
#>
param(
    [string]$OutputDir = '',
    # Built into the app as its update address (for example a GitHub release folder). Empty: none built in; set it in Settings › Updates.
    [string]$UpdateUrl = ''
)

$ErrorActionPreference = 'Stop'
if (-not $OutputDir) { $OutputDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\out' }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$release = Join-Path $OutputDir "LabelAxis-$version-release"
New-Item -ItemType Directory -Path $release -Force | Out-Null

& (Join-Path $PSScriptRoot 'publish.ps1') -UpdateUrl $UpdateUrl
& (Join-Path $PSScriptRoot 'portable.ps1') -Version $version -OutputDir $release -SkipPublish
& (Join-Path $PSScriptRoot 'installer.ps1') -OutputDir $release -SkipPublish
& (Join-Path $PSScriptRoot 'ui-tests.ps1') -OutputDir $release

function Package([string]$name) {
    $file = Get-Item (Join-Path $release $name)
    [ordered]@{
        file   = $file.Name
        sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        size   = $file.Length
    }
}

function Notes([string]$language) {
    $path = Join-Path $root "build\release-notes\$version.$language.txt"
    if (Test-Path $path) { return (Get-Content $path -Raw -Encoding UTF8).Trim() }
    return $null
}

$feed = [ordered]@{
    version          = $version
    published        = (Get-Date -Format 'yyyy-MM-dd')
    notes            = Notes 'en'
    notesByLanguage  = [ordered]@{}
    installer        = Package "LabelAxis-Setup-$version.exe"
    offlineInstaller = Package "LabelAxis-Setup-$version-Offline.exe"
    portable         = Package "LabelAxis-$version-portable.zip"
}
foreach ($language in 'ar', 'ckb') {
    $text = Notes $language
    if ($text) { $feed.notesByLanguage[$language] = $text }
}

$json = $feed | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $release 'update.json'), $json + "`n", (New-Object System.Text.UTF8Encoding $false))

$sums = Get-ChildItem $release -File | Where-Object { $_.Name -notin 'update.json', 'SHA256SUMS.txt' } |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content (Join-Path $release 'SHA256SUMS.txt') $sums -Encoding ASCII

Write-Host ''
Write-Host "Release $version in $release" -ForegroundColor Green
Get-ChildItem $release -File | ForEach-Object { Write-Host ("  {0,-42} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }

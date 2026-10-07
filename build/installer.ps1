<#
    Builds the LabelAxis setup programs: publishes a release build, then compiles both
    Inno Setup installers with the version from Directory.Build.props.

      LabelAxis-Setup-<version>.exe          small: installs the .NET Desktop Runtime from
                                             Microsoft first when the PC does not have it
      LabelAxis-Setup-<version>-Offline.exe  everything inside, no download during setup

    Requires Inno Setup 6 (ISCC.exe on PATH, or in its usual install folders).
    Usage:  powershell -ExecutionPolicy Bypass -File build\installer.ps1 [-OutputDir <folder>; default: artifacts\out] [-SkipPublish] [-UpdateUrl https://…]
#>
param(
    [string]$OutputDir = '',
    [switch]$SkipPublish,
    [string]$UpdateUrl = ''
)

$ErrorActionPreference = 'Stop'
if (-not $OutputDir) { $OutputDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\out' }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1') -UpdateUrl $UpdateUrl
}

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

$candidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { $_ -and (Test-Path $_) }
$iscc = $candidates | Select-Object -First 1
if (-not $iscc) { throw 'ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php' }

# Files only the offline build has (its own copy of the .NET runtime). The small installer
# deletes them when it upgrades a copy installed with the offline one, because the program
# would otherwise load them instead of the shared runtime.
$full = Join-Path $root 'artifacts\app'
$shared = Join-Path $root 'artifacts\app-shared'
$sharedFiles = @{}
Get-ChildItem $shared -Recurse -File | ForEach-Object { $sharedFiles[$_.FullName.Substring($shared.Length + 1)] = $true }
$obsolete = Get-ChildItem $full -Recurse -File |
    ForEach-Object { $_.FullName.Substring($full.Length + 1) } |
    Where-Object { -not $sharedFiles.ContainsKey($_) } |
    Sort-Object
$lines = @('; Made by build\installer.ps1: files of the offline build that the shared-runtime build does not have.')
$lines += $obsolete | ForEach-Object { "Type: files; Name: ""{app}\$_""" }
Set-Content (Join-Path $root 'installer\obsolete-runtime-files.iss') $lines -Encoding UTF8
Write-Host ("{0} runtime files are removed when the small installer upgrades an offline install." -f $obsolete.Count)

$built = @()
foreach ($flavour in @(@{ Name = 'small'; Define = '/DShared'; File = "LabelAxis-Setup-$version.exe" },
                       @{ Name = 'offline'; Define = '/DOffline'; File = "LabelAxis-Setup-$version-Offline.exe" })) {
    Write-Host "== Compile $($flavour.Name) installer $version ==" -ForegroundColor Cyan
    & $iscc /Q "/DAppVersion=$version" $flavour.Define (Join-Path $root 'installer\LabelAxis.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }

    $setup = Join-Path $root "installer\Output\$($flavour.File)"
    Copy-Item $setup $OutputDir -Force
    $built += Get-Item (Join-Path $OutputDir $flavour.File)
}

foreach ($file in $built) {
    Write-Host ("Installer: {0} ({1:N1} MB)" -f $file.FullName, ($file.Length / 1MB)) -ForegroundColor Green
}

<#
    Builds the LabelAxis setup program: publishes a release build, then compiles the
    Inno Setup installer with the version from Directory.Build.props.

    Requires Inno Setup 6 (ISCC.exe on PATH, or in its usual install folders).
    Usage:  powershell -ExecutionPolicy Bypass -File build\installer.ps1 [-OutputDir E:\] [-SkipPublish]
#>
param(
    [string]$OutputDir = 'E:\',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1')
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

Write-Host "== Compile installer $version ==" -ForegroundColor Cyan
& $iscc /Q "/DAppVersion=$version" (Join-Path $root 'installer\LabelAxis.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }

$setup = Join-Path $root "installer\Output\LabelAxis-Setup-$version.exe"
Copy-Item $setup $OutputDir -Force
$size = (Get-Item $setup).Length / 1MB
Write-Host ("Installer: {0} ({1:N1} MB)" -f (Join-Path $OutputDir (Split-Path $setup -Leaf)), $size) -ForegroundColor Green

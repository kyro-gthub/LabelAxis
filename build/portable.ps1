<#
    Builds the portable package: a self-contained win-x64 app that runs from any
    folder (or a USB stick) with nothing to install and no runtime to download.

    The "portable.txt" marker the app looks for is written into the package, so a
    copy started from this folder keeps its settings, user templates, calibration
    and recovery snapshots in .\Data instead of %APPDATA%.

    Usage:  powershell -ExecutionPolicy Bypass -File build\portable.ps1 [-Version 1.0.0] [-OutputDir E:\]
#>
param(
    [string]$Version = '1.0.0',
    [string]$OutputDir = 'E:\'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$stageName = "LabelMaker-$Version-portable"
$stage = Join-Path $root "artifacts\$stageName"

Write-Host '== Publish (self-contained win-x64) ==' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'publish.ps1')

Write-Host '== Stage ==' -ForegroundColor Cyan
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item (Join-Path $root 'artifacts\app\*') $stage -Recurse -Force

# The marker that switches the app to keeping its data beside the executable.
@'
Delete this file if you would rather Label Maker kept its settings in
%APPDATA%\LabelMaker (the normal, installed-app location).

While this file is here, settings, user templates, printer calibration and
recovery snapshots all live in the "Data" folder next to LabelMaker.exe.
'@ | Set-Content (Join-Path $stage 'portable.txt') -Encoding UTF8

Copy-Item (Join-Path $root 'docs\PORTABLE-README.txt') (Join-Path $stage 'README.txt') -Force

Write-Host '== Zip ==' -ForegroundColor Cyan
$zip = Join-Path $OutputDir "$stageName.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$size = (Get-Item $zip).Length / 1MB
Write-Host ("Portable build: {0} ({1:N1} MB)" -f $zip, $size) -ForegroundColor Green

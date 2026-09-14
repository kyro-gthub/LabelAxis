<#
    Publish the app, then compile the Inno Setup installer.
    Requires Inno Setup 6 (ISCC.exe on PATH, or set $ISCC below).
    Usage:  pwsh build\installer.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

& (Join-Path $PSScriptRoot 'publish.ps1')

$ISCC = (Get-Command ISCC.exe -ErrorAction SilentlyContinue)?.Source
if (-not $ISCC) { $ISCC = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $ISCC)) {
    throw "ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
}

& $ISCC (Join-Path $root 'installer\LabelAxis.iss')
Write-Host "Installer written to installer\Output\" -ForegroundColor Green

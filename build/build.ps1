<#
    Clean + restore + Release build + test.
    Usage:  pwsh build\build.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== Clean ==" -ForegroundColor Cyan
Get-ChildItem -Recurse -Directory -Include bin, obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "== Restore ==" -ForegroundColor Cyan
dotnet restore LabelMaker.slnx

Write-Host "== Build (Release) ==" -ForegroundColor Cyan
dotnet build LabelMaker.slnx -c Release --no-restore

Write-Host "== Test ==" -ForegroundColor Cyan
dotnet test tests/LabelMaker.Tests/LabelMaker.Tests.csproj -c Release --no-build

Write-Host "OK" -ForegroundColor Green

<#
    Self-contained win-x64 publish of the app and CLI into ./artifacts.
    Usage:  pwsh build\publish.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$rid = 'win-x64'
$out = Join-Path $root 'artifacts'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "== Publish app ==" -ForegroundColor Cyan
dotnet publish src/LabelMaker.App/LabelMaker.App.csproj -c Release -r $rid `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o (Join-Path $out 'app')

Write-Host "== Publish CLI ==" -ForegroundColor Cyan
dotnet publish src/LabelMaker.Cli/LabelMaker.Cli.csproj -c Release -r $rid `
    --self-contained true `
    -o (Join-Path $out 'cli')

Write-Host "Published to $out" -ForegroundColor Green

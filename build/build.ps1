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
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

Write-Host "== Build (Release) ==" -ForegroundColor Cyan
dotnet build LabelMaker.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

# The engine's tests and the app's own; the click-through tests run on a test PC (docs\UI-TESTS.md).
foreach ($project in 'tests/LabelMaker.Tests/LabelMaker.Tests.csproj', 'tests/LabelMaker.App.Tests/LabelMaker.App.Tests.csproj') {
    Write-Host "== Test $([IO.Path]::GetFileNameWithoutExtension($project)) ==" -ForegroundColor Cyan
    dotnet test $project -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
}

Write-Host "OK" -ForegroundColor Green

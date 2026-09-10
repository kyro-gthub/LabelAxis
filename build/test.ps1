<#
    Run the test suite with line/branch coverage (coverlet) into ./coverage.
    Usage:  pwsh build\test.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet test tests/LabelMaker.Tests/LabelMaker.Tests.csproj `
    -c Release `
    --collect:"XPlat Code Coverage" `
    --results-directory coverage

Write-Host "Coverage XML written under .\coverage\" -ForegroundColor Green

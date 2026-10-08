<#
    Runs the engine's and the app's unit tests with line and branch coverage (coverlet) into
    .\coverage, and fails when a part of the program has fewer of its lines run by the tests than
    its minimum below, so coverage cannot quietly slip.
    Usage:  pwsh build\test.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Share of each part's lines the tests must run. Raise a number when the tests grow; never lower
# one to make a change pass. LabelAxis (the app) counts its windows too, which only the
# click-through tests reach, so its number is lower.
$minimums = [ordered]@{
    'LabelMaker.Core'   = 0.78
    'LabelMaker.Data'   = 0.70
    'LabelMaker.Export' = 0.75
    'LabelAxis'         = 0.10
}

$results = Join-Path $root 'coverage'
if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force }

$rates = @{}
foreach ($project in 'LabelMaker.Tests', 'LabelMaker.App.Tests') {
    $out = Join-Path $results $project
    dotnet test "tests/$project/$project.csproj" -c Release --collect:"XPlat Code Coverage" --results-directory $out
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }

    $file = Get-ChildItem -LiteralPath $out -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
    [xml]$report = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($package in $report.coverage.packages.package) {
        $rate = [double]::Parse($package.'line-rate', [Globalization.CultureInfo]::InvariantCulture)
        # Each part is measured by the tests written for it: the best of the two runs.
        if (-not $rates.ContainsKey($package.name) -or $rate -gt $rates[$package.name]) { $rates[$package.name] = $rate }
    }
}

$short = @()
foreach ($part in $minimums.Keys) {
    $rate = if ($rates.ContainsKey($part)) { $rates[$part] } else { 0 }
    $line = '{0,-18} {1,6:P1}  (minimum {2:P0})' -f $part, $rate, $minimums[$part]
    if ($rate -lt $minimums[$part]) { Write-Host $line -ForegroundColor Red; $short += $part } else { Write-Host $line }
}

if ($short.Count -gt 0) { throw "Test coverage is below its minimum for: $($short -join ', ')" }
Write-Host "Coverage XML written under .\coverage\" -ForegroundColor Green

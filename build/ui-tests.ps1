<#
    Packages the click-through tests for a separate test PC: artifacts\ui-tests, zipped as
    LabelAxis-<version>-ui-tests.zip. See docs\UI-TESTS.md for how to run them there.

    The package uses the .NET 10 Desktop Runtime, which the small LabelAxis installer puts on
    the test PC anyway. Never run the tests on a PC someone is working on.

    Usage:  powershell -ExecutionPolicy Bypass -File build\ui-tests.ps1 [-OutputDir <folder>; default: artifacts\out]
#>
param([string]$OutputDir = '')

$ErrorActionPreference = 'Stop'
if (-not $OutputDir) { $OutputDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\out' }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

$out = Join-Path $root 'artifacts\ui-tests'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish tests/LabelMaker.UiTests/LabelMaker.UiTests.csproj -c Release -r win-x64 --self-contained false `
    -p:DebugType=None -p:DebugSymbols=false -p:SatelliteResourceLanguages=en -nologo -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed for the UI tests' }
Copy-Item (Join-Path $root 'docs\UI-TESTS.md') (Join-Path $out 'README.md') -Force

$zip = Join-Path $OutputDir "LabelAxis-$version-ui-tests.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ("UI tests: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green

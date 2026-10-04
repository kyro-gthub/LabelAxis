<#
    Release build of the app and CLI into ./artifacts, ready to package.

    - Self-contained win-x64: nothing to install first, no .NET runtime to download.
    - ReadyToRun: the code is compiled ahead of time for x64, so the app starts faster
      (it does not have to compile itself on every start).
    - No debug symbols (.pdb), no createdump.exe (the .NET crash-dump helper, only used if
      crash dumps are switched on), English-only satellite resources.

    Usage:  powershell -ExecutionPolicy Bypass -File build\publish.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$rid = 'win-x64'
$out = Join-Path $root 'artifacts'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

$common = @(
    '-c', 'Release', '-r', $rid,
    '--self-contained', 'true',
    '-p:PublishReadyToRun=true',
    '-p:DebugType=None', '-p:DebugSymbols=false',
    '-p:SatelliteResourceLanguages=en',
    '-nologo'
)

function Publish([string]$project, [string]$target) {
    dotnet publish $project @common -p:PublishSingleFile=false -o $target
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }

    # Not needed by the app; leaving it out keeps the package to what LabelAxis uses.
    Get-ChildItem $target -Recurse -Include 'createdump.exe', '*.pdb' | Remove-Item -Force
}

Write-Host '== Publish app ==' -ForegroundColor Cyan
Publish 'src/LabelMaker.App/LabelMaker.App.csproj' (Join-Path $out 'app')

Write-Host '== Publish CLI ==' -ForegroundColor Cyan
Publish 'src/LabelMaker.Cli/LabelMaker.Cli.csproj' (Join-Path $out 'cli')

$size = (Get-ChildItem (Join-Path $out 'app') -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Published to {0} (app {1:N0} MB)" -f $out, $size) -ForegroundColor Green

<#
    Release builds of the app and CLI into ./artifacts, ready to package.

    artifacts\app         Self-contained win-x64: the .NET runtime is inside, nothing to install
                          first. For the offline installer and the portable zip.
    artifacts\app-shared  Uses the .NET Desktop Runtime shared by Windows apps. Much smaller;
                          the small installer installs that runtime first when a PC lacks it.
    artifacts\cli         The command-line tool (self-contained).

    - ReadyToRun: LabelAxis's own code is compiled ahead of time for x64, so it starts faster.
      The large libraries used only for Excel import and databases are left as they are
      (see LabelMaker.App.csproj), which keeps the download a good deal smaller.
    - No debug symbols (.pdb), no createdump.exe (the .NET crash-dump helper, only used if
      crash dumps are switched on), English-only satellite resources (the app's own
      translations are in its Languages folder).

    Usage:  powershell -ExecutionPolicy Bypass -File build\publish.ps1 [-UpdateUrl https://…/update.json]
#>
param(
    # Where the built app looks for updates (a web address or a shared folder). Optional:
    # without it, an address can still be entered in Settings › Updates.
    [string]$UpdateUrl = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$rid = 'win-x64'
$out = Join-Path $root 'artifacts'
# Only the folders this script fills: other files kept in artifacts (notes, launch material) stay.
foreach ($folder in 'app', 'app-shared', 'cli') {
    Remove-Item (Join-Path $out $folder) -Recurse -Force -ErrorAction SilentlyContinue
}

$common = @(
    '-c', 'Release', '-r', $rid,
    '-p:PublishReadyToRun=true',
    '-p:DebugType=None', '-p:DebugSymbols=false',
    '-p:SatelliteResourceLanguages=en',
    '-nologo'
)
if ($UpdateUrl) { $common += "-p:UpdateFeedUrl=$UpdateUrl" }

function Publish([string]$project, [string]$target, [bool]$selfContained) {
    dotnet publish $project @common --self-contained $selfContained.ToString().ToLowerInvariant() -p:PublishSingleFile=false -o $target
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }

    # Not needed by the app; leaving it out keeps the package to what LabelAxis uses.
    Get-ChildItem $target -Recurse -Include 'createdump.exe', '*.pdb' | Remove-Item -Force
}

Write-Host '== Publish app (runtime inside) ==' -ForegroundColor Cyan
Publish 'src/LabelMaker.App/LabelMaker.App.csproj' (Join-Path $out 'app') $true

Write-Host '== Publish app (shared runtime) ==' -ForegroundColor Cyan
Publish 'src/LabelMaker.App/LabelMaker.App.csproj' (Join-Path $out 'app-shared') $false

Write-Host '== Publish CLI ==' -ForegroundColor Cyan
Publish 'src/LabelMaker.Cli/LabelMaker.Cli.csproj' (Join-Path $out 'cli') $true

foreach ($name in 'app', 'app-shared') {
    $size = (Get-ChildItem (Join-Path $out $name) -Recurse | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ("Published {0} ({1:N0} MB)" -f $name, $size) -ForegroundColor Green
}

# Building & packaging

## Prerequisites

| Need | Version |
|---|---|
| OS | Windows 10 1809+ / Windows 11 (the WPF app is Windows-only) |
| .NET SDK | **10.0** (`dotnet --version` ≥ 10.0.100) |
| Inno Setup | 6.x (only for building the installer) — <https://jrsoftware.org/isinfo.php> |

The engine (`LabelMaker.Core`), export library and CLI target plain `net10.0` and
build on any OS; only `LabelMaker.App` requires Windows.

NuGet packages are centrally pinned in `Directory.Packages.props` and restored
from nuget.org on first build (internet required once).

## Common commands

```bash
# Restore + build everything (Debug)
dotnet build LabelMaker.slnx

# Release build
dotnet build LabelMaker.slnx -c Release

# Run the 130-test suite
dotnet test tests/LabelMaker.Tests -c Release

# Launch the designer
dotnet run --project src/LabelMaker.App -c Release

# Run the CLI
dotnet run --project src/LabelMaker.Cli -c Release -- templates list
```

`LabelMaker.slnx` is the new XML solution format; `dotnet build`/`test` accept it
directly. Older tooling can open the individual `.csproj` files.

## Helper scripts (`build/`)

```powershell
build\build.ps1     # clean + restore + Release build + test
build\test.ps1      # test only, with coverage (coverlet) into ./coverage
build\publish.ps1   # self-contained win-x64 publish of the app + CLI into ./artifacts
build\installer.ps1 # publish, then compile installer\LabelMaker.iss with ISCC
```

## Publishing the application

Self-contained, single-folder, no runtime install required by the end user:

```powershell
dotnet publish src/LabelMaker.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/app

dotnet publish src/LabelMaker.Cli -c Release -r win-x64 --self-contained true `
  -o artifacts/cli
```

`PublishSingleFile` is intentionally **off** for the app: WPF + PdfSharp font
handling is more predictable from a normal folder, and the installer lays the
files out anyway.

## Building the installer

1. Publish as above into `artifacts/app`.
2. Install Inno Setup 6 and ensure `ISCC.exe` is on `PATH` (or edit the path in
   `build/installer.ps1`).
3. Run:

   ```powershell
   ISCC installer\LabelMaker.iss
   ```

   Output: `installer\Output\LabelMaker-Setup-1.0.0.exe`.

The installer is **per-user** (`PrivilegesRequired=lowest`), adds a Start Menu
shortcut, registers the `.labelproj` file association with the app icon, and
writes nothing outside the user profile. Uninstall is via *Apps & features*.

## Versioning

Product version lives in `Directory.Build.props` (`<Version>`) and is read by the
installer script and `AssemblyInformationalVersion`. Bump it there.

## Troubleshooting

* **`NETSDK1045` / wrong SDK** — install the .NET 10 SDK; `global.json` is not
  used so the latest installed 10.x is picked.
* **`CS7064: Error opening icon file`** — `installer/assets/icon.ico` is missing;
  regenerate it (any 32×32 `.ico`) or clear `<ApplicationIcon>` in
  `src/LabelMaker.App/LabelMaker.App.csproj`.
* **PDF text looks wrong on a server** — `WindowsFontResolver` reads
  `C:\Windows\Fonts`; on a machine without the expected fonts it falls back to
  Arial. Install the fonts your labels use.
* **Restore fails offline** — the first build needs nuget.org; afterwards the
  `~/.nuget/packages` cache is enough.

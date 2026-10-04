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
build\publish.ps1   # release builds into ./artifacts: app (runtime inside), app-shared, cli
build\portable.ps1  # the portable zip
build\installer.ps1 # both installers (small and offline)
build\ui-tests.ps1  # the click-through test package for a separate test PC
build\release.ps1   # all of the above in one folder, with update.json and SHA256SUMS.txt
```

## Making a release

```powershell
build\release.ps1 -OutputDir E:\ -UpdateUrl https://example.com/labelaxis/
```

This publishes once and writes `E:\LabelAxis-<version>-release\`:

| File | What it is |
|---|---|
| `LabelAxis-Setup-<v>.exe` | The small installer. Installs the program against the shared Microsoft .NET 10 Desktop Runtime and, on a PC that lacks it, downloads it from Microsoft first (once per PC). |
| `LabelAxis-Setup-<v>-Offline.exe` | Everything inside; no download during setup. |
| `LabelAxis-<v>-portable.zip` | Runs from any folder or USB stick, settings kept beside it. |
| `LabelAxis-<v>-ui-tests.zip` | The click-through tests ([UI-TESTS.md](UI-TESTS.md)). |
| `update.json` | What copies of LabelAxis read to find this version: version, notes, file names, sizes and SHA-256 fingerprints. |
| `SHA256SUMS.txt` | Fingerprints of every file. |

What's new is taken from `build\release-notes\<v>.en.txt`, `.ar.txt` and `.ckb.txt`.

### Automatic updates

`-UpdateUrl` is built into the app as its update address; without it, users (or an
administrator) enter one in *Settings › Updates*. The address can be:

* a web folder reached by **https://** (plain http is refused), for example
  `https://example.com/labelaxis/` or a GitHub release's
  `https://github.com/<owner>/<repo>/releases/latest/download/` folder;
* a **shared network folder**, for example `\server\share\LabelAxis`.

To publish a release, copy the release folder's files there (overwriting the previous
`update.json`). Each copy of LabelAxis checks about once a day: a newer version brings up
a bar with *Update now*. LabelAxis downloads the setup program, checks its size and SHA-256
against `update.json` (a mismatch, or a missing fingerprint, is refused), closes, runs it
silently (`/SILENT /RELAUNCH=1`) and starts again. A copy installed with the offline
installer updates with the offline installer; portable copies are pointed at the new zip.

## Publishing the application

`build\publish.ps1` does this. By hand:

```powershell
dotnet publish src/LabelMaker.App -c Release -r win-x64 --self-contained true  -p:PublishReadyToRun=true -o artifacts/app
dotnet publish src/LabelMaker.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/app-shared
```

ReadyToRun compiles LabelAxis's own code ahead of time for a quick start; the large Excel
and database libraries are left out of it (`PublishReadyToRunExclude` in the app's project
file), which makes every package noticeably smaller. `PublishSingleFile` is intentionally
**off**: WPF + PdfSharp font handling is more predictable from a normal folder, and the
installer lays the files out anyway.

## Building the installer

1. Install Inno Setup 6 (`ISCC.exe` on `PATH`, or in its usual folders).
2. Run `build\installer.ps1` (or `build\release.ps1`). It publishes, lists the files that
   only the offline build has (`installer\obsolete-runtime-files.iss`: the small installer
   removes them when it upgrades an offline install), and compiles both installers:

   ```powershell
   ISCC /DAppVersion=1.1.0 /DShared  installer\LabelAxis.iss   # small
   ISCC /DAppVersion=1.1.0 /DOffline installer\LabelAxis.iss   # offline
   ```

The installer is **per-user** by default (`PrivilegesRequired=lowest`; the first page
offers all users), adds a Start Menu shortcut and registers the `.labelproj` file
association with the app icon. Uninstall is via *Apps & features*. When the small
installer has to install the Microsoft .NET 10 Desktop Runtime, Microsoft's own installer
asks Windows for administrator rights for that step (the runtime is shared by all users).

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

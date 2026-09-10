; Inno Setup script for Label Maker (spec §44)
; Build:  ISCC installer\LabelMaker.iss
; Expects a published app in ..\artifacts\app  (see build\installer.ps1)

#define AppName        "Label Maker"
#define AppVersion     "1.0.0"
#define AppPublisher   "Label Maker Project"
#define AppExeName     "LabelMaker.exe"
#define AppId          "{{7F3B9A54-2C1E-4E6A-9E2D-4B1C7F0A9D31}"
#define SourceDir      "..\artifacts\app"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install — no administrator rights required for normal operation.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=LabelMaker-Setup-{#AppVersion}
SetupIconFile=assets\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "associate"; Description: "Associate .labelproj project files with {#AppName}"; GroupDescription: "File associations:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; .labelproj association (per-user under HKCU\Software\Classes)
Root: HKCU; Subkey: "Software\Classes\.labelproj"; ValueType: string; ValueName: ""; ValueData: "LabelMaker.Project"; Flags: uninsdeletevalue; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\LabelMaker.Project"; ValueType: string; ValueName: ""; ValueData: "Label Maker Project"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\LabelMaker.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\LabelMaker.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User data (settings, templates, calibration, recovery) is left in %APPDATA%\LabelMaker
; and %LOCALAPPDATA%\LabelMaker on purpose so a reinstall keeps preferences.
Type: filesandordirs; Name: "{app}"

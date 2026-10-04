; Inno Setup script for LabelAxis.
; Build with build\installer.ps1, which publishes the app first and passes the version:
;   ISCC /DAppVersion=1.0.0 installer\LabelAxis.iss
; Expects the published app in ..\artifacts\app (build\publish.ps1).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName        "LabelAxis"
#define AppPublisher   "LabelAxis"
#define AppExeName     "LabelAxis.exe"
#define AppDescription "Label design and printing"
; Never change the AppId: Windows uses it to recognise an upgrade of an installed copy.
#define AppId          "{{7F3B9A54-2C1E-4E6A-9E2D-4B1C7F0A9D31}"
#define SourceDir      "..\artifacts\app"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright © 2026 {#AppPublisher}
AppComments={#AppDescription}
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=Copyright © 2026 {#AppPublisher}

; Install for the current user without administrator rights by default; the user can
; choose "all users" (which asks for administrator rights) on the first page.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

; 64-bit Windows 10 (1607) or later, like the .NET runtime it carries.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.14393

; Look and feel
WizardStyle=modern
WizardSizePercent=110
WizardImageFile=assets\wizard-large-100.bmp,assets\wizard-large-150.bmp,assets\wizard-large-200.bmp
WizardSmallImageFile=assets\wizard-small-100.bmp,assets\wizard-small-150.bmp,assets\wizard-small-200.bmp
SetupIconFile=assets\icon.ico
ShowLanguageDialog=auto

; Apps & features entry
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

; Upgrades: close a running LabelAxis first (Windows Restart Manager), never reboot.
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes

; Small download: strongest compression, one solid block.
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=4

OutputDir=Output
OutputBaseFilename=LabelAxis-Setup-{#AppVersion}

; Code signing (when a certificate is available): define a sign tool in the Inno Setup
; compiler (Tools > Configure Sign Tools), for example named "labelaxis" as
;   signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /f "C:\path\cert.pfx" /p PASSWORD $f
; then remove the semicolons from the two lines below. Setup and uninstaller are both signed.
;SignTool=labelaxis
;SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate"; Description: "Open .labelproj project files with {#AppName}"; GroupDescription: "File types:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "{#AppDescription}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "{#AppDescription}"; Tasks: desktopicon

[Registry]
; .labelproj files (HKA: the current user's classes, or the machine's for an all-users install)
Root: HKA; Subkey: "Software\Classes\.labelproj"; ValueType: string; ValueName: ""; ValueData: "LabelAxis.Project"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.labelproj\OpenWithProgids"; ValueType: string; ValueName: "LabelAxis.Project"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\LabelAxis.Project"; ValueType: string; ValueName: ""; ValueData: "LabelAxis project"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\LabelAxis.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\LabelAxis.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".labelproj"; ValueData: ""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Settings, templates, calibration and recovery snapshots in %APPDATA% / %LOCALAPPDATA%
; are the user's own and are kept when LabelAxis is uninstalled.

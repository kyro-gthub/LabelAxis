; Inno Setup script for LabelAxis.
; Build with build\installer.ps1 (or build\release.ps1), which publishes the app first and
; passes the version:
;   ISCC /DAppVersion=1.1.0 installer\LabelAxis.iss            the offline installer
;   ISCC /DAppVersion=1.1.0 /DShared installer\LabelAxis.iss   the small installer
;
; The offline installer carries the .NET runtime inside the program folder (..\artifacts\app).
; The small one installs the program without it (..\artifacts\app-shared) and, on a PC that
; does not have the .NET 10 Desktop Runtime yet, downloads it from Microsoft and installs it
; first. Most of the download is that runtime, and a PC needs it only once, so every later
; update is small.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName        "LabelAxis"
#define AppPublisher   "LabelAxis"
#define AppExeName     "LabelAxis.exe"
#define AppDescription "Label design and printing"
; Never change the AppId: Windows uses it to recognise an upgrade of an installed copy.
#define AppId          "{{7F3B9A54-2C1E-4E6A-9E2D-4B1C7F0A9D31}"
#ifdef Shared
  #define SourceDir    "..\artifacts\app-shared"
  #define SetupName    "LabelAxis-Setup-" + AppVersion
#else
  #define SourceDir    "..\artifacts\app"
  #define SetupName    "LabelAxis-Setup-" + AppVersion + "-Offline"
#endif

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
OutputBaseFilename={#SetupName}

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

[CustomMessages]
english.RuntimeTitle=Getting .NET for LabelAxis
english.RuntimeDescription=LabelAxis needs the Microsoft .NET 10 Desktop Runtime. It is being downloaded from Microsoft and installed; this happens only once on this PC.
english.RuntimeDownloadFailed=The Microsoft .NET 10 Desktop Runtime could not be downloaded. Check the internet connection and run Setup again, or use the offline installer (LabelAxis-Setup-{#AppVersion}-Offline.exe), which needs no download.
english.RuntimeInstallFailed=The Microsoft .NET 10 Desktop Runtime could not be installed (error %1). Run Setup again, or use the offline installer (LabelAxis-Setup-{#AppVersion}-Offline.exe).
arabic.RuntimeTitle=تنزيل ‎.NET‎ لبرنامج LabelAxis
arabic.RuntimeDescription=يحتاج LabelAxis إلى Microsoft .NET 10 Desktop Runtime. يجري تنزيله من Microsoft وتثبيته؛ ويحدث ذلك مرة واحدة فقط على هذا الحاسوب.
arabic.RuntimeDownloadFailed=تعذّر تنزيل Microsoft .NET 10 Desktop Runtime. تحقّق من الاتصال بالإنترنت وشغّل الإعداد مرة أخرى، أو استخدم المثبّت دون اتصال (LabelAxis-Setup-{#AppVersion}-Offline.exe) الذي لا يحتاج إلى تنزيل.
arabic.RuntimeInstallFailed=تعذّر تثبيت Microsoft .NET 10 Desktop Runtime (الخطأ %1). شغّل الإعداد مرة أخرى، أو استخدم المثبّت دون اتصال (LabelAxis-Setup-{#AppVersion}-Offline.exe).

#ifdef Shared
[InstallDelete]
; Upgrading a copy installed with the offline installer: its own copy of the .NET runtime
; would be loaded instead of the shared one, so those files go (the list is made by
; build\installer.ps1 from the two builds). Nothing else in the folder is touched.
#include "obsolete-runtime-files.iss"
#endif

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
; An update started from LabelAxis (/RELAUNCH=1) starts it again when it is done.
Filename: "{app}\{#AppExeName}"; Flags: nowait runasoriginaluser; Check: RelaunchAfterUpdate

[Code]
function RelaunchAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

#ifdef Shared
const
  RuntimeUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  RuntimeFile = 'windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

{ The .NET 10 Desktop Runtime is installed for all users under Program Files\dotnet. }
function DesktopRuntimeInstalled: Boolean;
var
  Found: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), Found) then
  begin
    try
      repeat
        if (Found.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          Result := True;
      until Result or not FindNext(Found);
    finally
      FindClose(Found);
    end;
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(CustomMessage('RuntimeTitle'), CustomMessage('RuntimeDescription'), nil);
end;

{ Downloads the runtime from Microsoft and installs it; returns an error message, or '' when done. }
function InstallDesktopRuntime: String;
var
  ResultCode: Integer;
begin
  Result := '';
  try
    if WizardSilent then
      DownloadTemporaryFile(RuntimeUrl, RuntimeFile, '', nil)
    else
    begin
      DownloadPage.Clear;
      DownloadPage.Add(RuntimeUrl, RuntimeFile, '');
      DownloadPage.Show;
      try
        DownloadPage.Download;
      finally
        DownloadPage.Hide;
      end;
    end;
  except
    Result := CustomMessage('RuntimeDownloadFailed') + #13#10#13#10 + GetExceptionMessage;
    Exit;
  end;

  { Microsoft's installer asks Windows for administrator rights itself; 3010 = done, restart
    later; 1638 = a newer one is already there. }
  if not ShellExec('', ExpandConstant('{tmp}\') + RuntimeFile, '/install /passive /norestart', '',
                   SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    Result := FmtMessage(CustomMessage('RuntimeInstallFailed'), [IntToStr(ResultCode)])
  else if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638) then
    Result := FmtMessage(CustomMessage('RuntimeInstallFailed'), [IntToStr(ResultCode)])
  else if not DesktopRuntimeInstalled then
    Result := FmtMessage(CustomMessage('RuntimeInstallFailed'), [IntToStr(ResultCode)]);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not DesktopRuntimeInstalled then
    Result := InstallDesktopRuntime;
end;
#endif

{ Settings, templates, calibration and recovery snapshots in %APPDATA% / %LOCALAPPDATA%
  are the user's own and are kept when LabelAxis is uninstalled. }

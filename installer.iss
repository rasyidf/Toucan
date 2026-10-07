; Toucan Inno Setup Installer Script
; Build with: iscc installer.iss (after running publish.ps1)

#define AppName "Toucan"
; publish.ps1 passes /DAppVersion from Directory.Build.props; this is the fallback.
#ifndef AppVersion
  #define AppVersion "0.20.0"
#endif
#define AppPublisher "rasyid.dev"
#define AppURL "https://github.com/rasyidf/Toucan"
#define AppExeName "Toucan.exe"

[Setup]
AppId={{B7A3F2E1-8C4D-4F5A-9B6E-2D1C3A4F5B6E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE.txt
OutputDir=publish\installer
OutputBaseFilename=ToucanSetup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=Toucan.Avalonia\Assets\WindowIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fileassoc"; Description: "Associate .tproj files with Toucan"; GroupDescription: "File associations:"
Name: "contextmenu"; Description: "Add 'Open with Toucan' to folder context menu"; GroupDescription: "Shell integration:"

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; .tproj file association
Root: HKCU; Subkey: "Software\Classes\.tproj"; ValueType: string; ValueName: ""; ValueData: "Toucan.Project"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Toucan.Project"; ValueType: string; ValueName: ""; ValueData: "Toucan Translation Project"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Toucan.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Toucan.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: fileassoc

; Folder context menu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Toucan"; ValueType: string; ValueName: ""; ValueData: "Open with Toucan"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Toucan"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Toucan\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%V"""; Tasks: contextmenu

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

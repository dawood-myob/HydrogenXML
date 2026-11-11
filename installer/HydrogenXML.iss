; Inno Setup script for HydrogenXML. Build with build-installer.ps1 (or ISCC.exe /DAppVersion=1.0.0 HydrogenXML.iss).
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "HydrogenXML"
#define AppExe "HydrogenXML.exe"

[Setup]
AppId={{6F1B7C52-3D0A-4E8B-9C41-5A2E7D90B1A4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=HydrogenXML
AppPublisherURL=https://github.com/dawood-myob/HydrogenXML
AppSupportURL=https://github.com/dawood-myob/HydrogenXML/issues
AppUpdatesURL=https://github.com/dawood-myob/HydrogenXML/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installs per-user by default (no admin prompt); users can choose "all users" in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=HydrogenXML-Setup-{#AppVersion}
SetupIconFile=..\src\HydrogenXML\Assets\HydrogenXML.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

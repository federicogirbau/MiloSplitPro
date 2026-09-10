; Milo Split Pro - Inno Setup Script
#define MyAppName "Milo Split Pro"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MILOsoft"
#define MyAppURL "https://github.com/federicogirbau/MiloSplitPro"
#define MyAppExeName "MiloSplitPro.App.exe"

[Setup]
AppId={{D37B5924-4E49-4A1B-9D41-83BE1A13A36F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\MILOsoft\{#MyAppName}
DefaultGroupName=MILOsoft
DisableProgramGroupPage=no
LicenseFile=..\THIRD_PARTY_NOTICES.txt
SetupIconFile=..\src\MiloSplitPro.App\Assets\app_icon.ico
OutputDir=..\dist
OutputBaseFilename=MiloSplitPro_Setup_v1.0.0_x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app_icon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app_icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent


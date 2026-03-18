#define MyAppName "MC LCE TP Editor"
#define MyAppVersion "0.3"
#define MyAppExeName "LegacyConsolePackEditor.exe"
#ifndef MySourceDir
  #define MySourceDir "bin\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
AppId={{C8E7B3C2-4D3F-4B6A-9A1B-9F9D11B0A001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}

AppPublisher=TNT_addict
AppPublisherURL=https://github.com/TNTaddicted
AppSupportURL=https://github.com/TNTaddicted/MCLCE-Texture-Pack-Editor
AppUpdatesURL=https://github.com/TNTaddicted/MCLCE-Texture-Pack-Editor/releases

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

OutputDir=.
OutputBaseFilename=LegacyConsolePackEditor-Setup

Compression=lzma
SolidCompression=yes

SetupIconFile=Cactus_1.13.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

WizardStyle=modern
WizardSmallImageFile=Cactus_1.13.png

DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
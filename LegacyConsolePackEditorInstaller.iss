#define MyAppName "MC LCE TP Editor"
#define MyAppVersion "0.1"
#define MySourceDir "C:\Users\{user}\minecraft-legacy-editor\LegacyConsolePackEditor\bin\Release\net8.0-windows"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
Compression=lzma
SolidCompression=yes
OutputDir=.
OutputBaseFilename=LegacyConsolePackEditor-Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]

Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs promptifolder

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\LegacyConsolePackEditor.exe"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\LegacyConsolePackEditor.exe"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

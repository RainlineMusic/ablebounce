#define AppVersion "0.1.7"
[Setup]
AppId={{5B40FCEB-6F41-45B0-8F40-C7589C647C7D}
AppName=glucose
AppVersion={#AppVersion}
AppPublisher=Rainline Music
AppPublisherURL=https://github.com/RainlineMusic/ablebounce
DefaultDirName={localappdata}\Programs\glucose
DefaultGroupName=glucose
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=glucose-Setup-0.1.7-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\glucose.exe
[Files]
Source: "..\artifacts\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\glucose"; Filename: "{app}\glucose.exe"
Name: "{autodesktop}\glucose"; Filename: "{app}\glucose.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
[Run]
Filename: "{app}\glucose.exe"; Description: "Open glucose"; Flags: nowait postinstall skipifsilent


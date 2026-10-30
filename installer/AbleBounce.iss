#define AppVersion "0.1.0"
[Setup]
AppId={{5B40FCEB-6F41-45B0-8F40-C7589C647C7D}
AppName=AbleBounce
AppVersion={#AppVersion}
AppPublisher=Rainline Music
AppPublisherURL=https://github.com/RainlineMusic/ablebounce
DefaultDirName={localappdata}\Programs\AbleBounce
DefaultGroupName=AbleBounce
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=AbleBounce-Setup-0.1.0-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\AbleBounce.exe
[Files]
Source: "..\artifacts\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\AbleBounce"; Filename: "{app}\AbleBounce.exe"
Name: "{autodesktop}\AbleBounce"; Filename: "{app}\AbleBounce.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
[Run]
Filename: "{app}\AbleBounce.exe"; Description: "Open AbleBounce"; Flags: nowait postinstall skipifsilent

#ifndef PayloadDir
  #error PayloadDir must point to the packaged Windows release directory
#endif
#ifndef OutputDir
  #define OutputDir "..\out\installer"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B8D8DA9C-2829-49BA-9BC1-B265008E93D7}
AppName=WiseShell
AppVersion={#AppVersion}
AppPublisher=WiseShell
DefaultDirName={localappdata}\Programs\WiseShell
DefaultGroupName=WiseShell
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=WiseShell-{#AppVersion}-windows-x64-Setup
SetupIconFile=..\assets\logo.ico
UninstallDisplayIcon={app}\WiseShellCpp.exe
LicenseFile={#PayloadDir}\licenses\WiseShell-MIT.txt
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=WiseShell
VersionInfoVersion={#AppVersion}

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WiseShell"; Filename: "{app}\WiseShellCpp.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,WiseShell}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\WiseShell"; Filename: "{app}\WiseShellCpp.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WiseShellCpp.exe"; Description: "{cm:LaunchProgram,WiseShell}"; Flags: nowait postinstall skipifsilent

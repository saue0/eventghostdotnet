; Installateur EventGhost .NET pour Inno Setup 6.3 ou plus récent.
; Compilé par build\publish.ps1, qui définit MyArch, SourceDir, OutputDir,
; AppVersion et OutputBaseFilename.

#ifndef MyArch
  #define MyArch "x64compatible"
#endif

#ifndef SourceDir
  #define SourceDir "..\bin\publish\win-x64-framework-dependent"
#endif

#ifndef OutputDir
  #define OutputDir "..\bin\installer"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "EventGhostDotNet-setup"
#endif

[Setup]
AppId={{B2D4F7A1-6C3E-4A57-9E1B-8C2D5F0A7B64}
AppName=EventGhost .NET
AppVersion={#AppVersion}
AppVerName=EventGhost .NET {#AppVersion}
AppPublisher=EventGhostDotNet
DefaultDirName={autopf}\EventGhostDotNet
DefaultGroupName=EventGhost .NET
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\Assets\EventGhostDotNet.ico
UninstallDisplayIcon={app}\EventGhostDotNet.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#MyArch}
ArchitecturesInstallIn64BitMode={#MyArch}
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.17763

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\EventGhost .NET"; Filename: "{app}\EventGhostDotNet.exe"
Name: "{autodesktop}\EventGhost .NET"; Filename: "{app}\EventGhostDotNet.exe"; Tasks: desktopicon

; Source du journal des événements Windows utilisée pour les exceptions fatales.
; EventLogMessages.dll fournit le message générique qui affiche le texte de l'entrée.
[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Services\EventLog\Application\EventGhostDotNet"; ValueType: string; ValueName: "EventMessageFile"; ValueData: "{sys}\Microsoft.NET\Framework64\v4.0.30319\EventLogMessages.dll"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Services\EventLog\Application\EventGhostDotNet"; ValueType: dword; ValueName: "TypesSupported"; ValueData: "7"

[Run]
Filename: "{app}\EventGhostDotNet.exe"; Description: "{cm:LaunchProgram,EventGhost .NET}"; Flags: nowait postinstall skipifsilent

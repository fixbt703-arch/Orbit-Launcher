#define AppName "Orbit Launcher"
#define AppExeName "OrbitLauncher.exe"
#define AppPublisher "Orbit Launcher"
#ifndef OrbitBuildDir
  #define OrbitBuildDir SourcePath + "..\installer-staging"
#endif
#define AppVersion GetFileVersion(OrbitBuildDir + "\OrbitLauncher.exe")

[Setup]
AppId={{3FEAE432-8BE8-4E8C-A0F3-DB99A41F981B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppComments=Космический Minecraft Launcher для Windows
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Установщик Orbit Launcher
VersionInfoProductName={#AppName}
VersionInfoCopyright=MIT License — Orbit Launcher contributors
DefaultDirName={autopf}\Orbit Launcher
DefaultGroupName=Orbit Launcher
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=OrbitLauncherInstaler
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
InternalCompressLevel=max
WizardStyle=modern dynamic stellar includetitlebar
WizardSizePercent=110
DisableWelcomePage=no
DisableReadyPage=no
DisableFinishedPage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#OrbitBuildDir}\OrbitLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Orbit Launcher"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Orbit Launcher"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,Orbit Launcher}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

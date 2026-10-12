; One4All ERP desktop installer (Inno Setup 6). Build with installer/build.ps1, which publishes the app first.
#define AppVersion GetEnv("ONE4ALL_VERSION")
#if AppVersion == ""
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{6F2B8C1E-4A3D-4B8E-9C47-0E4A1D7B2F31}
AppName=One4All ERP
AppVersion={#AppVersion}
AppPublisher=One4All
DefaultDirName={autopf}\One4All
DefaultGroupName=One4All ERP
OutputDir=..\artifacts\installer
OutputBaseFilename=One4All-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
; An update installs over the previous version; the settings beside the app are kept.
UsePreviousAppDir=yes

[Files]
Source: "..\artifacts\publish\desktop\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.Development.json"
Source: "..\artifacts\publish\desktop\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{group}\One4All ERP"; Filename: "{app}\One4All.Desktop.exe"
Name: "{autodesktop}\One4All ERP"; Filename: "{app}\One4All.Desktop.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
Filename: "{app}\One4All.Desktop.exe"; Description: "Start One4All"; Flags: nowait postinstall skipifsilent

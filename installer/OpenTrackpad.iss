#define AppName "OpenTrackpad"
#define AppVersion "0.1.0"
#define AppPublisher "OpenTrackpad"
#define DriverDir "..\.cache\v2.3-amd64\AMD64"

[Setup]
AppId={{A7B5C9E1-4F3A-4E2B-9C1D-8F6A2D7B3E10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\OpenTrackpad
DefaultGroupName=OpenTrackpad
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=output
OutputBaseFilename=OpenTrackpad-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\app\OpenTrackpad.App\app.ico
SetupLogging=yes
CloseApplications=no
RestartApplications=no
UsedUserAreasWarning=no
UninstallDisplayIcon={app}\OpenTrackpad.App.exe
VersionInfoVersion=0.1.0.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "scripts\*.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\GPL-2.0.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

#if FileExists(AddBackslash(DriverDir) + "AmtPtpDevice.inf")
Source: "{#DriverDir}\*"; DestDir: "{tmp}\driver"; Flags: deleteafterinstall
#endif

[Icons]
Name: "{group}\OpenTrackpad"; Filename: "{app}\OpenTrackpad.App.exe"
Name: "{group}\Driver status"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Test-DriverStatus.ps1"""; WorkingDir: "{app}"
Name: "{autodesktop}\OpenTrackpad"; Filename: "{app}\OpenTrackpad.App.exe"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Stop-App.ps1"""; Flags: runhidden waituntilterminated; StatusMsg: "Stopping OpenTrackpad..."

#if FileExists(AddBackslash(DriverDir) + "AmtPtpDevice.inf")
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Install-Driver.ps1"" -DriverPath ""{tmp}\driver"""; Flags: runhidden waituntilterminated; StatusMsg: "Installing the Magic Trackpad driver..."
#endif

Filename: "{app}\OpenTrackpad.App.exe"; Description: "Launch OpenTrackpad"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Stop-App.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "StopOpenTrackpad"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\OpenTrackpad"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OpenTrackpad"; ValueData: """{app}\OpenTrackpad.App.exe"""; Flags: uninsdeletevalue

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ScriptPath: String;
begin
  Result := '';
  ScriptPath := ExpandConstant('{app}\scripts\Stop-App.ps1');
  if FileExists(ScriptPath) then
  begin
    Exec(
      'powershell.exe',
      '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    MsgBox(
      'The Magic Trackpad driver was kept.' + #13#10 +
      'To remove it, run Uninstall-Driver.ps1 from an elevated PowerShell.' + #13#10 +
      'The app settings in %LOCALAPPDATA%\OpenTrackpad were removed.',
      mbInformation, MB_OK);
end;

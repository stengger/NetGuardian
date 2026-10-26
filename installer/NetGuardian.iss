; 通过 scripts/build-installer.ps1 传入版本、干净发布目录和输出目录。
#ifndef AppVersion
  #error 必须指定 AppVersion
#endif
#ifndef PackageDirectory
  #error 必须指定 PackageDirectory
#endif

[Setup]
AppId={{5DE3E29B-A247-4A86-B139-B522DAB21872}
AppName=NetGuardian
AppVersion={#AppVersion}
AppPublisher=NetGuardian
AppPublisherURL=https://github.com/stengger/NetGuardian
AppSupportURL=https://github.com/stengger/NetGuardian/issues
DefaultDirName={autopf}\NetGuardian
DefaultGroupName=NetGuardian
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDirectory}
OutputBaseFilename=NetGuardian-{#AppVersion}-win-x64-Setup
SetupIconFile={#SourcePath}..\src\NetGuardian.App\Assets\app.ico
UninstallDisplayIcon={app}\NetGuardian.exe
AppMutex=Global\NetGuardian.UI
CloseApplications=no
RestartApplications=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#PackageDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "{#PackageDirectory}\scripts\installer-maintenance.ps1"; Flags: dontcopy

[Icons]
Name: "{group}\NetGuardian"; Filename: "{app}\NetGuardian.exe"; WorkingDir: "{app}"
Name: "{group}\卸载 NetGuardian"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NetGuardian"; Filename: "{app}\NetGuardian.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Code]
function RunMaintenance(const Mode, ScriptPath, Directory: String): String;
var
  ExitCode: Integer;
  ErrorPath, Arguments: String;
  ErrorText: AnsiString;
begin
  ErrorPath := ExpandConstant('{tmp}\netguardian-maintenance-error.txt');
  DeleteFile(ErrorPath);
  Arguments := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath +
    '" -Mode ' + Mode + ' -InstallDirectory "' + Directory + '" -ErrorFile "' + ErrorPath + '"';
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE,
      ewWaitUntilTerminated, ExitCode) and (ExitCode = 0) then
    Result := ''
  else begin
    Result := '无法安全准备 NetGuardian，请先退出界面并停止后台，再重试。';
    if LoadStringFromFile(ErrorPath, ErrorText) then
      Result := UTF8Decode(ErrorText);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExtractTemporaryFile('installer-maintenance.ps1');
  Result := RunMaintenance('CheckInstall', ExpandConstant('{tmp}\installer-maintenance.ps1'), WizardDirValue);
end;

function InitializeUninstall: Boolean;
var
  Error: String;
begin
  Result := False;
  if SuppressibleMsgBox('卸载将安全停止本目录的后台任务，并保留配置和日志。是否继续？',
      mbConfirmation, MB_YESNO, IDYES) <> IDYES then Exit;
  Error := RunMaintenance('Uninstall', ExpandConstant('{app}\scripts\installer-maintenance.ps1'), ExpandConstant('{app}'));
  if Error <> '' then
    SuppressibleMsgBox(Error, mbError, MB_OK, IDOK)
  else
    Result := True;
end;

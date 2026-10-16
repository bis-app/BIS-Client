; BIS Client – Inno Setup 6
; Ausgabe: installer\output\BISClientSetup.exe
; Per-User-Installation ohne Administratorrechte
;   Ziel: %LOCALAPPDATA%\Programs\BIS Client
;
; Vor dem Kompilieren:
;   dotnet publish src\BISClient\BISClient.csproj -c Release -p:PublishProfile=Win64
; Optional offline: MicrosoftEdgeWebView2Setup.exe nach installer\redist\ legen.

#define MyAppName "BIS Client"
#define MyAppVersion "1.1.5"
#define MyAppPublisher "BIS"
#define MyAppExeName "BISClient.exe"
#define MyAppId "{{B7E1C92A-4F18-4D6B-9A3C-8E5F21D47A90}}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=BISClientSetup
SetupIconFile=..\src\BISClient\Resources\App.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
DisableWelcomePage=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json,appsettings.Local.json"
Source: "..\publish\win-x64\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "redist\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist; Check: not IsWebView2RuntimeInstalled

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[UninstallDelete]
Type: files; Name: "{userappdata}\Microsoft\Windows\SendTo\BIS Import-Ordner.lnk"

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-AppxPackage -Name 'BIS.BISClient' | Remove-AppxPackage"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveIdentityPackage"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Microsoft WebView2 Runtime wird installiert..."; Flags: waituntilterminated skipifdoesntexist; Check: not IsWebView2RuntimeInstalled
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifnotsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

function IsWebView2RuntimeInstalled: Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
    begin
      Result := True;
      exit;
    end;
  end;
  if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
    begin
      Result := True;
      exit;
    end;
  end;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
      Result := True;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    exit;
  if FileExists(ExpandConstant('{app}\BIS.Identity.cer')) then
  begin
    Exec(
      ExpandConstant('{sys}\certutil.exe'),
      '-user -f -addstore TrustedPeople "' + ExpandConstant('{app}\BIS.Identity.cer') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Download abgeschlossen: %s', [FileName]));
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  InstallerPath: String;
begin
  Result := True;
  if CurPageID <> wpReady then
    exit;
  if IsWebView2RuntimeInstalled then
    exit;

  InstallerPath := ExpandConstant('{tmp}\MicrosoftEdgeWebView2Setup.exe');
  if FileExists(InstallerPath) then
    exit;

  DownloadPage.Clear;
  DownloadPage.Add('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebView2Setup.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      Result := True;
    except
      if DownloadPage.AbortedByUser then
        Log('Download der WebView2 Runtime wurde abgebrochen.')
      else
        SuppressibleMsgBox(
          'Die Microsoft Edge WebView2 Runtime konnte nicht heruntergeladen werden.' + #13#10 +
          'Bitte installieren Sie die Runtime manuell:' + #13#10 +
          'https://go.microsoft.com/fwlink/p/?LinkId=2124703',
          mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;

  if Result and FileExists(InstallerPath) then
  begin
    if not Exec(InstallerPath, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      SuppressibleMsgBox(
        'Die Microsoft Edge WebView2 Runtime konnte nicht installiert werden.',
        mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  end;
end;

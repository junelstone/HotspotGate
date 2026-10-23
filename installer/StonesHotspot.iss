; Stone's Hotspot — customer installer (Inno Setup 6). Built by: .\build.ps1 -Installer
; Contains ONLY the published service and tray app (checked by tools\Test-ReleaseContent.ps1 before this runs).
; Never add the LicenseManager, the LicenseIssuer, key files or tools\dev-keys here.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#define AppName "Stone's Hotspot"
#define ServiceName "StonesHotspot"
#define FirewallGroup "Stone's Hotspot"

[Setup]
AppId={{6F1C2B7E-4F0A-4C55-9C6B-5B1E0D7A2C11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Stone's IT Consulting
DefaultDirName={autopf}\Stone's Hotspot
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=StonesHotspot-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\StonesHotspot.Tray.exe
SetupLogging=yes

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
fr.DeleteData=Supprimer aussi les données du hotspot (base de données, sauvegardes, journaux) dans %1 ?%n%nChoisissez Non pour les garder (réinstallation, changement de PC). La licence et l'essai sont toujours conservés.
en.DeleteData=Also delete the hotspot data (database, backups, logs) in %1?%n%nChoose No to keep it (reinstall, moving to another PC). The licence and trial are always kept.
fr.StartingService=Démarrage du service Stone's Hotspot…
en.StartingService=Starting the Stone's Hotspot service…
fr.OpenDashboard=Ouvrir le tableau de bord (première installation)
en.OpenDashboard=Open the dashboard (first-time setup)

[Dirs]
; Data stays outside Program Files and survives upgrades.
Name: "{commonappdata}\StonesHotspot"; Flags: uninsneveruninstall

[Files]
Source: "{#PublishDir}\StonesHotspot.Service\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\StonesHotspot.Tray\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\third-party.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\StonesHotspot.Tray.exe"; Parameters: "--dashboard"; Comment: "Tableau de bord / Dashboard"
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\StonesHotspot.Tray.exe"; Parameters: "--dashboard"

[Registry]
; Tray icon for every user at sign-in.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StonesHotspotTray"; ValueData: """{app}\StonesHotspot.Tray.exe"""; Flags: uninsdeletevalue

[Run]
; The service: LocalSystem, automatic (delayed) start, restarted 5 s / 10 s / 30 s after a crash (counter reset after a day).
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\StonesHotspot.Service.exe\"""" start= delayed-auto DisplayName= ""{#AppName}"""; Flags: runhidden; Check: not ServiceExists
Filename: "{sys}\sc.exe"; Parameters: "config {#ServiceName} binPath= ""\""{app}\StonesHotspot.Service.exe\"""" start= delayed-auto"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Hotspot Wi-Fi et portail captif / Wi-Fi hotspot and captive portal"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failureflag {#ServiceName} 1"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden; StatusMsg: "{cm:StartingService}"
Filename: "{app}\StonesHotspot.Tray.exe"; Flags: nowait runasoriginaluser skipifsilent
Filename: "{app}\StonesHotspot.Tray.exe"; Parameters: "--dashboard"; Description: "{cm:OpenDashboard}"; Flags: nowait postinstall runasoriginaluser skipifsilent

[UninstallRun]
; Stopping the service switches internet sharing off and closes the packet gate.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM StonesHotspot.Tray.exe"; Flags: runhidden; RunOnceId: "KillTray"
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\timeout.exe"; Parameters: "/T 5 /NOBREAK"; Flags: runhidden; RunOnceId: "WaitService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-NetFirewallRule -Group '{#FirewallGroup}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule"""; Flags: runhidden; RunOnceId: "Firewall"

[Code]
function ServiceExists: Boolean;
var
  Code: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

{ Upgrade: stop the tray and the service before files are replaced; data and settings are not touched. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code, I: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM StonesHotspot.Tray.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if ServiceExists then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    { wait up to 30 s for the process to exit (find returns 0 while it is still listed) }
    for I := 1 to 30 do
    begin
      if not Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq StonesHotspot.Service.exe" /NH | find /I "StonesHotspot.Service" >NUL', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
        Break;
      Sleep(1000);
    end;
  end;
  Result := '';
end;

{ Uninstall: ask (default No) before deleting the data. The licence/trial records in
  %ProgramData%\StonesHotspot.Licence and HKLM\SOFTWARE\StonesHotspot are never deleted. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{commonappdata}\StonesHotspot');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox(FmtMessage(CustomMessage('DeleteData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;

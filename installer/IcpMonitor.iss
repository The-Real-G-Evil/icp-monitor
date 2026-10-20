; Installer fuer ICP Monitor (Inno Setup 6). Bauen ueber build-installer.ps1 im Hauptordner.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{6E0B9C7A-4D52-4C1E-9F1B-2A7D8E3C5B41}
AppName=ICP Monitor
AppVersion={#AppVersion}
AppVerName=ICP Monitor {#AppVersion}
DefaultDirName={autopf}\ICP Monitor
DefaultGroupName=ICP Monitor
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=ICP-Monitor-Setup-{#AppVersion}
SetupIconFile=..\src\IcpMonitor\Assets\app.ico
UninstallDisplayIcon={app}\IcpMonitor.exe
UninstallDisplayName=ICP Monitor
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Die laufende App beendet der [Code]-Abschnitt; der Restart Manager erreicht Tray-Apps nicht zuverlaessig.
CloseApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
de.Autostart=Mit Windows starten (mit Administratorrechten, ohne Nachfrage)
en.Autostart=Start with Windows (with administrator rights, without prompt)
de.LaunchApp=ICP Monitor jetzt starten
en.LaunchApp=Launch ICP Monitor now

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ICP Monitor"; Filename: "{app}\IcpMonitor.exe"
Name: "{autodesktop}\ICP Monitor"; Filename: "{app}\IcpMonitor.exe"; Tasks: desktopicon

[Run]
; Autostart = Aufgabe in der Aufgabenplanung, angelegt von der App selbst (gleicher Weg wie im Tray-Menue).
Filename: "{app}\IcpMonitor.exe"; Parameters: "--autostart on"; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{app}\IcpMonitor.exe"; Parameters: "--autostart off"; Flags: runhidden waituntilterminated; Tasks: not autostart
Filename: "{app}\IcpMonitor.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
; Update aus der App heraus (/SILENT): danach automatisch wieder starten.
Filename: "{app}\IcpMonitor.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{app}\IcpMonitor.exe"; Parameters: "--autostart off"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAutostart"

[Code]
// Laufende Instanz beenden, damit die Dateien ersetzt bzw. entfernt werden koennen.
procedure StopRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM IcpMonitor.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp;
  Result := True;
end;

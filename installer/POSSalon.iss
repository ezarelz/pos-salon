; =====================================================================================================
; POS Salon — Inno Setup script (Inno Setup 6.x)
; Build:  .\build-installer.ps1            (tests → dotnet publish → this script)
;    or:  ISCC.exe /DMyAppVersion=2.0.0 installer\POSSalon.iss   (after dotnet publish -p:PublishProfile=win-x64)
;
; Decisions (CLAUDE.md "Rilis"): framework-dependent (.NET 10 Desktop Runtime x64 required, checked below),
; per-machine install in Program Files, exe POSSalon.exe.
; DATA IS NEVER TOUCHED: the database lives in %LOCALAPPDATA%\MSPASalon of each Windows user. Install, upgrade and
; uninstall only manage program files. The app itself backs up the database before a schema upgrade.
; =====================================================================================================

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "POS Salon"
#define MyAppExe "POSSalon.exe"
#define PublishDir "..\publish\win-x64"
#define RuntimeUrl "https://dotnet.microsoft.com/download/dotnet/10.0"

[Setup]
; AppId must NEVER change: it is how a new version upgrades the installed one instead of installing twice.
AppId={{6F0B8C52-3E1D-4A7B-9C55-2D8E4B1F7A30}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppName}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\POS Salon
DefaultGroupName=POS Salon
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=POSSalon-Setup-{#MyAppVersion}
SetupIconFile=..\src\MSPASalon.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Close a running POS Salon before files are replaced (upgrade).
CloseApplications=yes
RestartApplications=no

[Languages]
; Inno Setup ships no official Indonesian translation; wizard texts are English, our own messages are Indonesian.
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat shortcut di Desktop"; GroupDescription: "Shortcut:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; runasoriginaluser: start the app as the logged-in cashier, not as the elevated installer account,
; so it opens THEIR database in their %LOCALAPPDATA%.
Filename: "{app}\{#MyAppExe}"; Description: "Jalankan POS Salon"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function IsDesktopRuntime10Installed(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Answer, ErrorCode: Integer;
begin
  Result := True;
  if IsDesktopRuntime10Installed() then Exit;
  if WizardSilent() then
  begin
    Log('.NET 10 Desktop Runtime (x64) not found; continuing silent install.');
    Exit;
  end;
  Answer := MsgBox(
    'POS Salon membutuhkan .NET 10 Desktop Runtime (x64), dan runtime itu belum terpasang di komputer ini.' + #13#10 + #13#10 +
    'Yes  = buka halaman unduhan Microsoft (pilih ".NET Desktop Runtime 10 – Windows x64"), lalu jalankan installer ini lagi.' + #13#10 +
    'No   = tetap pasang sekarang (aplikasi baru bisa dibuka setelah runtime dipasang).' + #13#10 +
    'Cancel = batalkan.',
    mbConfirmation, MB_YESNOCANCEL);
  if Answer = IDYES then
  begin
    ShellExec('open', '{#RuntimeUrl}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end
  else if Answer = IDCANCEL then
    Result := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent()) then
    MsgBox('Program POS Salon sudah dihapus.' + #13#10 + #13#10 +
      'Data salon (database, backup, log) TIDAK ikut dihapus dan tetap ada di folder:' + #13#10 +
      '%LOCALAPPDATA%\MSPASalon' + #13#10 + #13#10 +
      'Bila memasang ulang, data tersebut langsung terbaca kembali.', mbInformation, MB_OK);
end;

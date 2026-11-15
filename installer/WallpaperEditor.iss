; Installer for WallpaperEditor, built with Inno Setup 6.
;
;   iscc /DAppVersion=0.1.0 /DNumericVersion=0.1.0 /DPublishDir=<publish folder> /DOutputDir=<output> WallpaperEditor.iss
;
; The release workflow passes the version from the tag. Run from the repository, the defaults expect the app
; published to artifacts\WallpaperEditor, as the workflow does.
;
; - Asks whether to install for the current user only (AppData\Local\Programs, no administrator, the default) or for
;   everyone (Program Files, needs an administrator). The folder can be changed either way.
; - Asks for the redundancy folder, where the app keeps a second copy of its state and of the original wallpapers.
;   The app works on %LOCALAPPDATA%\WallpaperEditor; this folder is only read back if that one loses its state.
; - Uninstalling stops the companion, takes it off the startup list and asks before deleting any data. Projects are
;   never deleted.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\WallpaperEditor"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "WallpaperEditor"
#define AppExe "WallpaperEditor.App.exe"

[Setup]
; Fixed forever: Windows finds the existing installation by it, for updates and for uninstalling.
AppId={{573EBC83-436B-4D1A-AF43-BEF99E6F7D68}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Rafael Sakamoto
VersionInfoVersion={#NumericVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
UsePreviousAppDir=yes
; The redundancy folder and its setting belong to the user who installs, also when installing for everyone.
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\src\WallpaperEditor.App\assets\neko-purple-light.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
; Updating closes the app and the companion through the Restart Manager; the companion is started again below.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.MirrorTitle=Pasta de redundância
brazilianportuguese.MirrorSubtitle=Onde guardar uma segunda cópia dos dados do app
brazilianportuguese.MirrorText=O app trabalha em %LOCALAPPDATA%\WallpaperEditor e mantém nesta pasta uma cópia do que não dá para refazer: o estado de cada monitor e as cópias dos wallpapers originais. Se a pasta do app se perder, ele restaura daqui. Os projetos ficam onde você os criar.
brazilianportuguese.RuntimeMissing=O .NET Desktop Runtime 10 não foi encontrado, e o WallpaperEditor só abre com ele instalado.%n%nAbrir a página de download? A instalação continua de qualquer forma.
brazilianportuguese.DeleteData=Apagar também os dados do WallpaperEditor?%n%n• %1%n• o estado e as cópias dos originais em %2%n%nOs projetos não são apagados. Sem esses dados, não dá mais para restaurar os wallpapers originais pelo app.
english.MirrorTitle=Redundancy folder
english.MirrorSubtitle=Where to keep a second copy of the app's data
english.MirrorText=The app works in %LOCALAPPDATA%\WallpaperEditor and keeps in this folder a copy of what cannot be rebuilt: each monitor's state and the copies of the original wallpapers. If the app's folder is lost, it restores from here. Projects stay wherever you create them.
english.RuntimeMissing=The .NET Desktop Runtime 10 was not found, and WallpaperEditor only opens with it installed.%n%nOpen the download page? Setup continues either way.
english.DeleteData=Also delete WallpaperEditor's data?%n%n• %1%n• the state and original copies in %2%n%nProjects are not deleted. Without this data, the app can no longer restore the original wallpapers.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\WallpaperEditor"; ValueType: string; ValueName: "MirrorFolder"; ValueData: "{code:GetMirrorFolder}"; Flags: uninsdeletekey

[Run]
; The companion checks for itself: with a dynamic wallpaper on screen it stays and registers from the installed
; folder; without one it ends at once. As the user who ran setup, also when installing for everyone.
Filename: "{app}\{#AppExe}"; Parameters: "--companion"; Flags: nowait runasoriginaluser
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Before any file goes: the companion ends and leaves the startup list.
Filename: "{app}\{#AppExe}"; Parameters: "--stop-companion"; Flags: waituntilterminated skipifdoesntexist; RunOnceId: "StopCompanion"

[Code]
var
  MirrorPage: TInputDirWizardPage;
  MirrorAtUninstall: String;

function DefaultMirrorFolder: String;
begin
  Result := ExpandConstant('{%USERPROFILE}\.wallpaperEditor');
end;

function DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    try
      repeat
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not DesktopRuntimeInstalled then
  begin
    if SuppressibleMsgBox(CustomMessage('RuntimeMissing'), mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExecAsOriginalUser('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

procedure InitializeWizard;
begin
  MirrorPage := CreateInputDirPage(wpSelectDir, CustomMessage('MirrorTitle'), CustomMessage('MirrorSubtitle'),
    CustomMessage('MirrorText'), False, '');
  MirrorPage.Add('');
  MirrorPage.Values[0] := GetPreviousData('MirrorFolder', DefaultMirrorFolder);
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'MirrorFolder', MirrorPage.Values[0]);
end;

function GetMirrorFolder(Param: String): String;
begin
  Result := MirrorPage.Values[0];
end;

{ Only what the app writes there: the folder may have been chosen somewhere with other files in it. }
procedure DeleteMirror(Folder: String);
begin
  DeleteFile(AddBackslash(Folder) + 'state.json');
  DelTree(AddBackslash(Folder) + 'published', True, True, True);
  RemoveDir(Folder);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Read before the uninstaller removes the setting. }
    if not RegQueryStringValue(HKCU, 'Software\WallpaperEditor', 'MirrorFolder', MirrorAtUninstall) then
      MirrorAtUninstall := DefaultMirrorFolder;

    { In case the companion could not be reached, the entry still goes. }
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WallpaperEditor');
  end;

  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    if MsgBox(FmtMessage(CustomMessage('DeleteData'), [ExpandConstant('{localappdata}\WallpaperEditor'), MirrorAtUninstall]),
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\WallpaperEditor'), True, True, True);
      DeleteMirror(MirrorAtUninstall);
    end;
  end;
end;

; Per-user installer for Hearthsheet. Built by scripts/publish.ps1:
;   ISCC /DAppVersion=X.Y.Z /DSourceDir=<self-contained publish folder> /O<output> packaging/Hearthsheet.iss
; In-app updates replace {app} wholesale (Hearthsheet.Updater), so the uninstaller lives in a sibling folder
; and the app refreshes DisplayVersion itself (InstallRegistration).

#ifndef AppVersion
  #error Pass /DAppVersion=X.Y.Z
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<publish folder>
#endif

[Setup]
; Never change AppId: upgrades and the Windows Apps entry depend on it. InstallRegistration.UninstallKey matches it.
AppId={{5A4DFE70-9A1C-405F-842C-01D7DA9F8911}
AppName=Hearthsheet
AppVersion={#AppVersion}
AppVerName=Hearthsheet {#AppVersion}
AppPublisher=Hearthsheet
AppPublisherURL=https://github.com/BDesro/Hearthsheet
DefaultDirName={localappdata}\Programs\Hearthsheet
UninstallFilesDir={localappdata}\Programs\Hearthsheet Uninstall
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
CloseApplications=yes
OutputBaseFilename=Hearthsheet-Setup
SetupIconFile=..\src\Hearthsheet.App\Assets\Hearthsheet.ico
UninstallDisplayIcon={app}\Hearthsheet.exe
UninstallDisplayName=Hearthsheet
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"

[InstallDelete]
; Clears whatever was there before (an Install.cmd install, or files an older version had), never user data.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\Hearthsheet"; Filename: "{app}\Hearthsheet.exe"; WorkingDir: "{app}"; Comment: "D&D 5e character manager"
Name: "{userdesktop}\Hearthsheet"; Filename: "{app}\Hearthsheet.exe"; WorkingDir: "{app}"; Comment: "D&D 5e character manager"; Tasks: desktopicon

[Run]
Filename: "{app}\Hearthsheet.exe"; Description: "Launch Hearthsheet"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; In-app updates add files Inno never installed, so remove the folders wholesale.
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\Programs\Hearthsheet.backup"

[Code]
// The Application:DataDirectory override from appsettings.user.json, or '' if none.
// ponytail: substring scan, not a JSON parser; non-ASCII paths may display garbled (they're only shown, never deleted).
function CustomDataDirectory(): String;
var
  Json: AnsiString;
  Text: String;
  P: Integer;
begin
  Result := '';
  if not LoadStringFromFile(ExpandConstant('{localappdata}\Hearthsheet\appsettings.user.json'), Json) then Exit;
  Text := String(Json);
  P := Pos('"DataDirectory"', Text);
  if P = 0 then Exit;
  Delete(Text, 1, P + Length('"DataDirectory"') - 1);
  Text := TrimLeft(Text);
  if Copy(Text, 1, 1) <> ':' then Exit;
  Text := TrimLeft(Copy(Text, 2, MaxInt));
  if Copy(Text, 1, 1) <> '"' then Exit;
  Text := Copy(Text, 2, MaxInt);
  P := Pos('"', Text);
  if P = 0 then Exit;
  Result := Copy(Text, 1, P - 1);
  StringChangeEx(Result, '\\', '\', True);
  Result := Trim(Result);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Root, Custom, Msg: String;
  KeepRoot: Boolean;
  Rc: Integer;
begin
  if (CurUninstallStep <> usPostUninstall) or UninstallSilent then Exit;
  Root := ExpandConstant('{localappdata}\Hearthsheet');
  Custom := CustomDataDirectory();
  // A custom folder inside the default one would go with it, so then only the sign-in is removed.
  KeepRoot := (Custom <> '') and (Pos(Lowercase(AddBackslash(Root)), Lowercase(AddBackslash(Custom))) = 1);

  Msg := 'Also delete your characters, settings and backups?' + #13#10#13#10 +
    'Your characters will be gone for good unless you exported them or use cloud sync. ' +
    'Choose No to keep them for a future reinstall.';
  if Custom <> '' then
    Msg := Msg + #13#10#13#10 + 'Your custom data folder is not deleted: ' + Custom;

  if MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    if not KeepRoot then DelTree(Root, True, True, True);
    Exec(ExpandConstant('{sys}\cmdkey.exe'), '/delete:Hearthsheet/supabase-session', '', SW_HIDE, ewWaitUntilTerminated, Rc);
  end;
end;

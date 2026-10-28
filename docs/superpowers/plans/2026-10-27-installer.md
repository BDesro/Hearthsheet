# Installer and Safe Upgrades Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a one-file `Hearthsheet-Setup.exe` (wizard, desktop-shortcut checkbox, uninstaller that asks about data) and make every database or character-format upgrade back up the database first.

**Architecture:** Inno Setup compiles a per-user installer from the same self-contained publish folder that becomes the update zip. In-app updates keep using the zip and `Hearthsheet.Updater`. The uninstaller sits outside the install folder so updates can't delete it, and the app refreshes the *Apps* version at startup. `SqliteCharacterRepository.Initialize()` writes a dedicated pre-upgrade backup and refuses to upgrade if the backup fails.

**Tech Stack:** .NET 10 / WPF, Microsoft.Data.Sqlite, xUnit, Inno Setup 6 (build-time only), PowerShell, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-27-installer-design.md`

## Global Constraints

- Branch `feature/installer` (from `dev`); PR into `dev`, squash. No version bump or `RELEASE_NOTES.md` change in this PR (that's the release PR).
- No new NuGet packages. Inno Setup is a build tool, not a dependency.
- Inno `AppId`: `{{5A4DFE70-9A1C-405F-842C-01D7DA9F8911}` (in `.iss` syntax). The resulting uninstall registry key is `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{5A4DFE70-9A1C-405F-842C-01D7DA9F8911}_is1`. Never change it.
- Install folder `{localappdata}\Programs\Hearthsheet`; uninstaller folder `{localappdata}\Programs\Hearthsheet Uninstall`; the updater's backup sibling is `{localappdata}\Programs\Hearthsheet.backup`.
- Release assets: `Hearthsheet-X.Y.Z-win-x64.zip`, `Hearthsheet-Setup.exe`, `SHA256SUMS.txt` listing both (`<sha256-lowercase>  <name>` lines).
- Stable download link: `https://github.com/BDesro/Hearthsheet/releases/latest/download/Hearthsheet-Setup.exe`.
- Data folder `%LOCALAPPDATA%\Hearthsheet`; saved cloud sign-in is the generic credential `Hearthsheet/supabase-session`.
- `.ps1`/`.cmd` stay CRLF. Every user-facing string about data deletion defaults to keeping data.

## Review Focus

- **Hearthsheet running during setup or uninstall.** Expected: setup asks to close it (Restart Manager), never half-copies. Pinned by the manual check in Task 4.
- **Setup run over a 1.4.0 install made with `Install.cmd`** (no registry entry, framework-dependent files, old shortcuts). Expected: clean in-place upgrade, one Start menu shortcut, stale files gone. Pinned by `[InstallDelete]` in Task 2 and the manual check in Task 4.
- **Uninstall after an in-app update added files Inno never installed.** Expected: the install folder and `Hearthsheet.backup` are fully removed. Pinned by `[UninstallDelete]` in Task 2 and the manual check in Task 4.
- **Pre-upgrade backup on a database that already has newer-version data refused.** Expected: no backup written, the existing "newer version" error. Pinned by `NewerDatabase_IsRefusedWithoutBackup` in Task 1.
- **Custom `Application:DataDirectory` plus "delete my data".** Expected: the custom folder is never deleted, and the prompt names it. Pinned by the manual check in Task 4.

---

### Task 1: Pre-upgrade database backup

**Files:**
- Modify: `src/Hearthsheet.Infrastructure/Persistence/SqliteCharacterRepository.cs` (constructor, `Initialize()`)
- Modify: `src/Hearthsheet.App/App.xaml.cs` (`Compose`: pass `paths.Backups`)
- Modify: `docs/ARCHITECTURE.md` §7
- Test: `tests/Hearthsheet.Tests/PersistenceTests.cs` (new class `UpgradeBackupTests`)

**Interfaces:**
- Produces: `public SqliteCharacterRepository(string databasePath, ILogger<SqliteCharacterRepository> logger, string? backupDirectory = null)`. With `null`, no pre-upgrade backup is taken (tests only; the app always passes `paths.Backups`).
- Produces: backup files named `pre-upgrade-db{fromDb}-schema{fromSchema}-{yyyyMMdd-HHmmss}.db` in `backupDirectory`. The `pre-upgrade-` prefix keeps them out of `DatabaseBackups.RunDaily`'s `characters-*.db` rotation.
- Produces: a new `meta` row `last_schema_version` (string int), set to `CharacterJson.CurrentSchemaVersion` after any upgrade or backup check.

Behaviour of `Initialize()`, in order:
1. Remember `existed = File.Exists(databasePath)` before opening.
2. Create tables (as today). Read `db_version`; throw on newer, as today, **before** any backup.
3. Read `last_schema_version` (missing → `0` on an existing database).
4. If `existed`, `backupDirectory != null`, and (`current < DatabaseVersion` or `lastSchema < CharacterJson.CurrentSchemaVersion`): call `BackupTo(...)` with the name above. If it throws, log an error and rethrow as `InvalidOperationException("Hearthsheet could not back up your characters before upgrading them, so nothing was changed. " + ex.Message, ex)`. This surfaces through the existing "Hearthsheet could not start" dialog in `App.OnStartup`.
5. Run `UpgradeTo2` etc. as today, then upsert `last_schema_version`.
6. On a brand-new database: no backup; write `db_version` and `last_schema_version` as current.

- [ ] **Step 1: Write the failing tests** in `UpgradeBackupTests` (use `TempDir`, and build the v1 database the same way `SyncStoreTests.UpgradeFromVersion1_KeepsRowsAndMarksThemUnsynced` does; extract that SQL into a shared `internal static void CreateVersion1Database(string path, Character c)` helper in `PersistenceTests.cs` and use it from both):

```csharp
[Fact] public void UpgradeFromVersion1_WritesBackupFirst()
// arrange v1 db with Samples.Rich(); new repo(db, logger, backups)
// Assert.Single(Directory.GetFiles(backups, "pre-upgrade-db1-*.db"))
// open that file with SqliteConnection: SELECT value FROM meta WHERE key='db_version' == "1"; COUNT(*) FROM characters == 1
// repo.Load(c.Id)!.Identity.Name == "Arannis"

[Fact] public void BackupFailure_LeavesDatabaseAtVersion1()
// backups = a path whose parent is a *file* (File.WriteAllText(_dir.File("blocker"), ""); backups = _dir.File("blocker") + "\\sub")
// Assert.Throws<InvalidOperationException>(() => new SqliteCharacterRepository(db, logger, backups))
// db_version in the file is still "1"; characters table has no 'dirty' column (PRAGMA table_info)

[Fact] public void NoUpgradeNeeded_WritesNoBackup()
// new repo on fresh db (no backup: brand new), dispose, new repo again → Directory.GetFiles(backups, "pre-upgrade-*") is empty

[Fact] public void SchemaVersionRaise_BacksUpOnce()
// fresh repo; set meta last_schema_version to (CurrentSchemaVersion - 1) via SqliteConnection
// new repo → exactly one "pre-upgrade-db2-schema{Current-1}-*.db"; new repo again → still one

[Fact] public void NewerDatabase_IsRefusedWithoutBackup()
// v1 db then UPDATE meta db_version = '99' → Assert.Throws<InvalidOperationException>; no files in backups
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~UpgradeBackupTests"`
Expected: build error (no 3-argument constructor), then FAILs once it compiles.

- [ ] **Step 3: Implement** the constructor parameter and the `Initialize()` order above. Pass `paths.Backups` in `App.Compose`.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test Hearthsheet.slnx`
Expected: all pass, including `SyncStoreTests.UpgradeFromVersion1_KeepsRowsAndMarksThemUnsynced`.

- [ ] **Step 5: Document** in `docs/ARCHITECTURE.md` §7: replace "There are no migrations yet." with the rule that every upgrade step is transactional, `Initialize()` writes a `pre-upgrade-…` backup before any db-version or character-schema upgrade and refuses to upgrade if that fails, and there are no downgrade paths (newer data is refused).

- [ ] **Step 6: Commit**

```bash
git add src/Hearthsheet.Infrastructure/Persistence/SqliteCharacterRepository.cs src/Hearthsheet.App/App.xaml.cs tests/Hearthsheet.Tests/PersistenceTests.cs tests/Hearthsheet.Tests/SyncStoreTests.cs docs/ARCHITECTURE.md
git commit -m "Back up the database before any schema upgrade"
```

---

### Task 2: Self-contained package and Inno Setup installer

**Files:**
- Create: `packaging/Hearthsheet.iss`
- Delete: `packaging/Install.cmd`, `packaging/Install.ps1`
- Modify: `packaging/Dependencies.ps1` (keep only what `scripts/install.ps1` needs for the SDK check)
- Modify: `scripts/publish.ps1`, `scripts/install.ps1`, `.github/workflows/release.yml`
- Modify: `README.md` (Requirements, Install, Packaging sections), `docs/ARCHITECTURE.md` §1 table row, §9, §11 Privilege row, §14, `CLAUDE.md` (publish command comment)

**Interfaces:**
- Consumes: the AppId and folder names from Global Constraints.
- Produces: `scripts/publish.ps1` outputs `artifacts/Hearthsheet-<v>-win-x64.zip`, `artifacts/Hearthsheet-Setup.exe`, `artifacts/SHA256SUMS.txt` (two lines). `UpdateService`'s contract is unchanged.

- [ ] **Step 1: `publish.ps1`.** Publish the app with `--self-contained true` (drop the "framework-dependent" comment). Publish the updater `--self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true` into the same folder. Stop copying `packaging/*` into the stage. After the zip, find `ISCC.exe` in `${env:ProgramFiles(x86)}\Inno Setup 6`, then `$env:LOCALAPPDATA\Programs\Inno Setup 6` (where `winget install JRSoftware.InnoSetup` puts it). If it isn't found, throw `"Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup"`. Run `ISCC /DAppVersion=$Version "/DSourceDir=$stage" "/O$artifacts" "$root/packaging/Hearthsheet.iss"` (check `$LASTEXITCODE`). Write SHA256SUMS for both files.

- [ ] **Step 2: `packaging/Hearthsheet.iss`.** Required settings (the rest is the implementer's call):

```ini
[Setup]
AppId={{5A4DFE70-9A1C-405F-842C-01D7DA9F8911}
AppName=Hearthsheet
AppVersion={#AppVersion}
AppPublisher=Hearthsheet
DefaultDirName={localappdata}\Programs\Hearthsheet
UninstallFilesDir={localappdata}\Programs\Hearthsheet Uninstall
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
CloseApplications=yes
OutputBaseFilename=Hearthsheet-Setup
SetupIconFile=..\src\Hearthsheet.App\Assets\Hearthsheet.ico
UninstallDisplayIcon={app}\Hearthsheet.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"   ; no Flags: unchecked → ticked by default

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"          ; clears an old Install.cmd / framework-dependent install

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\Hearthsheet"; Filename: "{app}\Hearthsheet.exe"   ; same place Install.ps1 used, so it overwrites
Name: "{userdesktop}\Hearthsheet"; Filename: "{app}\Hearthsheet.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Hearthsheet.exe"; Description: "Launch Hearthsheet"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\Programs\Hearthsheet.backup"
```

`[Code]`: in `CurUninstallStepChanged(usPostUninstall)`, when `not UninstallSilent`, show a Yes/No `MsgBox` with **No** as the default button (`MB_DEFBUTTON2`). The text:

> Also delete your characters, settings and backups?
>
> Your characters will be gone for good unless you exported them or use cloud sync. Choose No to keep them for a future reinstall.

If the user chose Yes: `DelTree(ExpandConstant('{localappdata}\Hearthsheet'), True, True, True)` and `Exec('cmdkey.exe', '/delete:Hearthsheet/supabase-session', '', SW_HIDE, ewWaitUntilTerminated, rc)`. Before asking, read `{localappdata}\Hearthsheet\appsettings.user.json` (`LoadStringFromFile`). If it has a non-empty `"DataDirectory"` value, extract it (take the next quoted string after the key, and turn `\\` into `\`) and add a line to the prompt: "Your custom data folder is not deleted: <path>". Never delete that path.

- [ ] **Step 3: Retire the console installer.** Delete `packaging/Install.cmd` and `packaging/Install.ps1`. Strip `Dependencies.ps1` down to what the SDK check uses. Rewrite `scripts/install.ps1` to SDK check → `publish.ps1` → `Start-Process artifacts/Hearthsheet-Setup.exe -Wait`, and drop its `-InstallDir`/`-NoDesktopShortcut`/`-NoLaunch` params (the wizard asks). Keep CRLF.

- [ ] **Step 4: Build the package locally**

Run: `pwsh ./scripts/publish.ps1`
Expected: the three artifacts exist; `SHA256SUMS.txt` has two lines matching `Get-FileHash`. `artifacts/publish/Hearthsheet` has no `Install.cmd`, and does contain `coreclr.dll` (proof it's self-contained). The zip is under 500 MB.

- [ ] **Step 5: Smoke-install locally**, using a scratch data folder so real data is safe: run `artifacts/Hearthsheet-Setup.exe`, confirm the checkbox is ticked, install, launch from the finish page, check that *Settings → Apps* lists Hearthsheet with the version. Uninstall, choosing **No**, and confirm `%LOCALAPPDATA%\Hearthsheet` is still there.

- [ ] **Step 6: Release workflow.** In `release.yml`, add a step before Package: `if (-not (Test-Path "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")) { choco install innosetup -y --no-progress }`. Change the `gh release create` asset list to `(Get-Item artifacts/*.zip).FullName artifacts/Hearthsheet-Setup.exe artifacts/SHA256SUMS.txt`.

- [ ] **Step 7: Docs.**
  - README Install section: lead with the stable link, then the SmartScreen steps (*More info → Run anyway*), with a placeholder `docs/images/smartscreen.png` (Task 4 captures it). Then the wizard and the uninstall question.
  - README: remove the .NET runtime requirement for users. Packaging lists the three artifacts and the Inno prerequisite.
  - ARCHITECTURE: §9 (setup.exe + zip), §11 Privilege (per-user installer, no admin), §14 (drop "requires the .NET 10 Desktop Runtime"; add "unsigned: SmartScreen warns once").
  - CLAUDE.md publish comment: list the three artifacts and the Inno prerequisite.

- [ ] **Step 8: Commit**

```bash
git add packaging scripts .github/workflows/release.yml README.md docs/ARCHITECTURE.md CLAUDE.md
git commit -m "Add Inno Setup installer and self-contained package"
```

---

### Task 3: Keep the *Apps* version current after in-app updates

**Files:**
- Create: `src/Hearthsheet.Infrastructure/Updates/InstallRegistration.cs`
- Modify: `src/Hearthsheet.App/App.xaml.cs` (`OnStartup`, after logging is set up)
- Test: `tests/Hearthsheet.Tests/UpdateTests.cs` (new class `InstallRegistrationTests`)

**Interfaces:**
- Produces: `public static class InstallRegistration` with `public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{5A4DFE70-9A1C-405F-842C-01D7DA9F8911}_is1";` and `public static bool RefreshDisplayVersion(RegistryKey root, string keyPath, string version)`. It opens `keyPath` under `root` writable. If the key is missing it returns `false` and creates nothing. Otherwise it sets `DisplayVersion` (string) only when it differs, and returns `true`. Mark it `[SupportedOSPlatform("windows")]`.
- App: `InstallRegistration.RefreshDisplayVersion(Registry.CurrentUser, InstallRegistration.UninstallKey, AppSettings.AppVersion)` inside a `try`/`catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)` that logs a warning. Never block startup on it.

- [ ] **Step 1: Write the failing tests**, using a throwaway key `Software\Hearthsheet.Tests\<guid>` under `Registry.CurrentUser`, deleted in `Dispose`:

```csharp
[Fact] public void MissingKey_CreatesNothing()
// Assert.False(Refresh(HKCU, path, "1.5.0")); Assert.Null(Registry.CurrentUser.OpenSubKey(path))

[Fact] public void ExistingKey_GetsNewVersion()
// CreateSubKey(path).SetValue("DisplayVersion", "1.4.0"); Assert.True(Refresh(..., "1.5.0")); value == "1.5.0"
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~InstallRegistrationTests"`
Expected: build error (type missing).

- [ ] **Step 3: Implement** `InstallRegistration` and the startup call.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test Hearthsheet.slnx`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Hearthsheet.Infrastructure/Updates/InstallRegistration.cs src/Hearthsheet.App/App.xaml.cs tests/Hearthsheet.Tests/UpdateTests.cs
git commit -m "Refresh the installed version shown in Windows Apps at startup"
```

---

### Task 4: End-to-end check on a clean profile (manual, Beau)

Needs a fresh Windows user account or a VM **without .NET installed**, and two release builds. Make them locally with `pwsh ./scripts/publish.ps1 -Version 9.0.0` and `-Version 9.0.1`. Then point the 9.0.0 install at the 9.0.1 zip: the simplest way is a throwaway GitHub release pair on a fork, or skip the in-app step here and verify it on the first real release.

- [ ] Download the setup through a browser (so it gets the downloaded-file mark) and pass SmartScreen. Capture `docs/images/smartscreen.png` for the README.
- [ ] Install with the desktop shortcut ticked, then reinstall with it unticked. The desktop shortcut is created or left alone accordingly, and there is one Start menu entry.
- [ ] Run setup while Hearthsheet is open. It asks to close it.
- [ ] Install over a 1.4.0 `Install.cmd` install. The app starts, characters are intact, and `Install.cmd` is gone from the folder.
- [ ] In-app update N → N+1. *Apps* shows N+1 after the next start, and `Hearthsheet Uninstall\unins000.exe` still exists.
- [ ] Uninstall choosing **No**: the program folders are gone and the data is kept. Reinstall: the characters are back. Uninstall choosing **Yes**: `%LOCALAPPDATA%\Hearthsheet` and the `Hearthsheet/supabase-session` credential are gone. With a custom `DataDirectory` set, the prompt names it and the folder survives.
- [ ] Record the results in the PR description, then open the PR into `dev`.

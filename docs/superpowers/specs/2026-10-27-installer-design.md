# Installer and safe upgrades: design

## Intent

Today a first install means: find the releases page, download a zip, extract it, run `Install.cmd` (a PowerShell console),
click through the unsigned-publisher warning, and separately install the .NET 10 Desktop Runtime if it's missing. That's
fine for Beau but too much for a non-technical friend. The goal is **one link, one file, a short wizard**, and then
Hearthsheet is in the Start menu. Updates keep working through the app, and a big update (database or character
format change) never loses data.

## Goals

- A single `Hearthsheet-Setup.exe` on every release, downloadable from a stable link that always means "latest":
  `https://github.com/BDesro/Hearthsheet/releases/latest/download/Hearthsheet-Setup.exe` (the asset name has no version in
  it so the link never changes). The README's install section leads with this link.
- Wizard: a **"Create a desktop shortcut" checkbox, ticked by default** (Beau's requirement; the Start menu shortcut is
  always created), Install, and a "Launch Hearthsheet" box on the finish page.
- Per-user install to `%LOCALAPPDATA%\Programs\Hearthsheet` (the same folder as today), with **no admin prompt**.
- Nothing else to install: the .NET runtime is bundled (self-contained publish).
- A normal entry in Windows *Settings → Apps* that shows the current version and uninstalls cleanly.
- **The uninstaller asks** whether to also delete the user's characters, settings and backups. The default answer is
  keep. Beau decided this.
- Running the setup over an existing install (including one made with today's `Install.cmd`) upgrades it in place.
- Updates stay in-app, through the existing flow (GitHub release check, SHA-256, staged version check, `Hearthsheet.Updater`
  backup/replace/rollback).
- Before any database or character-format upgrade runs, a backup of the database is taken. A failed upgrade leaves the
  user's data recoverable.

## Non-goals

- **Code signing.** Beau decided against it for now. The "Windows protected your PC" screen will appear once, for the
  setup file. Instead the README shows what to click (*More info → Run anyway*), with a screenshot. Signing can come
  later without changing this design (it would sign `Hearthsheet-Setup.exe` and the app binaries in the release workflow).
- Silent or background updates, and a separate updater tool. The in-app "update available" prompt stays the only update path.
- Machine-wide (all users) install, or a choice of install folder.
- Changing cloud-sync data on uninstall. Characters in the cloud are untouched either way.

## Anti-goals (failure even if it "works")

- Any path where an update or upgrade loses or corrupts characters.
- The first app update silently breaking the uninstaller or the *Apps* entry.
- A second elevation or security prompt beyond the one SmartScreen screen.
- Adding runtime dependencies to the app for this.

## Decisions (Claude's calls; Beau can override)

1. **Inno Setup 6** builds the installer. It's free, standard, and a build-time tool only (nothing added to the app). It's
   preinstalled on GitHub's `windows-latest` runners; if a run finds it missing, install it with `choco install innosetup`.
   The script lives in `packaging/` (e.g. `packaging/Hearthsheet.iss`), and `scripts/publish.ps1` compiles it after the
   zip is built, so local and CI packaging stay the same command. Settings that matter: `PrivilegesRequired=lowest`,
   `DefaultDirName={localappdata}\Programs\Hearthsheet`, a fixed `AppId` GUID (never change it, upgrades depend on it),
   `DisableDirPage=yes`, `CloseApplications` so setup asks to close a running Hearthsheet.
2. **Self-contained publish** (`--self-contained true`, win-x64) for both the app and the updater. Installing the runtime
   separately needs admin, which means a second prompt. Downloads grow from a few MB to about 70 MB. That's acceptable,
   and well under the 500 MB `MaxPackageBytes` cap. The updater stays a single-file exe (self-contained single-file,
   trimming optional), because it's copied out of the install folder to replace it.
3. **The uninstaller lives outside the folder the updater replaces.** `Hearthsheet.Updater` clears the whole install folder,
   which would delete Inno's `unins000.exe`/`.dat`. Set Inno's `UninstallFilesDir` to a sibling folder
   (e.g. `{localappdata}\Programs\Hearthsheet Uninstall`), so the updater needs no change. (The alternative, teaching the
   updater to skip `unins*` files, is the builder's fallback if `UninstallFilesDir` causes trouble.)
   Inno only removes files it installed itself, and in-app updates add files it never saw. So the uninstaller also removes the
   whole install folder and the updater's `Hearthsheet.backup` sibling folder (`[UninstallDelete] Type: filesandordirs`).
4. **The *Apps* version stays current after in-app updates.** At startup the app writes its own version to
   `DisplayVersion` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\<AppId>_is1`, only if that key
   already exists (so dev runs and zip installs don't create one). Doing it at startup rather than in the updater means
   it also fixes itself after a rollback.
5. **Uninstall data prompt.** It's asked once in `CurUninstallStepChanged`, with wording like "Also delete your characters,
   settings and backups? Your characters will be gone unless you exported them or use cloud sync." The default button
   is **No**. Yes deletes `%LOCALAPPDATA%\Hearthsheet` and the saved cloud sign-in in Credential Manager. If the user set a
   custom `Application:DataDirectory`, that folder is **not** deleted; the prompt says where it is so they can delete it themselves.
   Silent uninstall (`/SILENT`, `/VERYSILENT`) keeps data.
6. **Release assets** become `Hearthsheet-X.Y.Z-win-x64.zip` (update package, unchanged contract), `Hearthsheet-Setup.exe`
   and `SHA256SUMS.txt` listing both. The `UpdateService` contract is unchanged; it keeps using the zip.
7. **Retire the console installer.** `packaging/Install.cmd`, `Install.ps1` and the runtime check in `Dependencies.ps1` are
   removed from the package. `scripts/install.ps1` (build from source) becomes "publish, then run the built setup"
   (it still needs the SDK check). Docs (`README.md`, `docs/ARCHITECTURE.md` §9 and §14, `CLAUDE.md` commands) are updated
   to match: "requires the .NET 10 Desktop Runtime" goes away.

## Safe upgrades (data)

What exists: the database has `meta.db_version` with in-place `UpgradeToN` steps in a transaction. Character documents carry
`schema_version` and are migrated as raw JSON on load. Newer data is refused, not half-read. User data lives outside the
install folder. A daily backup (keep 10) runs at startup.

The gap: `SqliteCharacterRepository`'s constructor runs `Initialize()` (including `UpgradeTo2` and any future upgrade)
*before* `App.Compose` calls `DatabaseBackups.RunDaily`, so the backup that day captures the already-upgraded file.

Change:

- In `Initialize()`, when `current < DatabaseVersion` and the database file already existed, write a backup **before** the
  first upgrade step, named so it stands out and isn't rotated away by the daily keep-10 (e.g.
  `characters-before-v{current}-to-v{DatabaseVersion}-{timestamp}.db` in the backups folder; the daily pruning only matches
  its own pattern). If that backup fails, **don't upgrade**. Fail startup with the existing friendly startup-error path,
  because upgrading without a backup is exactly the risk this exists to remove.
- Character schema migration needs no extra backup. It happens in memory on load, and the stored row is only rewritten on
  save, by which point the pre-migration backup above (when the db version also changed) or the daily backup holds the old copy.
  Because nothing forces a db version bump alongside a character schema bump, also take the same "before upgrade" backup
  the first time the app starts on a new `CharacterJson.CurrentSchemaVersion`. Track the last-seen value in `meta`
  (e.g. `meta.last_schema_version`, a new row, not a table change).
- Document the rule in `docs/ARCHITECTURE.md` §7: every upgrade step is transactional, a backup precedes it, and
  never write a downgrade path (newer data is still refused).

## Tests (minimum)

- An existing v1 database file (built in the test, like the current db-upgrade tests) is opened: the pre-upgrade backup exists,
  it is a v1 database with the same rows, and the upgraded database has the same characters.
- The backup failing (e.g. the backups folder is unwritable) means the database is left at v1, unchanged.
- No backup is written when no upgrade is needed.
- Raising the character schema version triggers the one-time backup, and only once.
- Manual end-to-end check, recorded in the PR: on a clean Windows user profile (or VM) without .NET installed, download the setup,
  pass SmartScreen, install with and without the desktop shortcut, launch, update in-app from release N to N+1, confirm
  *Apps* shows N+1, then uninstall both ways (keep / delete data).

## Left to the builder

- Inno script details, wizard images and text, the icon.
- Whether trimming is safe for the updater (the WPF app is not trimmed).
- Exact backup file naming and where the "before upgrade" logic sits, as long as it runs before any upgrade step and blocks on failure.
- README wording and the SmartScreen screenshot (take it from a real run on the test profile).
- Release notes and the version bump follow the normal release rules in `CLAUDE.md`.

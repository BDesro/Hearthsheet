# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Hearthsheet is a Windows desktop app (.NET 10, WPF) for playing and managing a D&D 5e character. `docs/ARCHITECTURE.md` is the detailed design reference (domain model, sync, updates, security, extension points); `docs/CLOUD_SYNC.md` covers the Supabase sync; `docs/WEB.md` covers the web app's deploy (Cloudflare Pages) and its preview-to-production switch. Read the relevant section before changing those areas.

## Commands

```bash
dotnet build Hearthsheet.slnx
dotnet test Hearthsheet.slnx
dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~RestTests"          # one class
dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~RestTests.LongRest"  # one test / name prefix
dotnet run --project src/Hearthsheet.App
dotnet run --project src/Hearthsheet.Web   # the Blazor WebAssembly PWA (preview; points at the Hearthsheet-Test Supabase project)
pwsh ./scripts/publish.ps1     # needs Inno Setup 6; artifacts/Hearthsheet-Setup.exe + Hearthsheet-<version>-win-x64.zip + SHA256SUMS.txt
```

CI (`.github/workflows/ci.yml`) runs `dotnet build` and `dotnet test` in Release on `windows-latest`. There is no separate linter or formatter step.

Running the app uses your real data in `%LOCALAPPDATA%\Hearthsheet`. There is no command-line or environment-variable config (removed on purpose); to point at a scratch folder, set `Application:DataDirectory` in `%LOCALAPPDATA%\Hearthsheet\appsettings.user.json`.

## Architecture (big picture)

Seven projects, dependencies point inward: `App` → `Presentation`, `Infrastructure` → `Cloud` → `Core`, `Web` → `Presentation`, `Cloud`, and `Presentation` → `Core`; `Updater` is a standalone console exe.

- **Core** has no WPF, SQLite or network references, so all game rules are unit-testable. **Cloud** is the Supabase client (auth, character store, `ISecretStore` contract) with no platform code, so a web app can share it. **Presentation** holds the UI-neutral view models (`CharacterViewModel`, `PlayViewModel`, rows, `RelayCommand`, `Lookups`) behind `IDialogs` and a post-to-UI action, so a web UI can share them. Tests reference Core, Cloud, Presentation, Infrastructure and Updater, never App.
- **One model, three views.** Sheet, Details and Play all bind directly to the same `Character` aggregate (domain classes derive from `Observable` and use `ObservableCollection`). No view holds its own copy of state. `ChangeTracker` subscribes to the whole object graph by reflection and feeds `CharacterSession.Changed`, which drives both `CharacterViewModel.Refresh()` (derived values) and the 2-second autosave.
- **Stored vs derived.** Only authoritative values are persisted. Totals (modifiers, saves, skills, AC, speed, max HP, spell DC, attack bonuses, carried weight) are pure functions in `Core/Rules/CharacterRules.cs` exposed through `CharacterViewModel`. Never store a derived value.
- **Modifiers** (`Target` key from `StatTargets`, `Kind` Bonus/Set, `Value`) are the one mechanism for bonuses and overrides on features, effects, equipped/attuned items and weapons. Add a new target there rather than special-casing.
- **Gameplay behaviour lives on the aggregate** (`Character.Play.cs`: damage, healing, death saves, casting, resources). Property setters clamp to legal ranges, which is also how imported data is sanitized. View models call domain methods; they don't mutate state themselves.
- **Rests** are an ordered list of `IRestStep`s composed in `RestService.CreateDefault()`.
- **Persistence**: SQLite, one row per character holding the JSON document (same shape as `.dndchar` export) plus denormalized list columns. `ICharacterRepository` is the only persistence contract Core knows.
- **Schema changes**: bump `CharacterJson.CurrentSchemaVersion` and migrate the raw `JsonObject` in `CharacterJson.Deserialize` before typed deserialization. Newer-schema data is refused, not half-read. Table-level changes use `meta.db_version`.
- **Composition**: no DI container, no MVVM toolkit. Everything is wired in `App.xaml.cs`; `Presentation/RelayCommand.cs` has a small `RelayCommand`; the WPF app bridges `CommandManager.RequerySuggested` to `CommandRequery.Raise()`. Config is only `appsettings.json` → per-user `appsettings.user.json` (`Composition/AppSettings.cs`).
- **Cloud sync** is optional; local SQLite stays the source of truth. A sync pass never writes the open character directly; remote changes come back as deferred changes applied on the UI thread. Cloud wins conflicts (local saved as a conflict copy); an edit beats a delete. Server schema is in `supabase/migrations/`.
- **Updates**: the app pulls GitHub Releases, verifies SHA-256 and the staged binary's version, then `Hearthsheet.Updater` swaps the install folder with backup/rollback. Only HTTPS GitHub hosts are contacted; keep that allowlist intact.

## Conventions

- Dependencies are kept deliberately minimal (Microsoft.Data.Sqlite, Microsoft.Extensions.Logging/Configuration, xUnit). Don't add packages for what a few lines can do.
- Parameterized SQL only. Never log secrets or tokens; the logger's redaction filter is a backstop, not a license.
- Every WPF input needs an `AutomationProperties.Name`; state is never shown by colour alone.
- The character sheet is a fixed letter-size layout (816×1056 DIP) that prints 1:1; keep add/remove controls hover-only so printouts stay clean.
- `.ps1` and `.cmd` files must stay CRLF (`.gitattributes`).
- No bundled spell, feature or item content beyond SRD 5.1 facts; see `NOTICE.md` before adding game content.

## Branches and releases

- Work on a feature branch from `dev`, PR into `dev` (squash). Never push directly to `dev` or `main`.
- `main` only changes via a `dev` → `main` PR (merge commit), and merging it **publishes a release automatically**. Don't open or merge PRs into `main` unless explicitly asked.
- A release bumps `<Version>` in `Directory.Build.props` (single source of the app version) and rewrites `RELEASE_NOTES.md` as plain text with `-` bullets (the in-app update dialog shows it verbatim). CI rejects a PR into `main` whose version isn't newer than the latest tag (`scripts/check-version.ps1`). Tags are plain `X.Y.Z`; no pre-releases. Fix broken releases forward with a patch version.

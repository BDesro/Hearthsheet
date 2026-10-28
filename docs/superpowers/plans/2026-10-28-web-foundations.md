# Web version foundations (Phases 0–1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove Core runs in a published Blazor WebAssembly build, create the test Supabase project, and move the
cloud client and the character view models into UI-neutral projects that both apps can use. The desktop must keep
working exactly as before.

**Architecture:**
- Two new `net10.0` class libraries:
  - `Hearthsheet.Cloud`: the Supabase client and the secret-store contract, moved out of `Infrastructure`.
  - `Hearthsheet.Presentation`: `RelayCommand`, the row view models, `CharacterViewModel` and `PlayViewModel`, moved
    out of `App`.
- WPF-only behaviour sits behind three seams:
  - an `IDialogs` interface
  - a "post to UI" `Action<Action>`
  - a command-requery hook the WPF app wires to `CommandManager`
- The web project itself is a later plan (Phase 2).

**Tech Stack:** .NET 10, WPF (desktop), xUnit, Blazor WebAssembly (spike only), Supabase.

**Spec:** `docs/superpowers/specs/2026-10-28-web-version-design.md`. Decisions 2, 3, 4, 5 and 15a and Phase 0–1 are what
this plan implements.

**Later plans:**
- Phase 2: web shell, storage, sync, deploy, migration `0002`.
- Phases 3–5: Play, Details, Sheet.
- Phase 6: audits.

Each is written when the previous phase has landed, using what it taught us. The shared `MainViewModel` split (decision 2)
belongs to the Phase 2 plan, because only the web shell can show which parts of it are really shared.

## Global Constraints

- Dependencies point inward:
  - `App` → `Presentation`, `Infrastructure`, `Cloud` → `Core`
  - `Infrastructure` → `Cloud`, `Core`
  - `Presentation` → `Core` only
  - `Cloud` → `Core` only
- `Core`, `Cloud` and `Presentation` have no WPF, SQLite, Windows or file-system-specific references; `TargetFramework`
  is `net10.0`.
- No new NuGet packages in any shipped project. The spike may use the Blazor WebAssembly SDK packages; it is never
  committed.
- Tests reference `Core`, `Cloud`, `Presentation`, `Infrastructure` and `Updater`, never `App` (`CLAUDE.md`).
- Behaviour of the desktop app is unchanged. All 259+ existing tests pass after every task.
- Never log secrets or tokens.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Work on branch `web-version-design` (or a feature branch from `dev`). Never push to `dev` or `main`.
- `.ps1` and `.cmd` files stay CRLF.

## Review Focus

1. **A desktop button stays disabled after its condition becomes true.** For example, the Play tab's Heal button after
   typing an amount, or Temp HP after setting `HpInput`. Expected: it enables exactly as it does today.
   - Test `CommandRequeryTests.Raise_NotifiesEveryLiveCommand` (Task 3).
   - Manual smoke check in Task 5.
2. **Requery stops after a garbage collection.** WPF's `CommandManager.RequerySuggested` holds handlers weakly. If the
   app's bridge handler isn't kept in a field, every button freezes after the first GC.
   - The Task 3 step pins a static field.
   - Task 5 forces a GC in the smoke check.
3. **Derived values go stale after a burst of edits** (a long rest, a level up). Expected: one refresh after the burst,
   showing final values.
   - Test `CharacterViewModelTests.BurstOfChanges_PostsOneRefresh` (Task 4).
4. **A saved session that can't be read** (credential store error) crashes startup instead of starting signed out.
   - Test `SupabaseAuthTests.Restore_ReturnsFalse_WhenSecretStoreFails` (Task 2).
5. **Dead commands pile up** in the requery list as characters are opened and closed.
   - Test `CommandRequeryTests.Raise_DropsCollectedCommands` (Task 3).

---

### Task 1: Phase 0: test Supabase project and Blazor spike

This task produces facts, not shipped code. The spike lives in the scratchpad and is never committed.

**Files:**
- Modify: `docs/superpowers/specs/2026-10-28-web-version-design.md` (the "Live project facts" section: add the test
  project ref and the spike results)

- [ ] **Step 1: Create the test project (with Beau's approval in chat).**
  - Ask Beau to confirm, then create a free Supabase project named `Hearthsheet-Test` in organization
    `wouglnascjbgucybeyhy`, region `us-east-2`.
  - Use `get_cost`/`confirm_cost`; the cost must be $0. If it isn't, stop and tell Beau.
  - Apply `supabase/migrations/0001_cloud_sync.sql` with `apply_migration`.
- [ ] **Step 2: Match the auth settings (Beau, in the dashboard).**
  - Steps 3 and 6 of `docs/CLOUD_SYNC.md`: Email on, Confirm email off, minimum password length 8, the reset-code
    template.
  - SMTP (step 5) is optional for the test project; without it the built-in mailer's rate limits apply.
- [ ] **Step 3: Verify the test project.**
  - Run `get_advisors` (security and performance) on it. Expected: no findings other than "Leaked password protection
    disabled".
  - Run the RLS check SQL from `docs/CLOUD_SYNC.md` with two test users. Expected: count 0.
- [ ] **Step 4: Build the spike in the scratchpad.**
  - Create the app with `dotnet new blazorwasm --empty -o <scratchpad>/spike`.
  - Add a `ProjectReference` to `C:\Dev\Hearthsheet\src\Hearthsheet.Core\Hearthsheet.Core.csproj`.
  - One page that:
    1. Builds a `Character` from `SrdCatalog`.
    2. Wraps it in a `CharacterSession`.
    3. Shows a counter of `CharacterSession.Changed` events.
    4. Has a button that calls `TakeDamage(3)`.
    5. Has a button that round-trips the character through `CharacterJson` and shows the name and current HP.
- [ ] **Step 5: Publish trimmed and run it.**
  - Run: `dotnet publish -c Release`.
  - Serve `bin/Release/net10.0/publish/wwwroot` with any static server.
  - Open it in the built-in browser (or Playwright) and click both buttons.
  - Expected: the counter increases on damage, and the JSON round-trip shows the same name and HP.
  - If either fails, try in order:
    - `<TrimmerRootAssembly Include="Hearthsheet.Core" />`
    - `<JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>`
  - Record which setting was needed.
- [ ] **Step 6: Record the results.**
  - In the spec's "Live project facts", add:
    - the test project ref
    - the trimming settings Core needs
    - the published download size (total of `_framework`, compressed `.br`)
    - anything that failed
  - These feed the Phase 2 plan.
- [ ] **Step 7: Commit.**

```bash
git add docs/superpowers/specs/2026-10-28-web-version-design.md
git commit -m "Record Phase 0 results for the web version"
```

### Task 2: `Hearthsheet.Cloud` project

**Files:**
- Create: `src/Hearthsheet.Cloud/Hearthsheet.Cloud.csproj` (`net10.0`, references `Core`, no packages)
- Move: `src/Hearthsheet.Infrastructure/Sync/SupabaseHttp.cs`, `SupabaseAuth.cs`, `SupabaseCharacterStore.cs` →
  `src/Hearthsheet.Cloud/`. The namespace becomes `Hearthsheet.Cloud`.
- Create: `src/Hearthsheet.Cloud/SecretStore.cs`, containing `ISecretStore` (moved out of `WindowsCredentialStore.cs`)
  and `SecretStoreException`
- Modify:
  - `src/Hearthsheet.Infrastructure/Security/WindowsCredentialStore.cs`: implements `Hearthsheet.Cloud.ISecretStore`
    and throws `SecretStoreException`
  - `src/Hearthsheet.Infrastructure/Hearthsheet.Infrastructure.csproj`: references `Cloud`
  - `src/Hearthsheet.App/Hearthsheet.App.csproj`, plus `using`s in `App.xaml.cs`, `Dialogs.cs`,
    `Composition/AppSettings.cs`, `ViewModels/SyncViewModel.cs` and the views that use `SupabaseAuth`
  - `Hearthsheet.slnx`
  - `tests/Hearthsheet.Tests/Hearthsheet.Tests.csproj` and test `using`s
- Test: `tests/Hearthsheet.Tests/SupabaseTests.cs`

**Interfaces:**
- Produces, in namespace `Hearthsheet.Cloud`:
  - `public interface ISecretStore { string? Read(string key); void Write(string key, string secret); void Delete(string key); }`
    Implementations throw `SecretStoreException` and nothing else for storage failures.
  - `public sealed class SecretStoreException(string message, Exception? inner = null) : Exception(message, inner)`
  - `SupabaseOptions`, `SupabaseHttp`, `SupabaseAuth` and `SupabaseCharacterStore` with unchanged public members.
  - `SupabaseHttp(SupabaseOptions options, HttpMessageHandler? handler = null, string userAgentVersion = "1.0")`, whose
    default handler is now `new HttpClientHandler { AllowAutoRedirect = false }`. That works on desktop and in the
    browser; `SocketsHttpHandler` throws in the browser.

- [ ] **Step 1: Write the failing test.** In `SupabaseAuthTests`:

```csharp
[Fact]
public void Restore_ReturnsFalse_WhenSecretStoreFails()
{
    var auth = new SupabaseAuth(new SupabaseHttp(Options, new SupabaseStub(_ => throw new InvalidOperationException())), new ThrowingSecrets(), _time);
    Assert.False(auth.Restore());
    Assert.Null(auth.UserId);
}
```

  `ThrowingSecrets : ISecretStore` (next to `FakeSecrets` in `UpdateTests.cs`) throws
  `new SecretStoreException("unavailable")` from every method.
- [ ] **Step 2: Run it to verify it fails.**
  - Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~Restore_ReturnsFalse_WhenSecretStoreFails"`
  - Expected: a build error (`SecretStoreException` and `ThrowingSecrets` don't exist yet).
- [ ] **Step 3: Create the project and move the files.**
  - Add `Hearthsheet.Cloud` to the solution and move the three `Supabase*` files and `ISecretStore` (use `git mv` so
    history follows).
  - Replace the three `Win32Exception` catches in `SupabaseAuth.cs` (`Restore`, the save-session path, `Forget`) with
    `SecretStoreException`.
  - In `WindowsCredentialStore`, throw `SecretStoreException(message, new Win32Exception(error))` where it threw
    `Win32Exception`.
  - Change `SupabaseHttp`'s default handler as above.
  - Fix the `using`s.
- [ ] **Step 4: Run the full suite.**
  - Run: `dotnet build Hearthsheet.slnx`, then `dotnet test Hearthsheet.slnx`.
  - Expected: build succeeds; all tests pass, including the new one.
- [ ] **Step 5: Update the docs.**
  - `CLAUDE.md` "Architecture": four projects becomes five, with `Cloud` (Supabase client, no platform code) in the
    dependency line.
  - `docs/ARCHITECTURE.md` §2: add the project.
  - `docs/CLOUD_SYNC.md`: file paths now under `src/Hearthsheet.Cloud`.
- [ ] **Step 6: Commit.**

```bash
git add -A src/Hearthsheet.Cloud src/Hearthsheet.Infrastructure src/Hearthsheet.App tests Hearthsheet.slnx CLAUDE.md docs/ARCHITECTURE.md docs/CLOUD_SYNC.md
git commit -m "Move the Supabase client into a platform-neutral Hearthsheet.Cloud project"
```

  `CLAUDE.md` is currently untracked; committing it is fine, since it's the project instructions file.

### Task 3: `Hearthsheet.Presentation` project with a cross-platform `RelayCommand`

**Files:**
- Create: `src/Hearthsheet.Presentation/Hearthsheet.Presentation.csproj` (`net10.0`, references `Core`)
- Create: `src/Hearthsheet.Presentation/RelayCommand.cs` (replaces `src/Hearthsheet.App/Mvvm/RelayCommand.cs`, which
  is deleted)
- Modify: `src/Hearthsheet.App/App.xaml.cs` (bridge `CommandManager.RequerySuggested` → `CommandRequery.Raise`), the
  App `.csproj`, `Hearthsheet.slnx`, the tests `.csproj`, and the `using Hearthsheet.App.Mvvm;` lines
- Test: `tests/Hearthsheet.Tests/CommandRequeryTests.cs`

**Interfaces:**
- Produces, in namespace `Hearthsheet.Presentation`:
  - `public sealed class RelayCommand : ICommand` and `public sealed class RelayCommand<T> : ICommand`, with the same
    constructors and `CanExecute`/`Execute` behaviour as today. `CanExecuteChanged` is an ordinary instance event.
    Each instance registers itself with `CommandRequery` when constructed.
  - `public static class CommandRequery`:
    - `public static void Raise()` raises `CanExecuteChanged` on every live command and drops dead entries.
    - `internal static int LiveCountForTests { get; }` (with `InternalsVisibleTo("Hearthsheet.Tests")`).
    - It holds `WeakReference` entries under a lock, so a command never outlives its view model because of the
      registry.

- [ ] **Step 1: Write the failing tests** in `CommandRequeryTests`:
  - `Raise_NotifiesEveryLiveCommand`: two commands (one `RelayCommand`, one `RelayCommand<int>`), each with a
    `CanExecuteChanged` counter. After `CommandRequery.Raise()`, both counters equal 1.
  - `Raise_DropsCollectedCommands`:
    1. Create 100 commands in a non-inlined helper.
    2. `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();`
    3. `CommandRequery.Raise()`
    4. Assert `CommandRequery.LiveCountForTests` is below 100.
  - `CanExecute_Typed_IgnoresOtherParameterTypes`: `new RelayCommand<int>(_ => { }).CanExecute("x")` is false.

  Mark the class `[Collection("CommandRequery")]` so the static registry isn't shared with parallel tests.
- [ ] **Step 2: Run them to verify they fail.**
  - Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~CommandRequeryTests"`
  - Expected: a build error.
- [ ] **Step 3: Implement `RelayCommand`, `RelayCommand<T>` and `CommandRequery`.**
  - Create the project, delete `App/Mvvm/RelayCommand.cs` and update `using`s to `Hearthsheet.Presentation`.
  - In `App.xaml.cs`, keep the bridge delegate in a **static field**
    (`private static readonly EventHandler RequeryBridge = (_, _) => CommandRequery.Raise();`) and subscribe it in
    `OnStartup` with `CommandManager.RequerySuggested += RequeryBridge;`. `CommandManager` holds handlers weakly, so
    the field is what keeps requery alive.
- [ ] **Step 4: Run the full suite.**
  - Run: `dotnet build Hearthsheet.slnx`, then `dotnet test Hearthsheet.slnx`.
  - Expected: all pass.
- [ ] **Step 5: Commit.**

```bash
git add -A src/Hearthsheet.Presentation src/Hearthsheet.App tests Hearthsheet.slnx
git commit -m "Add Hearthsheet.Presentation with a platform-neutral RelayCommand"
```

### Task 4: Move the character view models into Presentation

**Files:**
- Move to `src/Hearthsheet.Presentation/` (namespace `Hearthsheet.Presentation`):
  - `src/Hearthsheet.App/ViewModels/Rows.cs`
  - `CharacterViewModel.cs`
  - `PlayViewModel.cs` (including `QuickAddResult`)
- Create: `src/Hearthsheet.Presentation/IDialogs.cs`
- Modify:
  - `src/Hearthsheet.App/Dialogs.cs` (implements `IDialogs`)
  - `src/Hearthsheet.App/ViewModels/MainViewModel.cs:168` (passes the post action)
  - XAML files whose `xmlns` points at `Hearthsheet.App.ViewModels` for these types (Sheet, Details, Play views,
    `ShortRestDialog`)
  - `docs/ARCHITECTURE.md` §2–4
  - `CLAUDE.md` (six projects)
- Test: `tests/Hearthsheet.Tests/CharacterViewModelTests.cs`

**Interfaces:**
- Consumes: `RelayCommand`, `RelayCommand<T>` (Task 3).
- Produces, in namespace `Hearthsheet.Presentation`:

```csharp
public interface IDialogs
{
    bool Confirm(string message, string title = "Confirm");
    void Info(string message);
    (string ClassName, LevelUpOptions Options)? LevelUp(Character c, string? className = null);
    QuickAddResult? QuickAdd(string title, IReadOnlyList<object>? kinds, object? defaultKind, bool withDescription);
    bool ShortRest(PlayViewModel play, Character character);
}
```

- `CharacterViewModel(CharacterSession session, RestService rest, IDialogs dialogs, ILogger log, Action<Action> postToUi)`.
  `OnSessionChanged` calls `postToUi(Refresh)` where it called `Dispatcher.CurrentDispatcher.BeginInvoke(...)`; the
  coalescing flag stays.
- `PlayViewModel(CharacterViewModel owner, RestService rest, IDialogs dialogs, ILogger log)`.
- WPF passes `a => Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, a)`.
- `Dialogs` keeps its other members (`Error`, `NewCharacter`, `SignIn`, `AccountSwitch`, file dialogs, `ShowUpdates`)
  as WPF-only public methods.

- [ ] **Step 1: Write the failing tests** in `CharacterViewModelTests`. Use a `FakeDialogs : IDialogs` with settable
  answers, a queue-backed post action (`var posted = new Queue<Action>(); Action<Action> post = posted.Enqueue;`), and a
  character from `SrdCatalog`.
  - `BurstOfChanges_PostsOneRefresh`:
    1. Edit three ability scores in a row.
    2. Assert `posted.Count == 1`.
    3. Dequeue and run the posted action, and assert each changed ability's `AbilityRow` now shows the new modifier.
    4. One more edit gives `posted.Count == 1` again.
  - `LongRest_DoesNothing_WhenNotConfirmed`: `FakeDialogs.ConfirmAnswer = false`, `TakeDamage(5)`, execute
    `vm.Play.LongRest`. Assert `Character.Combat.CurrentHp` is unchanged.
  - `LongRest_RestoresHp_WhenConfirmed`: the same with `true`. Assert `Character.Combat.CurrentHp == vm.MaxHitPoints`.

  Use the existing property names on `CharacterViewModel`, `AbilityRow` and `PlayViewModel`; don't add new ones for the
  test.
- [ ] **Step 2: Run them to verify they fail.**
  - Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~CharacterViewModelTests"`
  - Expected: a build error.
- [ ] **Step 3: Move the files (`git mv`) and introduce `IDialogs` and the post action.**
  - Update XAML `xmlns:vm` mappings to `clr-namespace:Hearthsheet.Presentation;assembly=Hearthsheet.Presentation` where
    these types are referenced.
  - Run `grep -rn "Hearthsheet.App.ViewModels" src/Hearthsheet.App` to find every reference to update.
  - Confirm no `System.Windows` `using` remains in Presentation:
    `grep -rn "System.Windows\.\(Threading\|Controls\|Media\)" src/Hearthsheet.Presentation` must print nothing.
    `System.Windows.Input.ICommand` is allowed.
- [ ] **Step 4: Run the full suite.**
  - Run: `dotnet build Hearthsheet.slnx`, then `dotnet test Hearthsheet.slnx`.
  - Expected: all pass.
- [ ] **Step 5: Update the docs.**
  - `CLAUDE.md` "Architecture": six projects, with `Presentation` (shared view models) and the dependency line from
    Global Constraints.
  - `docs/ARCHITECTURE.md` §2 and §4: the shared view model now lives in Presentation.
- [ ] **Step 6: Commit.**

```bash
git add -A src tests CLAUDE.md docs/ARCHITECTURE.md
git commit -m "Move the character view models into Hearthsheet.Presentation behind IDialogs"
```

### Task 5: Desktop regression check

- [ ] **Step 1: Run the full suite in Release, as CI does.**
  - Run: `dotnet build Hearthsheet.slnx -c Release`, then `dotnet test Hearthsheet.slnx -c Release`.
  - Expected: 0 failures.
- [ ] **Step 2: Smoke-test the desktop app against a scratch data folder.**
  - Set `Application:DataDirectory` in `%LOCALAPPDATA%\Hearthsheet\appsettings.user.json` to a temp folder. Restore
    the file afterwards.
  - Run `dotnet run --project src/Hearthsheet.App` and drive it (the `run` skill, or Windows UI Automation). Check:
    1. New character.
    2. Sheet: change an ability score. The modifier updates.
    3. Play: type a heal amount. The Heal button enables. Damage 5, then Heal 5.
    4. Short rest dialog opens and closes.
    5. Long rest asks first.
    6. Level up dialog opens.
    7. Remove a named entry. It asks.
    8. Leave the app idle for 2 minutes (several GCs happen), then repeat step 3. The buttons still enable.
  - Expected: everything behaves as on `dev`.
- [ ] **Step 3: Report.** List what was checked and anything that differed. Don't merge or push.

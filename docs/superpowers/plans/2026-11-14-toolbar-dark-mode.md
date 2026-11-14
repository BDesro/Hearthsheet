# Toolbar and Dark Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the desktop menu and the web account dropdown with a toolbar of large styled buttons plus ⋯ More, and add Light / Dark / Match system themes covering every screen in both apps (the character sheet page stays white).

**Architecture:** Desktop: theme brushes move into `Themes/Light.xaml` and `Themes/Dark.xaml`; a small `ThemeManager` swaps the merged dictionary at runtime, and every theme-brush reference becomes `DynamicResource`. Controls still drawing system chrome get hand-written templates. Web: one dark block of the existing CSS variables, selected by `data-theme` on `<html>`, set early by `js/theme.js`. The sheet page re-declares the light tokens in both apps so it stays white.

**Tech Stack:** .NET 10 WPF, Blazor WebAssembly, plain CSS, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-11-14-toolbar-dark-mode-design.md`

## Global Constraints

- No new NuGet or npm packages; no icon library. Desktop icons: `FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"` glyphs.
- Every WPF input and icon-only button has `AutomationProperties.Name`; state is never shown by colour alone.
- Tests never reference App or Web projects; XAML/CSS are checked by reading the files from the repo root (pattern: `WebConfigTests.Root()`).
- Light mode must look as it does today apart from the toolbar. Do not change existing light colour values.
- Text contrast in both themes: 4.5:1 for normal text pairs (listed in Task 2). The sheet page keeps the `Sheet*` / `--sheet-*` colours.
- The site's CSP has `script-src 'self'`: no inline scripts or inline event handlers in `index.html`.
- Never rewrite `appsettings.user.json`. The theme choice lives in `%LOCALAPPDATA%\Hearthsheet\ui.json`.
- Toolbar targets: desktop buttons `MinHeight` 36, `FontSize` 14; web buttons `min-height: 36px` (44px under `@media (pointer: coarse)`), `font-size: 14px`.
- `.ps1`/`.cmd` files stay CRLF (none expected to change).
- Work on branch `feature/toolbar-dark-mode`; PR into `dev` at the end (squash). Never push to `dev` or `main`.

## Review Focus

1. **Theme switched while a dialog or popup is open:** the open dialog, its title bar and any open dropdown recolour too (DynamicResource + `ThemeManager` iterating `Application.Current.Windows`). Covered by the manual check in Task 3 Step 6.
2. **Missing, empty or corrupt `ui.json`** (or an unknown value like `"Theme":"Purple"`): the app starts on Match system and doesn't crash. Covered by tests in Task 1.
3. **Windows switches light/dark while the app runs:** under Match system the app follows within a second; under explicit Light or Dark it does not change. Manual check in Task 3 Step 6.
4. **Printing from dark mode:** the printout (desktop `Ctrl+P`, web print) is the normal light sheet with no dark backdrop. Manual check in Task 4 Step 5 (desktop) and Task 7 Step 5 (web).
5. **Phone-width web (375px):** no horizontal scroll, and every command, including hidden buttons and the theme choice, is reachable through ⋯ More with the keyboard. Manual check in Task 8 Step 4.

---

### Task 1: Theme preference storage

**Files:**
- Create: `src/Hearthsheet.Infrastructure/UiPreferences.cs`
- Modify: `src/Hearthsheet.Infrastructure/AppPaths.cs` (add `public static string UiPreferences => Path.Combine(DefaultRoot, "ui.json");` beside `UserSettings`)
- Test: `tests/Hearthsheet.Tests/UiPreferencesTests.cs`

**Interfaces:**
- Produces: `public enum ThemeChoice { System, Light, Dark }` and `public static class UiPreferences { public static ThemeChoice LoadTheme(string path); public static void SaveTheme(string path, ThemeChoice choice); }` in namespace `Hearthsheet.Infrastructure`. File format: `{"Theme":"Dark"}` (enum as string).

- [ ] **Step 1: Write the failing tests** (each uses a temp file under `Path.GetTempPath()`):
  - `LoadTheme_MissingFile_IsSystem`: `Assert.Equal(ThemeChoice.System, UiPreferences.LoadTheme(missingPath))`
  - `LoadTheme_CorruptOrUnknown_IsSystem` (Theory): file contents `""`, `"{"`, `"{\"Theme\":\"Purple\"}"`, `"[]"` → `ThemeChoice.System`
  - `SaveThenLoad_RoundTrips` (Theory over all three values)
  - `SaveTheme_CreatesMissingDirectory`: path in a new temp subfolder; after save, `File.Exists(path)`
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~UiPreferencesTests"`. Expected: build FAIL (types missing).
- [ ] **Step 3: Implement** with `System.Text.Json` (`JsonStringEnumConverter`). `LoadTheme` catches `IOException`, `UnauthorizedAccessException` and `JsonException`, and treats undefined enum values as `System`. `SaveTheme` writes to a `.tmp` file then `File.Move(..., overwrite: true)`.
- [ ] **Step 4: Run** the same filter. Expected: PASS.
- [ ] **Step 5: Commit** `Add per-device theme preference`.

### Task 2: Desktop theme dictionaries and ThemeManager

**Files:**
- Create: `src/Hearthsheet.App/Themes/Light.xaml`, `src/Hearthsheet.App/Themes/Dark.xaml`, `src/Hearthsheet.App/Themes/ThemeManager.cs`
- Modify: `src/Hearthsheet.App/Themes/Styles.xaml` (remove the palette block lines 5–21), `src/Hearthsheet.App/App.xaml` (merge `Themes/Light.xaml` first), `src/Hearthsheet.App/App.xaml.cs` (apply the saved theme before `window.Show()`), every `.xaml` under `src/Hearthsheet.App` that references a theme brush
- Test: `tests/Hearthsheet.Tests/ThemeTests.cs`

**Interfaces:**
- Consumes: `ThemeChoice`, `UiPreferences`, `AppPaths.UiPreferences` (Task 1).
- Produces: `public static class ThemeManager { public static ThemeChoice Choice { get; } public static bool IsDark { get; } public static event Action? Changed; public static void Apply(ThemeChoice choice); }` in `Hearthsheet.App.Themes`.
- Theme brush keys (identical sets in both files): `Ink, InkMuted, Accent, AccentHover, AccentSoft, OnAccent, Surface, ReadOnlyFill, Canvas, Sidebar, Line, LineStrong, Good, Danger`. `OnAccent` is new: the text colour on `Accent` fills (light: `#FFFFFF`). Drop the `InkColor`/`AccentColor` `Color` resources (only used by the brushes). Light values are exactly today's.

- [ ] **Step 1: Write the failing tests** in `ThemeTests.cs`. Helpers: `Root()` copied from `WebConfigTests`; `static Dictionary<string,string> XamlBrushes(string file)` (XDocument: `SolidColorBrush` → `x:Key` → `Color`); `static double Contrast(string hexA, string hexB)` (WCAG relative luminance).
  - `DesktopThemes_HaveTheSameKeys`: key sets of Light.xaml and Dark.xaml are equal.
  - `DesktopThemes_TextPairsMeetAA` (Theory over `"Light.xaml"`, `"Dark.xaml"`): every pair ≥ 4.5: (Ink, Surface), (Ink, Canvas), (Ink, Sidebar), (Ink, ReadOnlyFill), (Ink, AccentSoft), (InkMuted, Surface), (InkMuted, Canvas), (InkMuted, Sidebar), (Accent, Surface), (Good, Surface), (Danger, Surface), (OnAccent, Accent).
  - `NoStaticReferencesToThemeBrushes`: no `.xaml` under `src/Hearthsheet.App` matches `StaticResource (Ink|InkMuted|Accent|AccentHover|AccentSoft|OnAccent|Surface|ReadOnlyFill|Canvas|Sidebar|Line|LineStrong|Good|Danger)\}`.
  - `NoHardCodedColoursOutsideThemes`: no `.xaml` under `src/Hearthsheet.App` other than `Themes/Light.xaml`, `Themes/Dark.xaml` and `Themes/SheetStyles.xaml` matches `"#[0-9A-Fa-f]{3,8}"` or `="(White|Black)"`.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~ThemeTests"`. Expected: FAIL (files missing, static refs present).
- [ ] **Step 3: Create Light.xaml** (today's values plus `OnAccent`) and **Dark.xaml**: a warm dark palette (deep brown-charcoal surfaces, parchment ink, a lightened crimson accent) that passes the pairs above. Pick the values by running the test.
- [ ] **Step 4: Convert references.** Replace `{StaticResource <key>}` with `{DynamicResource <key>}` for the 14 keys in every App `.xaml`, including `Styles.xaml` setters and template triggers. Move the 4 hard-coded hex colours in view XAML into theme keys; where one has no matching key, use the closest existing key rather than adding a new one. Replace `Foreground="White"` on accent fills with `{DynamicResource OnAccent}`.
- [ ] **Step 5: Implement ThemeManager.** `Apply` saves nothing; it resolves System by reading `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (0 = dark; missing = light). It then replaces the merged dictionary whose `Source` ends in `Light.xaml`/`Dark.xaml` in `Application.Current.Resources.MergedDictionaries`, sets the dark title bar on every window in `Application.Current.Windows`, and raises `Changed`. Title bar: P/Invoke `dwmapi!DwmSetWindowAttribute(hwnd, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref int, sizeof(int))`, ignoring failures. New windows: register once with `EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, ...)` to set their title bar. Follow Windows: subscribe once to `SystemEvents.UserPreferenceChanged` (category `General`) and re-apply only when `Choice == System`, marshalled to the dispatcher.
- [ ] **Step 6: Startup.** In `App.OnStartup`, call `ThemeManager.Apply(UiPreferences.LoadTheme(AppPaths.UiPreferences))` before `new MainWindow`.
- [ ] **Step 7: Run** `dotnet build Hearthsheet.slnx` and the ThemeTests filter. Expected: build succeeds, tests PASS.
- [ ] **Step 8: Commit** `Split desktop palette into Light/Dark themes with live switching`.

### Task 3: Dark-aware desktop controls

**Files:**
- Modify: `src/Hearthsheet.App/Themes/Styles.xaml`

**Interfaces:**
- Consumes: the theme keys from Task 2 (all via `DynamicResource`).
- Produces: implicit styles (no `x:Key`) for `ComboBox` (both `IsEditable` modes, with its `ComboBoxItem` and drop-down `Popup`), `CheckBox`, `RadioButton`, `ScrollBar` (vertical and horizontal, with thumb), `ToolTip`, `ContextMenu`, `MenuItem` (flat: check mark, header, `InputGestureText`; no submenu support), `Separator` (menu), `ListBox`/`ListBoxItem`, `PasswordBox`, `ProgressBar`, `TabControl`; plus `DataGrid` row/row-header/select-all parts not yet styled.

- [ ] **Step 1: Write each template** using `Surface` for fields and popups, `LineStrong` for borders, `Accent` for focus, checked state and selection (selection also uses a check glyph or bold text, not colour alone), `Ink`/`InkMuted` text, and `AccentSoft` for hover. Keep today's light look: sizes, paddings and corner radii match the existing `TextBox` style.
- [ ] **Step 2: Temporary switch for manual testing:** in `App.OnStartup` (Task 2 Step 6), honour an `#if DEBUG` env var `HEARTHSHEET_THEME=Dark`. Remove it in Task 4 Step 4, when the real switch exists. (Project rule: no env-var config ships.)
- [ ] **Step 3: Build** with `dotnet build Hearthsheet.slnx`. Expected: success.
- [ ] **Step 4: Point the app at a scratch data folder** with sync blanked (memory note: scratch data still syncs). Set `Application:DataDirectory` to a temp folder and `Sync:Url` to `""` in `%LOCALAPPDATA%\Hearthsheet\appsettings.user.json`, saving a backup of the original file first. Restore it at the end of Task 4.
- [ ] **Step 5: Run in Dark** (`dotnet run --project src/Hearthsheet.App`) and screenshot the Details, Play and Sheet tabs, an open editable and an open non-editable ComboBox, a tooltip, the New character, Level up, Short rest, Quick add, Account and Update dialogs, and the Help window. Expected: no light control chrome anywhere except inside the white sheet pages.
- [ ] **Step 6: Review Focus 1 and 3:** with Match system, flip Windows' app mode in Settings while a dialog is open. The app, the dialog and its title bar follow. (Re-run this after Task 4 with explicit Light/Dark: no change on a Windows flip.)
- [ ] **Step 7: Commit** `Theme-aware templates for remaining desktop controls`.

### Task 4: Desktop sheet stays white, themed message dialog, toolbar

**Files:**
- Create: `src/Hearthsheet.App/Views/MessageDialog.xaml` + `.xaml.cs`
- Modify: `src/Hearthsheet.App/Views/SheetView.xaml` (page elements merge `Themes/Light.xaml` in their `Resources`), `src/Hearthsheet.App/Dialogs.cs` (`Show` uses `MessageDialog`), `src/Hearthsheet.App/App.xaml.cs` (`OnDispatcherUnhandledException` uses `MessageDialog`; the two startup-failure `MessageBox` calls stay, because themes may not be loaded), `src/Hearthsheet.App/Views/HelpWindow.xaml.cs:43` (uses `MessageDialog`), `src/Hearthsheet.App/MainWindow.xaml` (toolbar replaces `<Menu>` lines 30–82), `src/Hearthsheet.App/MainWindow.xaml.cs` (compact-labels width check), `src/Hearthsheet.App/ViewModels/MainViewModel.cs` (theme command), `src/Hearthsheet.App/Themes/Styles.xaml` (`ToolbarButton` style)

**Interfaces:**
- Consumes: `ThemeManager`, `UiPreferences`, `ThemeChoice` (Tasks 1–2).
- Produces:
  - `MessageDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage image)` with `public MessageBoxResult Result { get; }` and `public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)`. Buttons in Windows order (Yes, No, Cancel / OK). The first button `IsDefault`. Escape and the close box give Cancel for YesNoCancel, No for YesNo and OK for OK. The image is a Segoe Fluent glyph with `AutomationProperties.Name` ("Question", "Error", "Warning", "Information"). Text is selectable and wraps, max width 480.
  - `MainViewModel.SetTheme : ICommand` (parameter `ThemeChoice`). It calls `ThemeManager.Apply` then `UiPreferences.SaveTheme(AppPaths.UiPreferences, choice)`, and raises `PropertyChanged` for `IsLightTheme`, `IsDarkTheme` and `IsSystemTheme` (bools bound to the More menu checkmarks).

- [ ] **Step 1: Sheet stays white.** Merge `Light.xaml` into the `Resources` of each sheet page element (the `Page` style's target) so lookups inside a page resolve light. The scroll area around the pages uses `Canvas` (dark in Dark). Verify in Dark that the pages look the same as in Light and the backdrop is dark.
- [ ] **Step 2: MessageDialog and call sites** per the interface above. `Dialogs.Show` keeps its signature and returns `MessageDialog.Show(Owner, ...)`.
- [ ] **Step 3: Toolbar.** Replace the `<Menu>` with a `DockPanel` (`Background` `Surface`, bottom border `Line`, padding 8,4). Left to right: `+ New character`, `Save`, `Print sheet`, `Sync now` (visible when `Sync.IsConfigured`; when `!Sync.IsSignedIn` the same slot shows `Sign in…` bound to `Sync.SignIn`), `Help ▾`. Docked right: `⋯ More`, then the existing signed-in email text. `Help ▾` and `⋯ More` open a `ContextMenu` placed below the button (`PlacementTarget` = button, `Placement="Bottom"`).
  - **Help ▾** lists the four Help entries.
  - **⋯ More** lists: Import, Export, Export all, Duplicate, Delete; then Zoom in, Zoom out, Reset zoom; then the `Theme` label and three checkable items (Match system / Light / Dark); then Sign in, Sync now, Sign out (only when `Sync.IsConfigured`); then Check for updates, Open log folder, Open data folder; then Exit.
  - Keep the access keys and `InputGestureText` from the old menu items, and keep all `Window.InputBindings`.
  - Add `Alt` access: the More button's `Content` uses `_More`, and Help uses `_Help`.
  - `ToolbarButton` style: `MinHeight` 36, `FontSize` 14, padding 12,6, transparent background with `AccentSoft` hover, content = icon glyph + label `TextBlock` with `x:Name="Label"`, `ToolTip` = label, `AutomationProperties.Name` = label.
  - In `MainWindow.xaml.cs`, `SizeChanged` sets the toolbar's `Tag` to `"Compact"` when `ActualWidth < 1100`; a style `DataTrigger` on that `Tag` collapses `Label`.
- [ ] **Step 4: Remove** the temporary `HEARTHSHEET_THEME` env var from Task 3 Step 2.
- [ ] **Step 5: Build and run** (scratch settings from Task 3 Step 4).
  - Expected: the theme switches live from ⋯ More, and the choice survives a restart.
  - Delete a character to see the themed confirm, in both themes.
  - Labels collapse when the window is narrower than 1100 and every button still has a tooltip.
  - **Review Focus 4:** `Ctrl+P` in Dark, print to "Microsoft Print to PDF", and open the PDF: white pages, no dark backdrop.
  - Re-run Task 3 Step 6 with explicit Light and Dark.
  - Then restore the original `appsettings.user.json` from the backup.
- [ ] **Step 6: Run** `dotnet test Hearthsheet.slnx`. Expected: all pass.
- [ ] **Step 7: Commit** `Desktop toolbar, themed message dialog, white sheet in dark mode`.

### Task 5: Web dark theme tokens and early theme script

**Files:**
- Create: `src/Hearthsheet.Web/wwwroot/js/theme.js`
- Modify: `src/Hearthsheet.Web/wwwroot/css/app.css`, `src/Hearthsheet.Web/wwwroot/index.html` (add `<script src="js/theme.js"></script>` in `<head>` after the stylesheet), `src/Hearthsheet.Web/wwwroot/service-worker.published.js` only if its asset list needs `js/theme.js` (it matches `/\.js$/`, so probably not)
- Test: `tests/Hearthsheet.Tests/ThemeTests.cs`

**Interfaces:**
- Produces: `window.hearthsheetTheme = { get(): "system"|"light"|"dark", set(choice) }` (classic script, not a module). Storage key: `localStorage["hearthsheet.theme"]`, absent = system. Effective theme goes on `document.documentElement.dataset.theme` as `"light"` or `"dark"` and follows `matchMedia("(prefers-color-scheme: dark)")` changes while on system. All `localStorage` access is in try/catch; on failure it falls back to system.
- CSS: the `:root` block (light, unchanged values, plus new `--on-accent: #FFFFFF`, `--sheet-backdrop: #D9D4CB` and `--shadow: 31, 27, 22` for the `rgba()` shadows) and one `:root[data-theme="dark"]` block with the same variable names and `color-scheme: dark`.

- [ ] **Step 1: Write the failing tests** in `ThemeTests.cs`. Helper: `static Dictionary<string,string> CssVars(string css, string selector)` (regex the block that starts with the selector, then `--name: #hex;` pairs).
  - `WebThemes_HaveTheSameVariables`: the variable names in `:root {` and `:root[data-theme="dark"] {` are equal.
  - `WebThemes_TextPairsMeetAA` (Theory light/dark): the same pairs as Task 2, mapped to `--ink`, `--surface`, `--canvas`, `--sidebar`, `--read-only`, `--accent-soft`, `--ink-muted`, `--accent`, `--good`, `--danger`, `--on-accent`, all ≥ 4.5.
  - `Css_HasNoHardCodedColoursOutsideTokens`: outside the two token blocks, the `.sheet { ... }` token line, the `.sheet-page` light-token block (Task 7) and `@media print`, no `#hex` or `rgba(` literal except `rgba(var(--shadow), …)`.
  - `IndexHtml_LoadsThemeScriptBeforeBlazor`: `js/theme.js` appears in `index.html` before `blazor.webassembly.js`, and there is no inline `<script>` body.
- [ ] **Step 2: Run** the ThemeTests filter. Expected: FAIL.
- [ ] **Step 3: Implement** the dark block (a warm palette matching the desktop Dark.xaml values, so both apps look alike), `theme.js`, and the `index.html` tag. Replace the hard-coded `#fff` on accent buttons with `var(--on-accent)`, `.sheet-scroll`'s background with `var(--sheet-backdrop)`, and the shadow `rgba(31, 27, 22, x)` with `rgba(var(--shadow), x)`. The preview/error banners (`#fde68a`) get `--banner-bg`/`--banner-ink` tokens. Update the comment at the top of `app.css` ("Light only") to describe the two themes.
- [ ] **Step 4: Run** the ThemeTests filter. Expected: PASS.
- [ ] **Step 5: Commit** `Web dark theme tokens and early theme script`.

### Task 6: Web toolbar

**Files:**
- Modify: `src/Hearthsheet.Web/Components/Shell.razor` (header lines 17–33), `src/Hearthsheet.Web/wwwroot/css/app.css` (`.app-bar`, `.account-menu` rules, renamed to `.menu-button`)

**Interfaces:**
- Consumes: `window.hearthsheetTheme` (Task 5), via `JS.InvokeVoidAsync("hearthsheetTheme.set", choice)` and `JS.InvokeAsync<string>("hearthsheetTheme.get")` on first render.
- Existing calls to reuse: `_shell.CreateAsync`, `Print()` (already sets `_view` and `_print`), `_sync.RunAsync`/`_sync.IsBusy`, `Help.Open(HelpDocs.*)`, `HelpDocs.WikidotUrl`, `ExportAllAsync`, `DownloadDiagnosticsAsync`, `SignOut`, `Account.Email`.

- [ ] **Step 1: Header layout:** `<h1>Hearthsheet</h1>`, then a `<div class="toolbar" role="toolbar" aria-label="Commands">` with buttons `+ New character`, `Print sheet` (disabled when no character is open), `Sync now` (disabled while `_sync.IsBusy`) and a `Help ▾` `<details class="menu-button">`. Then the `⋯ More` `<details class="menu-button">`, pushed right. Each button has a glyph `<span aria-hidden="true">` and a `<span class="label">`.
  - **⋯ More** contents: the account email (muted, not a button); New character, Print sheet, Sync now; the four Help entries; Export all, Download diagnostics; a `Theme` group of three buttons with `aria-pressed` (Match system / Light / Dark; the pressed one also shows a ✓); Sign out.
  - Picking any menu entry closes its `<details>` (remove the `open` attribute via a bound `bool`).
  - Escape closes an open menu and returns focus to its summary. This needs a small `keydown` handler in `js/hearthsheet.js` (there are no inline handlers because of the CSP).
- [ ] **Step 2: CSS:**
  - `.toolbar button`, `.menu-button summary`: `min-height: 36px; font-size: 14px; padding: 6px 12px;`, and `min-height: 44px` under `@media (pointer: coarse)`.
  - `@media (max-width: 60rem)`: `.toolbar .label` becomes visually hidden (same rules as `.visually-hidden`), keeping the accessible name.
  - `@media (max-width: 30rem)`: hide the Help, Sync and Print toolbar items; they stay in More.
  - The header never scrolls horizontally: `flex-wrap: nowrap; min-width: 0`, and the email truncates with an ellipsis.
- [ ] **Step 3: Build** with `dotnet build Hearthsheet.slnx`. Expected: success.
- [ ] **Step 4: Commit** `Web toolbar with More menu and theme choice`.

### Task 7: Web sheet stays white, full-app check

**Files:**
- Modify: `src/Hearthsheet.Web/wwwroot/css/app.css`

- [ ] **Step 1: Light tokens on the sheet page.** Add a `.sheet-page { color-scheme: light; --ink: …; --surface: …; … }` block re-declaring every light token, so controls inside a page stay light in dark mode. Add a `@media print` override forcing the light `:root` tokens and `body { background: #FFFFFF }` (that rule already exists; keep it).
- [ ] **Step 2: Run the web app** under `.claude/launch.json` (add a config if missing: `dotnet run --project src/Hearthsheet.Web`, using its launchSettings port) with `preview_start`. Sign in to the **Hearthsheet-Test** project only (Development settings); if no test account exists, ask the user.
- [ ] **Step 3: In Dark, take screenshots** of the sign-in screen, the character list, Details, Play, Sheet (pages white, backdrop dark), every dialog (new character, level up, quick add, short rest, help, sign out), the update banner (`UpdateBanner.razor`) and the status bar. Expected: no light surfaces outside the sheet pages, and readable text everywhere.
- [ ] **Step 4: Switch** Light → Dark → Match system from ⋯ More. Reload: no light flash and the choice is kept. With Match system, emulate `prefers-color-scheme: dark` via `resize_window colorScheme` and confirm the app follows.
- [ ] **Step 5: Review Focus 4 (web):** use the browser's print preview from Dark, or emulate print media with `javascript_tool` checking the computed `.sheet-page` background, and confirm the page prints light.
- [ ] **Step 6: Run** the ThemeTests filter (`Css_HasNoHardCodedColoursOutsideTokens` must still pass with the new block). Expected: PASS.
- [ ] **Step 7: Commit** `Keep the web sheet white in dark mode`.

### Task 8: Docs and final verification

**Files:**
- Modify: `docs/user/tutorial.md` (and any other `docs/user/*.md` that names the File / View / Account / Help menus), `docs/ARCHITECTURE.md` (a short "Themes" subsection: the Light/Dark dictionaries, the DynamicResource rule for the 14 theme keys, ThemeManager, `ui.json`, web `data-theme` + `theme.js`, sheet pages forced light), `src/Hearthsheet.Web/wwwroot/css/app.css` header comment if not already done.

- [ ] **Step 1: Update the docs.** Menu references become toolbar / ⋯ More references, and add a line on choosing a theme. Run `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~HelpDocsTests"` (it checks Help anchors). Expected: PASS.
- [ ] **Step 2: Run** `dotnet build Hearthsheet.slnx -c Release` and `dotnet test Hearthsheet.slnx -c Release`. Expected: 0 errors, all tests pass.
- [ ] **Step 3: Run** `dotnet publish src/Hearthsheet.Web -c Release`. Expected: succeeds (CI runs it).
- [ ] **Step 4: Review Focus 5:** at a 375×812 viewport in the web preview, `document.documentElement.scrollWidth <= innerWidth` holds on Play, Details and Sheet's toolbar row. Tab to ⋯ More, open it with Enter, reach the theme buttons, and close it with Escape.
- [ ] **Step 5: Commit** `Docs: toolbar and themes`, push the branch, and open a PR into `dev` with screenshots of both themes.

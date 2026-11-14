# Toolbar and dark mode

## Intent

The desktop menu bar (File, View, Account, Help) is too small to read and click comfortably, and the web app has no
command bar at all, only an account dropdown. Replace both with a toolbar of larger styled buttons that keeps the
common actions one click away and folds the rest away on narrow windows. Add a dark mode that covers every screen
in both apps so the app is comfortable at the table at night.

This is the first of four requested features (then the attack roller on Play, then resources with rolls on Use);
those get their own designs.

## Goals

- Toolbar buttons that are easy to read and hit: noticeably larger text and targets than today's menu
  (at least 32 DIP tall with 14px+ text on desktop; on the web, at least 44px on touch screens).
- One click for frequent actions; everything else under a "⋯ More" menu. Nothing the menu offers today is lost.
- As the window or screen narrows, labels drop to icon-only, then buttons move into ⋯. No horizontal scrolling.
- Light, Dark and "Match system" themes, default Match system, remembered per device.
- In dark mode, every screen, dialog, popup, dropdown list, tooltip, scrollbar and context menu is dark and readable.
  Text/background pairs meet WCAG AA (4.5:1 normal text, 3:1 large text and UI boundaries), the same bar the
  light theme targets.
- Desktop switches theme immediately, without restart.

## Non-goals

- Changing the light theme's look. Light mode should look the same as today apart from the toolbar.
- Customisable toolbars (users choosing which buttons appear).
- Theme sync across devices (it's a per-device preference, not character data).

## Anti-goals

- Bright white controls (dropdowns, popups, scrollbars) inside a dark window. That fails "100% readable".
- State shown by colour alone in either theme (project rule).
- Printing in dark: printouts are always the light sheet.

## Decisions made with the user

- **Toolbar shape: direct buttons plus ⋯ More** (not File/View/Account/Help dropdown buttons). Reason given: the
  current menu is too small to read and click.
- **The character sheet stays white in dark mode**, like a sheet of paper on a dark desk, so the screen matches the
  printout. Only the area around it (canvas, toolbar, sidebar) goes dark. The sheet keeps its own `Sheet*` brushes.
- **Desktop dark mode by hand-styling** the controls the app doesn't restyle today, not by adopting WPF's Fluent
  theme (its theme switching is still experimental in .NET 10 and it restyles every control, changing light mode).

## Approaches considered

- Toolbar: (A) File/View/Account/Help as large dropdown buttons, (B) direct buttons for common actions plus ⋯ More.
  **B chosen** (see above).
- Sheet in dark mode: (A) stays white, (B) dark on screen and white when printed. **A chosen**.
- Desktop dark controls: (A) hand-style ComboBox, CheckBox, ScrollBar, ToolTip, ContextMenu/Menu and anything else
  still drawing system chrome; (B) Fluent theme. **A chosen**.
- Desktop switching: live vs. apply on restart. **Live chosen** (my call; a restart prompt would feel broken). It
  means every theme brush reference becomes `DynamicResource`.

## Design

### Toolbar contents

Desktop (replaces the `Menu` in `MainWindow.xaml`; the signed-in email stays at the right):

| Button | Notes |
|---|---|
| + New character | Ctrl+N |
| Save | Ctrl+S |
| Print sheet | Ctrl+P |
| Sync now | only when sync is configured; signed-out state offers Sign in instead |
| Help ▾ | Tutorial, Beginner's guide, Look up rules, Legal and credits |
| ⋯ More | Import, Export, Export all, Duplicate, Delete; Zoom in/out/reset; Theme (Light / Dark / Match system); Account (Sign in, Sign out); Check for updates; Open log folder, Open data folder; Exit |

The Sheet / Details / Play tabs already exist below the toolbar, so View's tab commands are not repeated (keyboard
shortcuts stay). All existing keyboard shortcuts keep working.

Web (replaces the account dropdown in the `app-bar` header of `Shell.razor`):

| Button | Notes |
|---|---|
| + New character | |
| Print sheet | |
| Sync now | |
| Help ▾ | same four entries as desktop |
| ⋯ More | Export all, Download diagnostics, Theme, Sign out (account email shown at the top) |

The web saves automatically, so it has no Save button. Import and export of one character stay where they are today
in the web character list.

### Shrinking

- Desktop: use WPF's `ToolBar` (its overflow already moves buttons that don't fit into a ⋯ popup). Under a width
  threshold, button labels collapse and icons stay (tooltips and `AutomationProperties.Name` keep the label).
  Priority order for overflow: Help first, then Sync, Print, Save; New stays visible longest.
- Web: a flex row; at a medium breakpoint labels hide (visually hidden, still announced), at narrow width the lower
  priority buttons move into ⋯ More. Same priority order as desktop.
- Icons: Segoe Fluent Icons / Segoe MDL2 Assets glyphs on desktop (already on Windows, no new assets). Inline SVG or
  text glyphs on the web (no icon library).

### Themes

- One set of semantic colour names already exists in both apps (`Ink`, `InkMuted`, `Accent`, `Surface`, `Canvas`,
  `Sidebar`, `Line`, `Good`, `Danger`, … in `Themes/Styles.xaml`; `--ink`, `--surface`, … in `app.css`). Dark mode
  is a second value for each name. No new names unless a hard-coded colour is found that needs one.
- Desktop: move the brush definitions into `Themes/Light.xaml` and `Themes/Dark.xaml`; `App.xaml` merges one, and
  switching replaces it in `Application.Resources.MergedDictionaries`. Every reference to a theme brush becomes
  `DynamicResource` (about 130 today, plus `Color` resources used inside brushes). The 4 hard-coded hex colours in
  view XAML move into theme resources. `Sheet*` brushes stay light-only.
- Desktop controls to restyle so they follow the theme: ComboBox (box and drop-down list), CheckBox, ScrollBar,
  ToolTip, ContextMenu and MenuItem (also used by the ⋯ and Help popups), the DataGrid's remaining parts, the
  `ToolBar` itself, and the window title bar (dark title bar via `DwmSetWindowAttribute` with
  `DWMWA_USE_IMMERSIVE_DARK_MODE`). Every dialog window (`Views/*Dialog.xaml`, Help, Update, Account) must use the
  themed resources too.
- "Match system" on desktop reads Windows' app theme (`AppsUseLightTheme` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`) and follows changes while running
  (`SystemEvents.UserPreferenceChanged`).
- Web: dark values for the CSS variables under `:root[data-theme="dark"]` and under
  `@media (prefers-color-scheme: dark)` for Match system; set `color-scheme` so native form controls and
  scrollbars follow. The sheet view keeps light colours. `@media print` always uses the light values.
  The service worker's offline page and the sign-in screen are themed too.
- Persisting the choice: web in `localStorage` (the existing `setLocal`/`local` helpers). Desktop: builder's call
  (see below).
- Help documents (`docs/user/*` rendered in the Help window and web viewer) must be readable in dark.

### Docs and tests

- Update the user docs/tutorial wherever they mention the File / View / Account / Help menus, and the in-app Help.
- `docs/ARCHITECTURE.md` gets a short note on the theme dictionaries and the DynamicResource rule.
- Core has no UI, so tests are limited: a test that every key in `Light.xaml` exists in `Dark.xaml` (and the CSS
  equivalents) and that each foreground/background pair passes the contrast ratio, if it can be done without
  referencing the App project (e.g. parsing the XAML files as XML from the test project).

## Settled after the builder check (my calls)

- **Web sheet stays white:** `app.css` already scopes `--sheet-*` variables to `.sheet`. In dark mode `.sheet-page`
  keeps light values, and the grey `.sheet-scroll` backdrop (hard-coded `#D9D4CB`) becomes a theme variable so the
  "desk" goes dark. Play and Details go dark like everything else.
- **Sync button state already exists:** desktop `SyncViewModel` has `IsConfigured`, `IsSignedIn`, `SignIn`,
  `SyncNow`; the web has `WebSync.RunAsync`/`IsBusy`. No new view-model state.
- **Desktop overflow:** the ⋯ More button sits outside the `ToolBar`, docked right, so it's always reachable.
  Items in the `ToolBar` are ordered so the lowest priority (Help) is last and overflows first. WPF's own overflow
  chevron is hidden; overflowed items appear at the top of ⋯ More instead (via `ToolBar.OverflowMode` and a bound
  list, or a simple width check in code-behind). The builder picks the simpler of the two that works.
- **No light flash at startup:** desktop applies the theme before `MainWindow` is shown. The web sets `data-theme`
  from a small `js/theme.js` loaded by a `<script src>` in `index.html` before Blazor starts. The site's
  Content-Security-Policy forbids inline scripts, so it must be a file.
- **Desktop storage:** a small per-user `ui.json` in the data folder (`%LOCALAPPDATA%\Hearthsheet`), not
  `appsettings.user.json`. A missing or unreadable file means Match system.
- **Confirmation pop-ups (user's decision):** the 9 `MessageBox` calls in `src/Hearthsheet.App/Dialogs.cs` are
  replaced by one small themed confirm/message dialog window in the app's own style, so prompts like "Delete this
  character?" follow the theme. Same buttons, default and Escape behaviour as today.
- **Windows' own dialogs** (file open/save, print) follow Windows' own setting and can't be themed by the app.
  Accepted.
- **Help renderers:** both are the app's own Markdown renderers (WPF in `HelpWindow`, HTML in `HelpViewer.razor`)
  using theme resources, so they follow the theme once their few `StaticResource` references are converted.

## Left to the builder

- Exact dark palette values (warm, matching the app's parchment and oxblood feel), within the contrast bar above.
- Icon choice for each button.
- Where the desktop stores the theme choice: a small per-user file next to `appsettings.user.json` or a key in the
  SQLite `meta` table. It must not rewrite the user's hand-edited `appsettings.user.json`.
- The exact width thresholds for label collapse and overflow.

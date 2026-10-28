# Hearthsheet for the web: design

## Intent

Three of the six people in Beau's weekly group play on Macs, and Hearthsheet is a Windows-only WPF app. The goal is a
**web version that does everything the desktop app does**, runs on any Mac or Windows browser, keeps working offline
at the table, and **stays in sync with the desktop app** so every person can use either one and see the same characters.
It has to stay **free forever** for this group, and it has to be audited for security, efficiency and accessibility
before friends get the link.

Long term, Mac support matters because many of Beau's friends use Macs. A native Mac build was ruled out: without a paid
Apple developer account, every install and every update would need a trip to System Settings to allow it. A browser app
avoids signing, installers and per-platform updaters entirely.

## Who and how much

- Six people (three on Mac, three on Windows), at most two characters each, so roughly a dozen characters in total.
- One session a week plus small use in between.
- **Each person sees and edits only their own characters.** Sharing or collaboration may come later; nothing in this
  design may make it harder (for example, don't bake "one owner forever" into anything beyond the existing `user_id`).

## Goals

1. **Full parity with the desktop app:**
   - the Sheet, Details and Play views
   - every dialog: new character, level up, short rest, quick add, token picker, account (sign up, sign in, stay signed
     in, forgot password with emailed code, account switch)
   - import and export of `.dndchar`
   - printing the letter-size sheet
   - conflict copies and the "Updated from another device" behaviour

   The desktop's updater and installer have no web equivalent and are replaced by the service-worker update described
   below.
2. **Desktop and web stay in sync** through the existing Supabase project, the existing `characters` table and the
   existing `CloudSync` engine. A character edited on one shows up on the other with no manual steps.
3. **Offline-first.**
   - After the first visit, the app loads and works with no network, and can be installed from the browser to the Mac
     Dock or the Windows Start menu.
   - Characters are stored in the browser.
   - Sync runs when the network is back.
   - **An account is required on the web** (Beau's decision): the web app shows nothing but sign-in/sign-up until a
     session exists. That way the cloud always holds a copy, and losing browser data loses nothing that was synced.
   - Sign-in is needed once per browser, not per session, and being offline never locks anyone out of their characters.
4. **One version for both.**
   - The web app ships from the same `<Version>` in `Directory.Build.props`, in the same release workflow, at the same
     moment as the desktop release.
   - CI builds and tests the web project on every PR, like the desktop.
5. **$0, forever, for this group.**
   - Hosting is a free static host.
   - The database, sign-in and sync are the existing Supabase free project; email is the existing Resend free tier.
   - No paid domain for now (`hearthsheet.com` may come later, which only changes DNS and the allowed origins).
6. **Audited before launch** (see "Audits and launch gate").

## Non-goals

- Sharing characters, DM views, real-time collaboration.
- A native macOS or iOS app.
- Phone-first layouts. The web app must be usable on a laptop at any window width and on a tablet. Phones are a
  nice-to-have; nothing may be impossible on a phone, but layouts are designed for laptop screens (Claude's call).
- Server-side code of our own (no API server, no Edge Functions) unless an audit finding requires one.
- Retiring the desktop app.

## Anti-goals (failure even if every feature works)

- **Any character is lost**, by sync, by browser storage, by an update or by an outage.
- **Any bill.** No payment method is ever added to Supabase, Cloudflare, GitHub or Resend for this. A limit being hit
  must mean "restricted", never "charged".
- **An outage that stops play.** If Supabase is down, paused or over a limit, the web and desktop apps keep working
  locally.
- **A security problem for Beau or a friend:**
  - one account reading or changing another's data
  - a leaked secret
  - script injection
  - a hijacked deploy
  - a stolen session leading anywhere beyond that one person's characters
- Desktop and web drifting apart in rules, so the same character shows different numbers in each.

## Constraints

- Everything in `CLAUDE.md` still holds:
  - minimal dependencies
  - parameterized SQL
  - no secrets in logs
  - every input has an accessible name
  - state never shown by colour alone
  - SRD 5.1 content only
  - feature branches into `dev`, never push to `dev` or `main`
- **Core stays the single home of the game rules.** The web app must not reimplement any rule; it calls the same Core
  code the desktop does.
- The server schema in `supabase/migrations/` is shared by both apps. Schema changes are new numbered migration files,
  backward-compatible with any desktop version still in use.
- The character JSON format and `CharacterJson.CurrentSchemaVersion` are shared. A web build never writes a schema the
  same-numbered desktop build can't read (they are the same code, so this holds by construction).

## Decisions (Beau's)

- Web version, not a native Mac app. Desktop stays.
- Desktop and web always in sync, each person free to use either.
- Offline at the table is required.
- Everything the desktop does.
- Same release, same version number.
- The web requires signing in, and stores each account's characters separately in the browser (decision 7a).
- Free for six people indefinitely; no paid domain yet.
- Security, efficiency and accessibility audits across several disciplines before friends use it. Beau installed the
  `supabase`, `Security Audit Loop` and `Perf Profiler` plugins for this.

## Decisions (Claude's calls; Beau can override)

### Technology

1. **Blazor WebAssembly, standalone, as a Progressive Web App**, in a new project `src/Hearthsheet.Web`.
   - It runs Core in the browser unchanged, which is the only way to guarantee identical rules with no duplicated logic.
   - No component library: plain Razor components and CSS, matching the "few dependencies" rule.
2. **Shared presentation layer.**
   - `CharacterViewModel`, `PlayViewModel` and `Rows` (in `src/Hearthsheet.App/ViewModels`) hold the derived-value and
     command logic both UIs need. They move to a new UI-neutral project, for example `src/Hearthsheet.Presentation`
     (`net10.0`, no WPF), which both apps reference. That way "what the sheet shows" can't drift between desktop and
     web.
   - WPF-specific pieces stay behind small seams:
     - `Dispatcher.CurrentDispatcher.BeginInvoke` in `CharacterViewModel.cs:207` becomes an injected "post to UI" action.
     - `DispatcherTimer` in `MainViewModel` and `SyncViewModel` becomes an injected timer or stays in the WPF app.
     - `Dialogs` is a concrete WPF class passed into `CharacterViewModel` and `PlayViewModel` (`CharacterViewModel.cs:22`,
       `PlayViewModel.cs:19`), so it gets an `IDialogs` interface in Presentation, implemented per app.
   - `ICommand` itself is cross-platform (it lives in `System.ObjectModel`). `RelayCommand` is not: it hooks WPF's
     `CommandManager.RequerySuggested` (`Mvvm/RelayCommand.cs:11-12`). Presentation gets a version that raises
     `CanExecuteChanged` explicitly. The WPF app can keep requery by subscribing in its own subclass or adapter.
   - `MainViewModel` is split only as far as needed: library/open/save/autosave logic shared, file dialogs and SQLite
     specifics per app.
   - The builder decides the exact cut.
3. **Shared Supabase client.**
   - `SupabaseHttp`, `SupabaseAuth` and `SupabaseCharacterStore` move out of `Infrastructure` (which pulls in SQLite and
     Windows-only code) into a project both apps can reference: a new `Hearthsheet.Cloud`, or into Core if that stays
     free of platform code. Builder's choice.
   - `ISecretStore` moves with them. `SupabaseAuth.Restore` catches `Win32Exception` from the Windows credential store;
     that handling moves into `WindowsCredentialStore`, so the shared code stays platform-neutral.
   - On the web, `SupabaseHttp` is constructed with the browser's default handler (`new HttpClientHandler()`), not
     `SocketsHttpHandler`, which the browser doesn't support (`SupabaseHttp.cs`, constructor).
4. **`CloudSync` gets no blocking waits.**
   - `Relink` uses `_gate.Wait()` (`CloudSync.cs:104`), and browser WebAssembly is single-threaded, so it becomes
     `RelinkAsync` with `WaitAsync`.
   - Desktop callers are updated.
   - The builder also checks for any other `.Wait()`, `.Result` or `GetAwaiter().GetResult()` on the web's code path.
5. **Trimming.**
   - `ChangeTracker` discovers properties by reflection and `System.Text.Json` deserializes by reflection.
   - The web build must keep Core's and Presentation's types intact (`TrimmerRootAssembly`, or trimming off for those
     assemblies).
   - A test or startup self-check must fail loudly if change tracking stops firing, because the symptom (autosave
     silently not running) is exactly the data-loss anti-goal.

### Storage in the browser

6. **In-memory store, persisted to IndexedDB.**
   - `ICharacterRepository` and `ISyncLocalStore` are synchronous, and IndexedDB is async-only. The web implementation
     keeps all of a person's characters and sync bookkeeping in memory (a dozen documents) and writes each change
     through to IndexedDB in the background, in order.
   - Startup loads everything from IndexedDB before the UI shows.
   - Bookkeeping matches the desktop: `dirty`, `local_revision`, `cloud_revision`, pending deletes, `sync_account`,
     `sync_last_pull`.
   - A small JS module does the IndexedDB calls; that's the only hand-written JS besides the service worker.
   - A failed IndexedDB write must surface to the user ("Not saved on this device"), never be swallowed.
7. **One tab at a time.**
   - Two tabs in the same browser would both hold in-memory copies and overwrite each other.
   - The app takes a Web Lock (`navigator.locks`). A second tab shows "Hearthsheet is open in another tab" with a button
     to take over (the first tab then saves, releases and goes read-only).
7a. **Accounts on the web** (Beau's decision; details Claude's).
    - **Sign-in required.** There's no signed-out mode on the web, so the desktop's account-switch prompt and `Relink`
      (moving one database's characters into another account) never run on the web.
    - **One store per account.** IndexedDB data is keyed by the Supabase user id. Signing in as someone else on the same
      browser opens only that person's store, so person B never sees or uploads person A's characters.
    - **Expired session while offline.** The store of the last signed-in user stays open for editing, with "Signed out
      – sign in to resume sync". Nobody is locked out at the table.
    - **Signing in again** as that user resumes sync from the same store. Signing in as a different user switches stores
      and leaves the other one untouched.
    - **Signing out:**
      - If everything is synced, the app removes that account's store from this browser. The cloud has it all.
      - If anything is unsynced, the app first tries to sync. If that fails (offline), it says how many changes would be
        lost and offers "Stay signed in", "Export them, then sign out" or "Sign out anyway".
8. **Durability.**
   - On first sign-in the app calls `navigator.storage.persist()`.
   - Safari deletes storage for sites not used for 7 days unless they're installed to the Dock, so the app encourages
     installing.
   - The cloud copy is the real safety net. The app shows a visible "N changes not yet synced" indicator whenever
     something is dirty, so offline edits are never silently at risk.
9. **Backups.**
   - Every device holds a full copy, and the cloud holds one more.
   - **Export all characters** (one `.zip` of `.dndchar` files) goes into **both** apps.
   - The desktop keeps its existing database backups.
   - No scheduled server-side backup job: it would need a key that bypasses row-level security stored as a CI secret, and
     on a public repo the backup files themselves are hard to keep private. The risk of that outweighs the benefit at
     this scale. Revisit if the group grows.

### Sync on the web

10. **Same engine, web triggers.**
    - `CloudSync.RunAsync` runs at startup, every few minutes while the tab is visible, when the browser reports it's
      back online, and when the tab is hidden (`visibilitychange`), which is the web's "on close".
    - The browser has one thread. The desktop runs the pass on a worker thread (`Task.Run` in `SyncViewModel.cs:74`); the
      web runs it on the UI thread as async code, so saves and the pass interleave only at `await` points. Every
      in-memory store method must finish without awaiting, so each one stays atomic just as each SQLite statement is
      on the desktop. Network calls are the only awaits inside a pass.
    - Deferred changes to the open character are applied exactly as on the desktop.
    - Status texts match the desktop.
11. **Session storage.**
    - With "Stay signed in", the refresh session goes to IndexedDB. Without it, it goes to `sessionStorage`.
    - Access tokens stay in memory only.
    - Tokens are never logged; the existing redaction applies to the browser console logger.
    - An expired or revoked session while offline keeps local editing working and shows "Signed out – sign in to resume
      sync", exactly as the desktop does.

### Hosting, deploy and updates

12. **Cloudflare Pages, free plan, on the free `*.pages.dev` address.** GitHub Pages can't set HTTP security headers,
    which the security goals need. Cloudflare Pages can (a `_headers` file), has unlimited free bandwidth and no billing
    without a card. The Resend setup in `docs/CLOUD_SYNC.md` already assumes a Cloudflare account.
    - The deploy uses Cloudflare's direct upload through `wrangler`, run with `npx` at a pinned exact version in the
      release job only. That's a deploy-time tool, not part of the app, so "no npm toolchain" in the app still holds.
    - Cloudflare's Git integration is not used, because it deploys on branch pushes.
    - The API token has only "Cloudflare Pages: Edit". Cloudflare can't narrow it to one project, but it grants no DNS,
      email or other access. It's stored as a GitHub Actions secret, used only by the release job on `main`.
13. **Release.**
    - The release workflow (`.github/workflows/release.yml`) gains a step after tests: publish `Hearthsheet.Web` and
      deploy it to the production Pages project.
    - CI (`ci.yml`) builds and tests the web project, but never deploys.
    - Deploys run only from the release workflow on `main`, never from PRs, forks or `dev`.
    - All third-party actions are pinned to a full commit SHA.
14. **Web updates.** The service worker serves the cached app offline. When a new version is deployed, the app shows
    "A new version is ready – reload" and never reloads by itself mid-session. The service worker's asset list carries
    hashes, so a partial or tampered download is rejected.
15. **Until parity is reached,** release deploys still go out (same version rule), but the web app shows a "preview"
    banner and the link isn't shared with friends. The launch gate below decides when the banner comes off.
15a. **A second free Supabase project for preview and testing** (Beau's decision).
    - Preview builds and every data-safety test use it, so unfinished code never touches real characters.
    - Same migrations, same auth settings, its own keep-alive variables.
    - The web build picks its Supabase URL and key at build time: preview until the launch gate, then production.
    - Tests never hold production credentials.
    - The desktop keeps pointing at production. A desktop developer can point at the test project through
      `appsettings.user.json`.

### Security hardening

16. **Response headers** (`_headers`):
    - a strict Content-Security-Policy:
      - `default-src 'self'`
      - `script-src 'self' 'wasm-unsafe-eval'`
      - `connect-src 'self'` plus the one Supabase project URL
      - `style-src 'self'`
      - `img-src 'self' data: blob:`
      - `object-src 'none'`
      - `base-uri 'self'`
      - `form-action 'none'`
      - `frame-ancestors 'none'`
    - `Strict-Transport-Security`
    - `X-Content-Type-Options: nosniff`
    - `Referrer-Policy: no-referrer`
    - a restrictive `Permissions-Policy`
    - `Cross-Origin-Opener-Policy: same-origin`

    No inline scripts or styles; the Blazor template's inline bits move to files. No third-party scripts, fonts, analytics
    or CDNs at runtime.
17. **Untrusted input.** Imported files and pulled rows go through the same Core pipeline as the desktop (size cap, depth
    32, schema check, migration, typed deserialization, clamping, `Normalize()`, fresh id on import). Character text is
    rendered only through Blazor's encoded output, never as raw HTML (`MarkupString` is banned in the web project).
18. **Server-side guardrails** (a new migration, `0002_…`), so that a bug, a stolen session or a stranger's sign-up
    can't fill the free database and cause an outage:
    - A check that `data` is under 1 MB per row (generous for a character; the client's 10 MB import cap is for files).
      The builder confirms against real documents before choosing the number.
    - A per-user cap on rows (live plus tombstones), for example 100, enforced in a trigger.
    - The keep-alive `anon` select grant stays; re-check that it exposes nothing.
19. **Sign-up allowlist** (Beau's decision). The Supabase project is public by nature, since its URL and publishable
    key ship in both apps.
    - A Supabase "before user created" auth hook (a Postgres function, in a new migration) decides every sign-up from
      either app. Neither app changes.
    - Table `private.signup_allowlist (email text primary key, added_at timestamptz, source text)`, in a schema not
      exposed through the API, so no client can read or write it.
    - The hook's rules:
      - Normalize the email (trim, lowercase).
      - If it's on the list: allow.
      - Else, if the list has fewer than **10** entries: add it (`source = 'auto'`) and allow.
      - Else: reject with "Sign-ups are closed. Ask Beau to add your email."
    - The count and the insert happen under a transaction-level advisory lock, so two simultaneous sign-ups can't both
      take the tenth slot.
    - The migration seeds the list with every existing `auth.users` email (`source = 'existing'`). Those count toward
      the 10.
    - The limit 10 is a constant in the function; changing it is a new migration.
    - Beau adds people manually with one `insert` in the SQL editor (documented in `CLOUD_SYNC.md`). Deleting a user
      does not free a slot; removing their row does.
    - The hook must be enabled under Authentication → Hooks. Phase 0 confirms the free plan offers "before user
      created". If it doesn't, the fallback is closing sign-ups and inviting by email, which the web app would then have
      to handle (an accept-invite / set-password page).
    - Supabase's built-in auth rate limits stay on. No CAPTCHA, so no third-party script and no desktop change.
    - Accepted risk: until 10 accounts exist, a stranger who finds the URL could take a free slot. They would get only
      their own empty space, limited by the caps in decision 18, and Beau can remove them.
20. **Supply chain.**
    - No new NuGet packages beyond Microsoft's Blazor WebAssembly packages.
    - No npm toolchain.
    - Dependabot already covers NuGet and Actions; it keeps targeting `dev`.

### Printing

21. **The sheet is a fixed letter-size page in HTML/CSS** (`@page { size: letter; margin: 0 }`, 816×1056 CSS px, which
    matches the desktop's DIP layout 1:1). Add/remove controls stay hover-only and are hidden in `@media print`. Print
    uses the browser's print dialog, so Save as PDF comes free on Mac and Windows.

### Parity scope

22. Desktop features with no natural web equivalent:

    | Desktop | Web |
    |---|---|
    | Open logs | **Download diagnostics**: the in-memory log of this session, already redacted, as a text file |
    | Open data folder | Dropped; **Export all characters** covers getting your data out |
    | Database backups and their mention in error messages | Dropped; error messages point to Export all and the cloud copy instead |
    | Intro video (`Assets/tigrisoft-intro-short.mp4`) | Kept, played once per browser and skippable; muted autoplay rules apply |
    | Ctrl+P, zoom shortcuts | Ctrl/Cmd+P opens the print view of the sheet; zoom is the browser's own |
    | Update dialog and installer | Replaced by the service-worker "new version ready" prompt (decision 14) |

    Everything else (views, dialogs, commands, keyboard shortcuts) is on the parity checklist.

## Audits and launch gate

Audits run once the web app reaches parity, and again for any release that touches auth, sync, storage, headers or the
deploy pipeline. Findings go into a tracked register (Security Audit Loop) and a rescan confirms each fix.

| Discipline | What is checked | Tooling |
|---|---|---|
| Server config | The live Supabase project is on the free plan with no billing or paid add-ons. RLS is on with the policies in `0001`. Auth settings match `CLOUD_SYNC.md`. Supabase's security and performance advisors are clean. | `supabase` plugin (needs `/mcp` authorization) |
| Access control | A signed-in user can't read, write or tombstone another user's rows by any REST call. Repeat the RLS SQL check in `CLOUD_SYNC.md`, plus live attempts with two test accounts. | manual + Security Audit Loop |
| App security | OWASP Top 10, secrets in repo and build output, CSP and headers in place on the deployed site, no `MarkupString`, token handling, import fuzzing (reuse the desktop's invalid-file test cases). | Security Audit Loop, `/security-review`, `/code-review` |
| Supply chain and CI | Pinned actions, least-privilege workflow permissions, Cloudflare token scope, no deploy from untrusted refs. | Security Audit Loop |
| Data safety | Offline edit → reconnect → sync on two devices (desktop + web); conflict copies; edit beats delete; two tabs; storage eviction; a failed IndexedDB write; an update while offline edits are pending. The manual test table in `CLOUD_SYNC.md` is extended with web rows. | manual + browser automation |
| Performance | First load, repeat (cached) load, responsiveness while typing in the sheet. Target: repeat load under 2 s on a mid-range laptop. First load is reported, and the download size is reduced with trimming (within decision 5), Brotli and lazy loading where it helps. | Perf Profiler (Lighthouse) |
| Accessibility | Every input labelled, full keyboard use, visible focus, no colour-only state, screen reader pass on the main flows. | `impeccable` audit + Lighthouse |
| Cost | Supabase usage (database size, monthly active users, egress) and Cloudflare usage far below free limits; no payment method on any account. | supabase plugin + dashboards |

**Launch gate:** parity checklist complete (every desktop view, dialog and command has a web equivalent, each checked
off), no open critical or high findings, data-safety scenarios all pass, Beau signs off. Then the preview banner comes
off and the link goes to friends.

## Phases (order, not size)

0. **Checks before building.**
   - Authorize the `supabase` plugin and run the server-config row of the audit table.
   - Confirm the free plan offers the "before user created" auth hook.
   - Create the second (test) Supabase project.
   - Do a throwaway spike: Blazor WASM app referencing Core that loads a character, edits it, and shows change tracking
     firing in a published (trimmed) build.
1. **Shared foundations.**
   - Presentation and Supabase-client extraction (decisions 2 and 3).
   - Async `Relink`.
   - The desktop keeps working, with all existing tests passing.
2. **Web shell.**
   - IndexedDB store, single-tab lock, sign-in and account dialog, sync, character list, new/delete/import/export,
     offline PWA, headers.
   - Deploy wired into the release workflow behind the preview banner.
   - Migration `0002`: size and row caps (decision 18) and the sign-up allowlist hook (decision 19).
3. **Play view** (most used at the table).
4. **Details view** and its dialogs.
5. **Sheet view** and printing.
6. **Audits and fixes**, then the launch gate.

Each phase is its own PR into `dev`; releases follow the existing `dev` → `main` flow.

## Left to the builder

- Project names and the exact split of `MainViewModel`.
- The IndexedDB layout and the JS interop surface.
- Sync interval on the web (the desktop's is fine).
- CSS structure and how the Sheet's XAML layout is translated.
- Which tests are shared, and how web-specific code is tested. Core and Presentation logic stays in xUnit. Browser
  behaviour is checked with Playwright or the built-in browser.
- Exact numbers for the row-size and per-user caps, after measuring real documents.

## Open questions for Beau

None. The builder check's questions were settled as follows:
- Accounts on a shared browser: decision 7a.
- Sign-up protection: decision 19.
- Preview data kept apart: decision 15a.
- Deploy tooling: decision 12.
- Parity scope: decision 22.

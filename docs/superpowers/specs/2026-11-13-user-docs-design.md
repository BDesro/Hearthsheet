# Player documentation and in-app help

## Intent

Hearthsheet has developer docs (`docs/ARCHITECTURE.md`, `CLOUD_SYNC.md`, `WEB.md`) and an IP notice written for developers (`NOTICE.md`), but nothing for players. This adds player-facing docs that are read in two places, **inside the app** (desktop and web) and **on GitHub**, from one set of Markdown files. A new player should be offered help at the moment they need it, which is when they have no characters yet or are creating one.

## Goals

- A **tutorial**: using Hearthsheet, your first character step by step, with screenshots.
- A **beginner's guide**: new to D&D 5e, what the numbers on the sheet mean, and where to look things up. It recommends the D&D 5e wikidot (<https://dnd5e.wikidot.com>) as a companion for rules, spell and feature text, since Hearthsheet ships no rules text.
- A **legal notice** for players: not affiliated with or endorsed by Wizards of the Coast, the D&D 5e wikidot, D&D Wiki, Critical Role, Ishimori Productions or Toei; trademark line; SRD 5.1 CC-BY-4.0 attribution.
- One source of truth: the same Markdown files show on GitHub and in both apps, and match the installed version. Help works offline.

## Non-goals

- A guided tour or coach marks over the live UI (possible later; not now).
- An FAQ or troubleshooting page until real questions exist.
- Any wikidot integration beyond a plain link (no embedding, scraping or per-spell deep links). Much wikidot content is unlicensed rulebook text; Hearthsheet links to it, never reproduces it.
- Full Markdown support (tables, raw HTML, code blocks, nested lists).

## Anti-goals

- A first-launch modal that blocks the app or nags on every launch.
- Docs that drift from the app, or from each other between GitHub and in-app.
- Anything that implies a partnership with WotC or the wikidot.
- Copying rules text into the beginner's guide. It explains the sheet in original wording and points to the wikidot or the SRD for the rules themselves.

## Constraints

- No new packages (CLAUDE.md). The Markdown rendering is hand-written.
- Presentation stays UI-neutral; parsing lives there, drawing lives in App and Web.
- Every new WPF input gets an `AutomationProperties.Name`.
- Bundled content follows `NOTICE.md`: names and mechanics only, no rules text.

## Approaches considered

- **A. Bundle the Markdown and render it in-app (chosen).** `docs/user/*.md` plus their images are embedded in Presentation; a small parser turns them into blocks; a WPF Help window and a Blazor modal draw the blocks. One source, offline, version-matched. Costs about 100 lines and limits the docs to a simple Markdown subset.
- **B. Help menu opens GitHub in the browser.** Almost no code, but needs internet, leaves the app, and shows `main`, so docs can be ahead of an older install.
- Hand-written XAML/Razor help pages were ruled out: two copies that drift.

## Design

### Files

```
docs/user/
  tutorial.md            # Your first character
  beginners-guide.md     # New to D&D 5e
  legal.md               # Legal and credits
  images/                # tutorial screenshots (PNG)
```

`README.md` gets a short "Using Hearthsheet" section linking the three. `NOTICE.md` stays as the developer-facing detail and gains the wikidot non-affiliation line; `legal.md` links to it on GitHub.

### Content outline

- **tutorial.md**: what Hearthsheet is (one character, three views); creating a character (the New character dialog, what built-in classes pre-fill); filling in the Sheet vs Details; a session in Play (damage/heal, death saves, casting and slots, resources, conditions, rests, the log); level-up; saving, autosave, export/import of `.dndchar`, printing; optional cloud sign-in; keyboard shortcuts. Screenshots of the desktop app.
- **beginners-guide.md**: abilities and modifiers, proficiency bonus, saves and skills, AC, HP and hit dice, spell slots and DC, rests, in original wording and briefly, each tied to where it shows in Hearthsheet; then "Where to look things up": the wikidot for spells, features and rules, with the note that it is a community site Hearthsheet is not affiliated with, plus the free SRD.
- **legal.md**: the non-affiliation statement, trademark line, SRD 5.1 attribution (verbatim from `NOTICE.md`), the third-party class credits, and a note that the app contains no rules text.

### Supported Markdown subset

`#`/`##`/`###` headings, paragraphs, `-` bullet lists (one level), `**bold**`, `[text](url)` links, and `![alt](images/x.png)` images on their own line. Links to another doc (`beginners-guide.md`) navigate inside the help viewer; `http(s)` links open in the system browser. Anything else renders as plain text.

### Code

- **Presentation**: `HelpDocs` loads embedded `docs/user/*.md` and images (linked into the csproj as `EmbeddedResource`, not copied), and parses a doc into a flat list of blocks (heading level, paragraph of inline runs, bullet item, image). Unit tests cover the parser.
- **App (WPF)**: a `HelpWindow` (non-modal, one instance, reused) that draws blocks with `TextBlock`/`Hyperlink`/`Image` in a `ScrollViewer`, with a back button for in-doc navigation. Help menu gains: **Tutorial**, **Beginner's guide to 5e**, **Look up rules (D&D 5e wikidot)** (opens browser), separator, **Legal and credits**, above the existing items.
- **Web (Blazor)**: the same blocks drawn in the existing `Modal`; images as `data:` URIs from the embedded bytes. A **Help** entry with the same items goes in the shell's menu, and **Legal and credits** is also reachable from the sign-in screen.

### Entry points for new players

- **Empty character list** (desktop and web): under "No characters yet…", a link **New to Hearthsheet? Read the tutorial**. It disappears once a character exists, so there is no first-run flag to store.
- **New character dialog** (desktop and web): a **Need help?** link next to the hint text, opening the tutorial at its "Creating a character" section (the viewer can scroll to a heading).

## Settled after review

- **Sharing**: docs and images are `EmbeddedResource`s of Presentation (linked from `docs/user/`), read with `GetManifestResourceStream` by both apps. Manifest resources survive WASM trimming; no wwwroot copy. Screenshots are PNGs at most about 1000 px wide to keep the web download small. *(my call)*
- **Menus**: the desktop Help menu already exists (updates, log folder, data folder); the web shell's existing `<details>` menu gets the help items. *(from the repo)*
- **Credits list** comes from `NOTICE.md`: SRD 5.1 (WotC, CC-BY-4.0), Kamen Rider (D&D Wiki; Ishimori Productions; Toei), Blood Hunter (Critical Role), plus the new wikidot line. Wording is drafted by the builder and reviewed by Beau in the PR. *(my call)*
- **Section links** use GitHub's heading slugs (lowercase, spaces to `-`, punctuation dropped), so `tutorial.md#creating-a-character` works on GitHub and in-app. *(my call)*
- **Shipping**: a feature branch and a PR into `dev`. No version bump or release here. *(CLAUDE.md)*
- **Screenshots** are captured by Claude, running the desktop app against a scratch `Application:DataDirectory` with `Sync:Url` blanked so the real account is never touched, using a sample character created for the purpose. *(Beau's call)*
- **Web**: the empty-list link text matches the desktop. Legal and credits on the sign-in screen needs no account. *(my call)*

## Left to the builder

- Exact wording and length of each doc, within the outline.
- Which screenshots to take and how they're captured (scratch data folder, sync URL blanked).
- Viewer styling, window size, and whether the web modal is full-screen on phones.
- Anchor syntax for "open at a section" (heading text slug is fine).

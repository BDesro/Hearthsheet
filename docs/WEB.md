# Hearthsheet on the web

The web version is `src/Hearthsheet.Web`, a Blazor WebAssembly app that installs as a Progressive Web App (PWA). It is
static files only: Cloudflare Pages hosts them on the free `hearthsheet.pages.dev` address, and the browser talks to
Supabase directly. Until the launch gate it shows a "Preview" banner and uses the test Supabase project
(`Hearthsheet-Test`), so don't share the link yet.

## How it is built and deployed

1. `dotnet publish src/Hearthsheet.Web -c Release -o <folder>` builds the site into `<folder>/wwwroot`. CI does this on
   pushes to `main` and `dev` and on pull requests (`.github/workflows/ci.yml`), so a trimming problem fails the build. CI never deploys.
2. Merging `dev` into `main` runs the Release workflow (`.github/workflows/release.yml`). When the version in
   `Directory.Build.props` is new, the `release` job tests, packages and publishes the desktop release. Then the
   `deploy-web` job publishes the web app and uploads it with Cloudflare's `wrangler` tool (pinned to 4.150.0):
   `wrangler pages deploy web/wwwroot --project-name=hearthsheet --branch=main`.
3. A merge that doesn't bump the version deploys nothing, and pull requests, forks and `dev` never deploy. Cloudflare's
   own Git integration is not used, because it would deploy every branch push.
4. In the browser, the service worker keeps a copy of the app for offline use. Each file in its list carries a SHA-256
   hash, so a partial or tampered download is rejected. After a deploy, open copies of the app download the new version
   in the background (checked at load and every hour) and show "A new version is ready – reload". The page never
   reloads by itself. The reload button saves the open character and waits until everything is stored in the browser.
   If that fails ("Not saved on this device"), it doesn't reload and says so.

## One-time Cloudflare setup (Beau)

You need a free Cloudflare account (the Resend setup in `CLOUD_SYNC.md` already uses one). No card is needed.

1. **Create the Pages project.** In the Cloudflare dashboard, open **Workers & Pages** → **Create** → **Pages** →
   **Upload assets** (direct upload, not "Connect to Git"). Name the project exactly `hearthsheet`. Cloudflare asks for a
   first upload: upload any small folder (for example one with a single `index.html`); the first release replaces it.
   The site is then at `https://hearthsheet.pages.dev`. Then open the project's **Settings** and check that the
   **production branch** is `main`. If it's anything else, the release's `--branch=main` upload becomes a preview
   deploy instead of the live site; change it to `main`.
2. **Create the API token.** Click your profile icon → **My Profile** → **API Tokens** → **Create Token** → **Create
   Custom Token**. Name it `Hearthsheet deploy`. Under **Permissions** add one row only: **Account** → **Cloudflare
   Pages** → **Edit**. Under **Account Resources** choose your account. Leave everything else empty, then **Continue to
   summary** → **Create Token**, and copy the token (it is shown only once). It can't be narrowed to one project, but it
   gives no DNS, email or other access.
3. **Find your account ID.** On the dashboard home page, open the account menu (⋯ next to the account name) →
   **Copy account ID**. It's also on the right of any Workers & Pages overview page.
4. **Store both in GitHub.** In the repository, open **Settings** → **Secrets and variables** → **Actions**.
   - **Secrets** tab → **New repository secret**: name `CLOUDFLARE_API_TOKEN`, value the token from step 2.
   - **Variables** tab → **New repository variable**: name `CLOUDFLARE_ACCOUNT_ID`, value the ID from step 3.
5. **Check it.** The next release (a `dev` → `main` merge with a new version) runs **deploy-web**. A green run means
   the site is live. Open `https://hearthsheet.pages.dev` and check that the Preview banner shows.
6. **If the token leaks or expires:** create a new one (step 2), replace the secret (step 4), then delete the old token
   under **API Tokens**.

## Switching from preview to production (at the launch gate)

Do these together in one pull request, never one without the other. The strict Content-Security-Policy only lets the app
talk to the Supabase URL in `_headers`, and `WebConfigTests` fails the build if the two disagree.

1. In `src/Hearthsheet.Web/wwwroot/appsettings.json`, set `Sync:Url` and `Sync:AnonKey` to the production project's
   values (the same as `src/Hearthsheet.App/appsettings.json`).
2. In `src/Hearthsheet.Web/wwwroot/_headers`, change the URL after `connect-src 'self'` to the same production URL.
3. Run `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~WebConfigTests"`. It must pass.
4. Then set `"Preview": false` in `appsettings.json`. The banner goes away.
5. Bump the version and release as usual. Testing keeps using `Hearthsheet-Test` (see `CLOUD_SYNC.md`).

## Testing two versions locally

To check offline use and the update banner without deploying:

1. `dotnet publish src/Hearthsheet.Web -c Release -o artifacts/web-a`, then change any visible text and publish again
   to `artifacts/web-b`. Undo the text change.
2. `npx --yes wrangler@4.150.0 pages dev artifacts/web-a/wwwroot`, open the printed address in Chrome or Edge, and reload
   once so the service worker controls the page.
3. Stop wrangler and reload: the app still loads (offline).
4. Start wrangler on `artifacts/web-b/wwwroot` and reload, or wait for the hourly check: the banner appears.

The manual test script in `CLOUD_SYNC.md` has the web rows (12–17).

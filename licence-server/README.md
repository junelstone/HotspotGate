# Licence server on Supabase

The licence server (trial registration, activation, daily check-in, renewals, revocations, update information) runs
as one **Supabase Edge Function** with a **Postgres** database. It speaks exactly the protocol of the C# server in
`src/StonesHotspot.LicenseServer` (checked by `SupabaseLicenceServerTests`), so hotspots and LicenseManager need no
change: only the address.

| Piece | Where |
|---|---|
| Tables `licences`, `trials`, `checkins`, `update_manifest` + 2 SQL functions | `supabase/migrations/` |
| Edge Function `licence` (Deno) | `supabase/functions/licence/index.ts` → logic in `supabase/functions/_shared/` |
| Installer downloads | GitHub Releases of the **public** repository `junelstone/stoneshotspot-releases` (the source repository is private), made by `.github/workflows/release.yml` |

Address after deployment (no domain to buy): **`https://<project-ref>.supabase.co/functions/v1/licence/`**

**This installation:** project ref `tfsnklurcjezjwgwdxkv` → `https://tfsnklurcjezjwgwdxkv.supabase.co/functions/v1/licence/` (already set in
`src/StonesHotspot.Service/appsettings.json`).

## 1. The owner's keys (once, on the owner's Windows PC — never in the cloud, never in Git)

1. Build and start LicenseManager: `dotnet run --project tools\StonesHotspot.LicenseManager` (or install
   `LicenseManager-Setup.exe` built with `.\build.ps1 -Installer -AllowDevelopmentKeys`).
2. First start: choose a password (≥ 12 characters, not used anywhere else). The keys are created and LicenseManager
   insists on a **backup copy of the key file**: put it on a USB stick kept somewhere else than the PC.
3. **Clés et sauvegarde** tab:
   - the **licence public key** and **server public key**: public, they go into the program
     (`src/StonesHotspot.Licensing/Keys/licence-public.txt` and `server-public.txt`, whole file replaced, no
     "DEVELOPMENT" line) — commit them, or send them to the developer;
   - the **server private key**: goes only into Supabase (step 3 below). It cannot create licences, but keep it
     secret: it signs the server's answers.

The licence private key never leaves LicenseManager.

## 2. Supabase project

1. Create an account at <https://supabase.com> and a **new project**: name `stoneshotspot-licences`, a strong database
   password (keep it), region **Paris (eu-west-3)** or Frankfurt (closest to Brazzaville).
2. Note the **project ref** (Settings → General, e.g. `abcdefghijklmnop`).
3. Install the Supabase CLI (<https://supabase.com/docs/guides/cli>), then from this folder:

```powershell
cd licence-server
supabase login
supabase link --project-ref <project-ref>
supabase db push                                   # creates the tables
supabase secrets set SERVER_PRIVATE_KEY=<server private key from LicenseManager>
supabase secrets set LICENCE_PUBLIC_KEY=<licence public key>
supabase secrets set MANAGER_TOKEN=<long random text, e.g. 40 characters>
supabase functions deploy licence --no-verify-jwt
```

4. Check: open `https://<project-ref>.supabase.co/functions/v1/licence/health` → `{"status":"ok"}`.

Supabase's own keys: the function gets the server key from Supabase automatically (legacy service-role key or the new
`sb_secret_…` keys). Only if the log says no key was found: `supabase secrets set SB_SECRET_KEY=<a secret key>`. The
**secret key** (`sb_secret_…`) bypasses every database protection: never in Git, in the hotspot, in a chat or an
e-mail; if it was shown somewhere, create a new one (Project Settings → API Keys) and delete the old. The
**publishable key** (`sb_publishable_…`) is not used by Stone's Hotspot.

## 3. Point the hotspots and LicenseManager at it

- Hotspot: `src/StonesHotspot.Service/appsettings.json` → `"Licensing": { "ServerUrl":
  "https://<project-ref>.supabase.co/functions/v1/licence/" }` (done for `tfsnklurcjezjwgwdxkv`), then build the installer. Existing installations get
  it with the next update (the installer replaces `appsettings.json`), or by editing it by hand and restarting the
  service.
- LicenseManager → **Serveur**: the same address and the `MANAGER_TOKEN`. **Synchroniser** uploads licences,
  renewals and revocations.

## 4. Downloads repository (once)

The source repository `junelstone/HotspotGate` is **private** (GitHub → Settings → General → Danger Zone →
Change visibility). Release files of a private repository need a GitHub login, so installers go to a small public one:

1. GitHub → **New repository**: `stoneshotspot-releases`, **Public**, "Add a README". Nothing else goes in it.
2. GitHub → Settings (your profile) → Developer settings → **Fine-grained tokens** → Generate: repository access
   *Only* `stoneshotspot-releases`, permission **Contents: Read and write**, expiry 1 year (note the date).
3. `HotspotGate` → Settings → Secrets and variables → Actions → **New repository secret** `RELEASES_TOKEN` = the
   token. (Other name for the downloads repository: add the variable `RELEASES_REPO` = `owner/name`.)

## 5. Daily use for the owner

- **Supabase → Table Editor**: `licences` (with revocations), `trials` (first start of every PC), `checkins` (every
  contact: date, Machine ID, result, IP, version). Two PCs checking in with one licence show up as two Machine IDs on
  the same `licence_id`.
- New version: push a tag `v1.2.3` (same as `<Version>` in `Directory.Build.props`). The **Release** workflow builds
  `StonesHotspot-Setup-1.2.3.exe`, attaches it to a release of `stoneshotspot-releases` and writes its address and SHA-256 in the release
  notes. Copy them into the single row of `update_manifest` (`version`, `url`, `sha256`, `notes`): within a day every
  hotspot shows the update. Empty `version` = no update offered.

## Costs and limits (free plan)

- Free: 500 MB database (years of check-ins for hundreds of hotspots), 500 000 function calls a month (a hotspot
  makes about 30).
- A free project is **paused after a week without activity**. Once hotspots check in daily this does not happen;
  before that, open the dashboard now and then. While paused, hotspots simply keep working offline and retry every
  hour. With paying customers, the Pro plan (about 25 USD a month) removes the pause and adds daily backups.
- Own domain (e.g. `licences.stonesit.cg`): Supabase "custom domain" add-on (paid) — not needed. If the address ever
  changes, ship an update with the new `ServerUrl` before switching the old one off.

## Tests

`dotnet test --filter SupabaseLicenceServerTests` runs the function's logic in Node 22 (in memory) with keys and
codes made by the C# tools, and checks every answer with the hotspot's own code. Type-check:
`npx -p typescript tsc -p licence-server` (with `@types/node`).

The C# server (`src/StonesHotspot.LicenseServer`) stays available for hosting on a VPS instead.

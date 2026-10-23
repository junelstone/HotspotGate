# Stone's Hotspot

Windows Wi-Fi hotspot manager (captive portal) by **Stone's IT Consulting**: turns a Windows 10/11 PC into a
managed, paid Wi-Fi hotspot — vouchers, user accounts, tariffs, Mobile Money, admin dashboard.

> **Status: Phase 11 — installer and updates.** `StonesHotspot-Setup.exe` installs the Windows service (restarted
> automatically after a crash), the tray app and the firewall rules; a first-run wizard sets up language, business,
> admin password, network and prices; daily backups; signed updates from the licence server. The owner's
> `LicenseManager-Setup.exe` is a separate installer, and the build refuses to put owner files or development keys in
> the customer installer. See [Installing](#installing-on-the-hotspot-pc) and [Build phases](#build-phases).

## Requirements

| For | You need |
|---|---|
| Building | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.100 or newer 8.0.x), PowerShell 7+ or Windows PowerShell 5.1 |
| Running the hotspot | Windows 10/11 x64, Administrator rights, two network connections (WAN + LAN) — details arrive in phase 2 |

## Build and test

```powershell
.\build.ps1                    # restore, build (Release), run all tests
.\build.ps1 -Configuration Debug
.\build.ps1 -SkipTests
.\build.ps1 -Publish           # also publish Service + Tray (win-x64, self-contained) to .\artifacts\publish
.\build.ps1 -Installer         # publish + release check + both installers (Windows, Inno Setup 6) → installer\Output
.\build.ps1 -Installer -AllowDevelopmentKeys   # TEST installer with the development keys — never for a customer
```

`-Installer` needs [Inno Setup 6](https://jrsoftware.org/isdl.php) and prints the SHA-256 of each installer (needed
to publish an update). Before it runs Inno Setup, `tools\Test-ReleaseContent.ps1` checks the customer files and
**fails the build** if they contain the LicenseManager/LicenseIssuer/LicenseServer, key files, `appsettings.Local.json`,
a private key, or the development public keys. A real release therefore needs the owner's keys first
([`docs/licensing.md`](docs/licensing.md) → "Before the first release").

Test results are written to `artifacts\test-results\tests.trx`. GitHub Actions runs the same script on
`windows-latest` for every push (`.github/workflows/build.yml`) and uploads the published apps.

## Run from source (development)

```powershell
dotnet run --project src\StonesHotspot.Service --launch-profile StonesHotspot.Service
```

Then open <http://localhost:8080/admin> (add `?lang=en` for English) and <http://localhost:8080/health>.
In Development the data folder is `src\StonesHotspot.Service\data\` instead of `%ProgramData%\StonesHotspot`,
the service listens on **localhost only** and the portal uses port **8081**: fine for working on pages, but phones
cannot reach it. A desktop browser on the PC is treated as a test device (MAC `02:00:00:00:00:01`), so the
portal can be tried at <http://localhost:8081/portal> with a code from <http://localhost:8080/admin/gate>. For tests with real devices run the published service
(`.\build.ps1 -Publish`, then `artifacts\publish\StonesHotspot.Service\StonesHotspot.Service.exe` as administrator),
which listens on ports 80 and 8080 on all addresses.

Tray app (Windows only): `dotnet run --project src\StonesHotspot.Tray`. It shows the state of the
`StonesHotspot` Windows service, opens the dashboard, and starts/stops the service (needs admin rights).

## Installing on the hotspot PC

Run `StonesHotspot-Setup-x.y.z.exe` (administrator; nothing else to install, .NET is included):

- Program files in `C:\Program Files\Stone's Hotspot`, data in `%ProgramData%\StonesHotspot` (kept on upgrade).
- Windows service **StonesHotspot** (automatic, delayed start), restarted by Windows 5 s / 10 s / 30 s after a crash.
- Tray icon for every user at sign-in; desktop shortcut **Stone's Hotspot** (opens the dashboard).
- Firewall: the service opens TCP 80, 443, 8080 and UDP 53 **on the customer (LAN) adapter only**, group
  "Stone's Hotspot", as soon as the network is set up.
- At the end the dashboard opens on the **first-run wizard** (`/setup`, only on the PC itself): language, business
  and Wi-Fi name, administrator password, internet and customer connections, prices of the first plans.

**Upgrade:** run the newer installer (or tray → **Installer la mise à jour**): the service is stopped, files replaced,
service started; data, settings and licence stay. **Uninstall** (Settings → Apps): stops and removes the service
(internet sharing off), removes the firewall rules and the tray start-up, then asks whether to delete the data
(default **No**). The trial/licence records are always kept. Manual checklist:
[`phase-11-installer.md`](docs/manual-tests/phase-11-installer.md).

## Configuration

| File | Committed? | Purpose |
|---|---|---|
| `src/StonesHotspot.Service/appsettings.json` | yes | Defaults: `StonesHotspot:AdminPort` (8080), `PortalPort` (80), `HttpsPort` (443, 0 = off), `LocalhostOnly`, `DataDirectory` (empty = `%ProgramData%\StonesHotspot`), `SetupWizard` (true), `Licensing:ServerUrl` (licence server, also used for updates), log levels |
| `appsettings.Local.json` | **no** (git-ignored) | Secrets (SMS, Mobile Money keys). Copy `appsettings.Local.example.json`. Read from the exe folder and from `%ProgramData%\StonesHotspot\` |

Never put API keys or passwords in `appsettings.json`.

Runtime data (`%ProgramData%\StonesHotspot\`):

| Path | Content |
|---|---|
| `logs\service-YYYYMMDD.log` | Service log, one file per day, 30 days kept |
| `backups\` | Database backups: automatic every day (`…-auto.db`, the 14 latest kept) and manual ones (never deleted automatically) |
| `https.pfx`, `https.key` | This PC's own self-signed HTTPS certificate (made on first start) and its password, DPAPI-protected |
| `stoneshotspot.db` | SQLite database (WAL mode, so also `-wal`/`-shm` files while running) |
| `initial-admin-password.txt` | Created once, on first start: the random password of the `admin` account. Readable by Administrators only. Delete it after the first login |

## First start

On a new installation the dashboard opens the **first-run wizard** at <http://localhost:8080/setup> (from another
device it only says to finish the setup on the PC). Its third step sets the password of the **`admin`** account.
Until then that account has a random password in `%ProgramData%\StonesHotspot\initial-admin-password.txt`
(Administrators only), deleted once a password is chosen. There is deliberately no default password: customers on
the hotspot can reach the dashboard port. A database from before the wizard existed is treated as set up.

**Lost admin password**, no other admin: from a command prompt opened *as administrator* on the PC,
`"C:\Program Files\Stone's Hotspot\StonesHotspot.Service.exe" --reset-admin-password` writes a new temporary
password to `initial-admin-password.txt` (to be changed at the next sign-in).

## Dashboard

`http://localhost:8080/admin` on the PC, `http://192.168.137.1:8080/admin` from a phone on the hotspot Wi-Fi.

| Role | Can use |
|---|---|
| **Caissier / Cashier** | Home, **Caisse** (till: products and Wi-Fi codes in one cart, change to give, receipt), **Vendre** (new codes printed on a ticket, or a pre-printed card), **Ma caisse** (open with starting cash, sales, close with counted cash), **Comptes** (search, top up) |
| **Administrateur** | Everything: **En direct** (customers online: add time, log out, block, whitelist), **Forfaits**, **Filtrage web**, **Débit**, **Tickets** (batches, A4 cards with QR, CSV, disable), **Produits** (groups, prices with VAT, stock deliveries and counts), **Rapports**, **Accès gratuit** (free access, password of the day, SMS login), **Mobile Money** (operator keys, plans sold online, payments), **Comptes**, **Personnel**, **Paramètres** (name, Wi-Fi name, logo, colour, welcome page, terms, language, idle timeout, receipt header/footer), **Journal** (audit log), **Sauvegarde**, network and diagnostics pages |

- Sign-in ends after 2 hours without activity; 5 wrong passwords from one address → 5 minutes wait. New staff
  and reset passwords are temporary: they must be changed at first sign-in.
- Shifts, the till, products, receipts, the Pro reports and extra staff accounts are Pro features (included in the
  trial). Basic keeps **Vendre**, sales by plan and the sessions report.
- Printing is always on **A4**, through the browser's print dialog (Ctrl+P on the PC, Share → Print on a phone) or
  a downloaded PDF. Code cards print 3 per row with a QR code, to cut out.
- Backups: automatic copy every day (14 kept); **Sauvegarde** makes or downloads a copy; restoring keeps a copy of
  the current database first.
- HTTPS: `https://192.168.137.1/admin` uses the PC's own self-signed certificate (the browser warns once).
- Updates: when the licence server publishes a newer version (signed), admins see a banner and the tray offers
  **Installer la mise à jour** (download, SHA-256 check against the signed information, then the installer).
  Copy backups to a USB stick regularly (automatic daily backups come in phase 11).

## Network setup

The PC needs two connections: **WAN** (the internet) and **LAN** (a second card cabled to a Wi-Fi access point in
access point/bridge mode, or the PC's own Wi-Fi in Windows *Mobile hotspot* mode).

1. Allow the portal and dashboard ports from the customer network (the installer will do this later):
   `New-NetFirewallRule -DisplayName "Stone's Hotspot portal + dashboard" -Direction Inbound -Protocol TCP -LocalPort 80,8080 -Action Allow`
2. Make sure nothing else listens on port 80 (IIS, Skype, another web server).
3. Start the service with administrator rights (the Windows service always has them).
4. On the PC, open <http://localhost:8080/admin/network>, choose WAN and LAN, click **Partager Internet**.
   The packet gate starts first, then Windows Internet Connection Sharing gives the LAN card **192.168.137.1**
   and hands out 192.168.137.x addresses.
5. Connected devices: <http://localhost:8080/admin/devices>. Gate diagnostics (authorise/revoke a device by hand,
   counters, blocked/free-access domains, test codes): <http://localhost:8080/admin/gate>.

The setup pages need an administrator sign-in. Customers see the portal at
`http://192.168.137.1/portal`. Every 5 s the service makes the network match the saved setup (it switches
sharing back on if Windows dropped it).

### Customer portal

`http://192.168.137.1/portal`: code login (default) and account login, status page with live countdown, data left,
speed and **Se déconnecter**, terms of use, "site blocked" page — French/English, ~5 KB per page, nothing loaded from
the internet. After login the customer goes to the **welcome page** (setting `Portal.WelcomeUrl`) if set, otherwise
to the page they first asked for. Business name, accent colour (`Portal.AccentColor`) and logo
(`%ProgramData%\StonesHotspot\branding\logo.png`) and the terms of use are set in **Paramètres**.

### Speed limits and web filter

- **Débit** (admin): plan limits overview, **WAN speed test** (download/upload from speed.cloudflare.com; URLs in
  `appsettings.json` → `Bandwidth`), **fair share** (the line's total split between active devices, with a
  minimum and maximum per device; the lower of plan and share applies).
- Limits are enforced by **shaping** in the gate: packets over the rate are delayed (up to 400 ms) then
  re-injected, beyond that dropped — so a phone's speed test shows the plan's speed. Editing a plan changes running
  sessions within a second.
- **Filtrage web** (admin): blocked domains/IPs, ready-made lists *Réseaux sociaux* (without WhatsApp) and
  *Contenus adultes*, **family DNS** (Cloudflare for Families 1.1.1.3, blocks adult and malware sites),
  free-access sites (walled garden), MAC whitelist and blacklist. Changes apply immediately.

### Till, receipts and reports

- **Produits** (admin): groups (Boissons, Snacks, Services…), price including VAT, VAT rate, stock. "Track stock"
  off for services (printing, photocopies). **Livraison** adds stock, **Inventaire** sets the counted stock; both go
  to the audit log. A product already sold cannot be deleted (untick "En vente").
- **Caisse**: tap Wi-Fi plans and products, choose cash / MTN MoMo / Airtel Money, type the cash received to see the
  change, **Encaisser**. Everything is sold in one database transaction with one receipt number: if a product is
  short, nothing is sold. Cashiers need an open shift (Pro).
- **Receipt** page (A4): browser print or **PDF** (also handy to send by WhatsApp). Wi-Fi codes on the receipt are
  cards with a QR code that opens the portal with the code filled in. Header (address, phone, NIU) and footer texts
  are set in **Paramètres**.
- **Ma caisse**: A4 PDF and browser print of the shift report (totals computed from the listed transactions).
- **Rapports** (admin, dates from/to, CSV for Excel and PDF): sales by plan and sessions with peak hours (Basic);
  shifts and sales per staff member, transactions with filters, turnover by day or month (Pro). Days are local time.
  Account charges are left out of turnover (the money was counted at the top-up).
- **Journal** (admin): audit log with dates, staff and text filters, CSV.

### Free access, password of the day, SMS login (Pro)

- **Accès gratuit** (admin): each mode on/off with its own plan (create e.g. "Gratuit 15 min" in **Forfaits**) and
  logins per device per day. A login gives the device a hidden code on that plan, so time and data count as usual;
  logging out and in again does not use a new one.
- **Password of the day**: fixed, or a new 6-character password every day at midnight; shown on the dashboard home.
- **SMS code**: Congo number (`06 123 45 67`, `+242…`), allowed number starts, 6-digit code valid 10 minutes (5
  tries), at most one code per minute per number, daily limits per number, per device and in total. Sending by a
  **USB GSM modem** (COM port, SIM PIN disabled, AT commands) or an **HTTP SMS provider** (URL template with
  `{phone}`, `{phone_digits}`, `{message}`, optional POST/JSON body). The provider address is stored encrypted and
  never shown again; **test SMS** button and a log of the last 50 SMS.
- Social login (Google/Facebook) is not built: see [`docs/social-login.md`](docs/social-login.md).

### Mobile Money (Pro)

- **Mobile Money** (admin): MTN MoMo (Collection API) and Airtel Money (Collections API) — address, environment,
  currency and keys (stored encrypted, never shown again), number starts per operator (MTN `+24206`, Airtel
  `+24205, +24204`), **Tester la connexion**, plans sold online, time to approve (3 min), optional public https
  address for operator notifications, latest payments with **Vérifier**.
- Portal: **Acheter un forfait avec Mobile Money** → plan → operator → number → the customer approves with their PIN
  → code shown, device logged in, code sent by SMS. Declined / not approved in time → clear message.
- Payments are checked every few seconds (waiting page and a background job), so no public address is needed;
  operator notifications, when set up, only trigger a check with our own keys (a forged one cannot create a code).
  Each payment completes once (code + sale + login), even if notifications arrive twice. A payment approved late is
  completed by **Vérifier** or a late notification (within 24 h).
- Sales are recorded with method MTN MoMo / Airtel Money and the operator's transaction number; **Rapports →
  Mobile Money** gives per-day totals per operator and every payment, to compare with the operator's statement.
- Sandbox: MTN `https://sandbox.momodeveloper.mtn.com`, environment `sandbox`, currency EUR (the defaults);
  Airtel `https://openapiuat.airtel.africa`.

### Licence

- 30-day **trial** with every Pro feature from the first start on the PC (kept across reinstalls), then a **licence
  code** (`SHS1-…`) made by Stone's IT Consulting for this PC's **Machine ID** (`MID-XXXX-XXXX-XXXX`), Basic or Pro,
  usually one year. **Licence** page (dashboard): Machine ID with Copy / WhatsApp, paste a code or import a `.lic`.
- Warnings 30/14/7/3/1 days before the end (dashboard banner, tray balloon), then 7 days of grace (red banner), then
  the hotspot stops: customers see "Service temporairement indisponible", the dashboard keeps Licence, Reports,
  Backup and Audit log. No data is deleted or locked; a valid code works at once.
- Optional licence server (`Licensing:ServerUrl`): registers the trial, checks the licence daily, brings renewals
  automatically, applies revocations (7 days of grace). Offline, a licence works until its end date.
- Owner side: LicenseManager (password-protected, never shipped) and the licence server. Setup, key handover and the
  honest limits of the protection: [`docs/licensing.md`](docs/licensing.md).

### Packet gate (WinDivert)

`build.ps1` downloads **WinDivert 2.2.2** from its official release (SHA-256 checked, see
[`docs/third-party.md`](docs/third-party.md)) and copies `WinDivert.dll` + `WinDivert64.sys` next to the service.
How the gate, DNS and redirect work: [`docs/architecture.md`](docs/architecture.md).

**If the service stops or crashes:** a normal stop switches internet sharing off (customers lose internet, the
Wi-Fi is never left open). If WinDivert cannot start, sharing stays off and the Network page says why. After a
*crash* (process killed), sharing stays on **without filter** until the service starts again; Windows restarts it
after 5 seconds (installer setting). In Windows Mobile hotspot mode the service cannot switch the hotspot off. The
PC's own internet is never filtered.

Manual tests: [`phase-2-network.md`](docs/manual-tests/phase-2-network.md),
[`phase-3-gate.md`](docs/manual-tests/phase-3-gate.md), [`phase-4-portal.md`](docs/manual-tests/phase-4-portal.md),
[`phase-5-dashboard.md`](docs/manual-tests/phase-5-dashboard.md),
[`phase-6-bandwidth-filter.md`](docs/manual-tests/phase-6-bandwidth-filter.md),
[`phase-7-business-tools.md`](docs/manual-tests/phase-7-business-tools.md),
[`phase-8-free-sms.md`](docs/manual-tests/phase-8-free-sms.md),
[`phase-9-mobile-money.md`](docs/manual-tests/phase-9-mobile-money.md),
[`phase-10-licensing.md`](docs/manual-tests/phase-10-licensing.md),
[`phase-11-installer.md`](docs/manual-tests/phase-11-installer.md).

## Database

SQLite through EF Core; migrations are in `src/StonesHotspot.Core/Data/Migrations` and are applied
automatically when the service starts. To change the model:

```powershell
dotnet tool restore                      # once: installs dotnet-ef from .config/dotnet-tools.json
dotnet ef migrations add <Name> --project src/StonesHotspot.Core --output-dir Data/Migrations
```

Business rules are summarised in [`docs/business-rules.md`](docs/business-rules.md).

## Solution layout

| Project | Purpose |
|---|---|
| `src/StonesHotspot.Core` | Domain and business rules, no Windows dependency: EF Core model + migrations (`Data/`), rules (`Rules/`), services (`Services/`: tariffs, vouchers, sessions, accounts, cash, staff, devices, settings, audit). FR/EN resources, `Xaf` money type |
| `src/StonesHotspot.Licensing` | `ILicenseService` (the single "is this feature allowed?" gate), editions and the Basic/Pro feature table, licence codes, Machine ID, trial and clock protection, licence-server client. Public keys only |
| `src/StonesHotspot.Network` | Adapters, ICS (`HNetCfg.HNetShare` COM), neighbour/ARP table, connected-device scan; WinDivert packet gate and DNS in phase 3 |
| `src/StonesHotspot.Web` | Razor Pages: captive portal and admin dashboard (today: home, network setup, connected devices) |
| `src/StonesHotspot.Service` | Windows Service host (Kestrel, Serilog, DI) |
| `src/StonesHotspot.Tray` | Tray icon: status, Start/Stop, Open dashboard |
| `src/StonesHotspot.LicenseServer` | Licence server: trial registration, activation, daily check-in, renewals, revocations, owner page (`/admin`) |
| `tools/StonesHotspot.LicenseIssuer` | **Owner-only** library: key vault, licence book, signing, sync. Never shipped |
| `tools/StonesHotspot.LicenseManager` | **Owner-only** Windows app over the issuer library. Never shipped to customers |
| `tests/StonesHotspot.Tests` | xUnit tests |
| `installer/` | Inno Setup scripts: `StonesHotspot.iss` (customer) and `LicenseManager.iss` (**owner only**) |
| `tools/Test-ReleaseContent.ps1` | Build check: nothing of the owner (tools, keys, development keys) in the customer installer |
| `legacy/python-prototype` | The earlier Python *HotspotGate* prototype, kept for reference only |

## Languages and currency

- All user-facing text lives in `src/StonesHotspot.Core/Localization/Strings.resx` (French, default) and
  `Strings.en.resx` (English). Add every new key to both files — a test fails otherwise.
- Web pages pick the language from `?lang=fr|en`, then the browser; French otherwise.
- Amounts use `Xaf` (whole FCFA, `long`): `new Xaf(1000).ToString()` → `1 000 FCFA` (no-break space);
  `ToString(XafStyle.Ascii)` for CSV and SMS.

## Build phases

| Phase | Content | State |
|---|---|---|
| 0 | Project skeleton | done |
| 1 | Data layer and business rules | done |
| 2 | Network setup (adapters, ICS, connected devices) | done (Mode B dropped) |
| 3 | Packet gate (WinDivert) and DNS | done — to be verified on Windows |
| 4 | Captive portal | done — to be verified on Windows |
| 5 | Admin dashboard | done |
| 6 | Bandwidth control and web filter | done — speeds to be verified on Windows |
| 7 | Business tools (products, till, A4 receipts, reports) | done |
| 8 | Free access and SMS login | done — SMS to be verified with the real modem or provider |
| 9 | Mobile Money payments | done — to be verified with the operators' sandbox, then production keys |
| 10 | Licensing, trial and activation | done — owner keys and server hosting to be set up |
| 11 | Windows service, installer and updates | **done** — installers to be built and tried on a clean Windows 11 |
| 12 | Hardening and final testing | — |

Plans for each phase are in `docs/phase-N-plan.md`.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `build.ps1` says the .NET SDK is missing | Install the .NET 8 SDK, open a new terminal |
| Port 8080 already in use | Change `Kestrel:Endpoints:Admin:Url` in `appsettings.json` (or `appsettings.Local.json`) |
| Service won't start | Read `%ProgramData%\StonesHotspot\logs\service-*.log` and the Windows Event Viewer (Application log) |
| Network page: "not running with administrator rights" | Run the service as the Windows service, or start it from a PowerShell opened with "Run as administrator" |
| Network page: "IP address conflict … 192.168.137.x" | Your internet box/modem uses the same network as Windows sharing. Change the box's LAN network (e.g. to 192.168.1.x) |
| Phones get 192.168.0.x / 192.168.1.x instead of 192.168.137.x | The access point is in router mode or its DHCP is on: set it to access point/bridge mode |
| Gate page: "WinDivert driver files … are missing" | Run `.\build.ps1` (it downloads WinDivert), or copy `WinDivert.dll` and `WinDivert64.sys` next to `StonesHotspot.Service.exe` |
| Gate page: "driver was blocked" / "signature" | Antivirus or Secure Boot policy blocked WinDivert: allow it in the antivirus, then restart the service |
| Service log: "address already in use" for port 80 | Another program uses port 80 (IIS, Skype…): stop it, or set `StonesHotspot:PortalPort` |
| Portal says "Votre appareil n'a pas pu être identifié" | The PC has not seen the phone in its ARP table: turn the phone's Wi-Fi off and on. If it persists, check the LAN adapter choice on the Network page |
| Phones never show "Sign in to network" | Check the "Stone's Hotspot" firewall rules (port 80) and the gate state on <http://localhost:8080/admin/gate> |
| "database is locked" in the log | Another program (e.g. DB Browser for SQLite) has the database open for writing; close it |
| Lost the admin password | Another admin resets it in **Personnel**. If no admin can sign in: `StonesHotspot.Service.exe --reset-admin-password` as administrator on the PC (see [First start](#first-start)) |
| Build: "Release check FAILED" | The customer installer would contain owner files or keys (listed). Development keys: create the owner's keys ([`docs/licensing.md`](docs/licensing.md)), or `-AllowDevelopmentKeys` for a test build only |
| Phones can't open the dashboard or portal although the gate runs | Windows Firewall: check the "Stone's Hotspot" rules exist for the LAN adapter (`Get-NetFirewallRule -Group "Stone's Hotspot"`); re-apply the network setup to recreate them |
| No update banner although a version is published | `Licensing:ServerUrl` must be set; the check runs 2 minutes after start, then daily. The service log says if the update information was not signed with the server key |
| Speed test in **Débit** fails | The PC has no internet, or speed.cloudflare.com is blocked: set other URLs in `appsettings.json` → `Bandwidth` |
| Customers slower than their plan | Check **Débit**: fair share may give them less (many active devices or totals set too low). The gate page shows delayed/dropped packets |
| A blocked site still opens | The phone cached the address: switch its Wi-Fi off and on. Apps using their own encrypted DNS (DoH) can bypass domain blocking; add the site's IP range as well |
| Dashboard says "Accès refusé" | The page is for administrators; sign in with an admin account |
| Tray "Start/Stop" shows an administrator message | Starting or stopping a Windows service needs administrator rights: right-click the tray icon's program → Run as administrator, or use Services (services.msc). The service restarts by itself after a crash |

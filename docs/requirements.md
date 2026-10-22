# Stone's Hotspot — requirements (owner's brief)

Copy of the brief from Stone's IT Consulting that the phases are built from. Decisions taken along the way are
recorded in the `phase-N-plan.md` files and the phase reports.

## 1. Role and way of working

- Build in the phases of section 7, in order. At the end of each phase: run the build and tests, list what was
  made, explain how to test by hand, and **stop and wait for "OK"** before the next phase.
- Before coding a phase, write a short plan in `docs/phase-N-plan.md`.
- Ask first when a requirement is unclear or a design choice is risky (network changes, drivers, payments).
- Never hard-code secrets: config file excluded from Git + example file next to it.
- Automated tests for business logic (tariffs, vouchers, sessions, payments); manual checklist for network code.
- Keep `README.md` up to date (install, configuration, troubleshooting).
- All user-facing text in **French and English** (resource files). Default language: French.
- Currency **FCFA (XAF)**, whole numbers, formatted like `1 000 FCFA`.

## 2. Product summary

The PC has two connections: **WAN** (internet: Ethernet, 4G modem or Wi-Fi) and **LAN** (second card to a Wi-Fi
access point in AP/bridge mode, or the PC's own Wi-Fi in Mobile Hotspot mode). Customers on the Wi-Fi are blocked
and redirected to a **captive portal** until they log in with a voucher code, a user account, a free-access method
or a Mobile Money payment; then they get internet according to a **tariff** (time, data, speed). Staff manage
everything from an **admin dashboard**.

## 3. Architecture

| Part | Technology |
|---|---|
| Language/runtime | C# on .NET 8, single solution |
| Packet control | WinDivert 2.2 via P/Invoke: filter, block, redirect, rate-limit LAN traffic |
| Background engine | Windows Service `StonesHotspot.Service`, LocalSystem, auto-start |
| Web server | ASP.NET Core (Kestrel) in the service: captive portal + admin dashboard |
| Admin UI | Web dashboard at `http://<gateway-ip>:8080/admin` and `http://localhost:8080/admin` (usable on a phone) |
| Tray app | WinForms/WPF: status, Start/Stop, "Open dashboard" |
| Database | SQLite + EF Core migrations in `%ProgramData%\StonesHotspot\` |
| DNS | Built-in DNS forwarder on the LAN gateway IP, port 53: captive-portal detection, free URLs, domain blocking |
| Network sharing | Windows ICS via `HNetCfg.HNetShare` (gateway 192.168.137.1). Optional built-in DHCP server mode |
| IP → MAC | `GetIpNetTable2` + DHCP lease info |
| Logging | Serilog rolling files in `%ProgramData%\StonesHotspot\logs` |
| Installer | Inno Setup: service, WinDivert, tray app, firewall rules |

Projects: Core, Network, Web, Service, Tray, Licensing, LicenseManager (owner only, never shipped),
LicenseServer, Tests. Every Pro-only feature asks a single `ILicenseService`.

## 4. Data model and rules

Entities: Tariff, Voucher, UserAccount, Device, Session, Staff, Shift, Product, ProductGroup, Sale/Transaction,
WebFilterRule, Setting, AuditLog (details in `docs/business-rules.md`).

Rules: voucher activates at first login (validity starts then); time counted per second, data per byte; session
ends immediately when time/data/validity runs out or after idle timeout, device blocked again; max devices per
voucher/account; wrong codes rate-limited per device (5 tries, 5-minute lockout); shift cash totals = sum of cash
transactions.

## 5. Network behaviour

Unauthenticated device: allow DHCP, DNS to the gateway, HTTP/HTTPS to the portal, free-access domains/IPs;
redirect any HTTP (TCP 80) with `302` to `http://<gateway-ip>/portal?mac=…&url=<original-url>`; block
everything else (including HTTPS). Captive-portal detection must trigger the "Sign in to network" pop-up
(Android `connectivitycheck.gstatic.com/generate_204`, Apple `captive.apple.com/hotspot-detect.html`, Windows
`www.msftconnecttest.com/connecttest.txt`, Firefox `detectportal.firefox.com`): no "success" answer before login,
normal answer after.

Authenticated device: full internet minus blocked domains/IPs ("Site bloqué / Site blocked" page on HTTP); byte
counting and per-device token-bucket speed limits in the WinDivert loop; optional fair-share mode.

Robustness: a crash/stop must not leave the LAN fully open nor break the PC's own internet (document it);
re-apply firewall/ICS and restore sessions at start; MAC is the identity (explain random/private MACs in the
dashboard); unique self-signed HTTPS certificate per installation for the optional HTTPS admin page.

## 6. Licensing, trial and activation

- Editions: **Trial** (all Pro features, 30 days from first install), **Basic** (portal, vouchers, accounts,
  tariffs, dashboard, bandwidth control, web filter, basic reports), **Pro** (+ Mobile Money, SMS login, free
  access, shifts, POS, products, receipts, full reports, multiple staff). Feature table in one place in Licensing.
- **Ed25519** signatures. Private key only in LicenseManager, encrypted with the owner's password; forced
  encrypted backup on first run; clear warnings about loss/leak. The app contains only the public key.
- Licence code: licence ID, customer, Machine ID, edition, issue date, expiry, signature; text groups
  (`SHS1-XXXXX-…`) for WhatsApp/SMS/email, plus `.lic` file and QR code.
- **Machine ID** (e.g. `MID-7K3Q-9XPA-2LMD`) from a hash of MachineGuid, board/BIOS serial, system disk serial,
  CPU ID; match if most still match. Copy + "Send via WhatsApp" on the activation screen. Moving a licence:
  revoke + new code.
- Durations: 1 year or custom days; early renewal extends from the current expiry.
- Warnings 30/14/7/3/1 days before end (dashboard banner + tray). **7-day grace** after expiry with a red banner;
  then the hotspot stops ("Service temporairement indisponible"), dashboard limited to activation, reports and
  backup/export. Never delete or lock customer data. A valid code takes effect immediately.
- Trial protection: first-run date in several protected places (DPAPI file in ProgramData + registry), tied to
  the Machine ID, survives uninstall/reinstall; clock-tampering detection (latest date seen, > 24 h backwards →
  warning, count from latest date; trusted time from the licence server / HTTPS `Date` when online); register
  the trial online.
- **LicenseServer**: ASP.NET Core API + own database, HTTPS, cheap Linux VPS or Windows host (ask where).
  Endpoints: register trial, activate, daily check-in (valid / revoked / renewed), download renewal. Responses
  signed with the owner's key. Offline keeps working until expiry; revoked → grace period → stop; unreachable
  server → retry later. Password-protected admin page for the owner.
- **LicenseManager**: password-protected desktop app, never shipped. New licence (Machine ID, customer, phone,
  business, edition, duration → code + `.lic` + QR, copy, WhatsApp), customers & licences list, renew, revoke
  (sent to the server), CSV export, backup of its database and private key. Works offline, syncs when online.
- Honest limits in `docs/licensing.md`: obfuscate, code-sign, check in several places, integrity check at start;
  never promise it is unbreakable.

## 7. Build phases

- **Phase 0 — Project skeleton.** Solution, projects, Git, `.gitignore`, README, config, Serilog, `build.ps1`,
  FR/EN resources, XAF formatting. Done when `build.ps1` builds and tests pass.
- **Phase 1 — Data layer and business rules.** EF Core models + migrations, seed (Admin with forced password
  change, 3 sample tariffs), TariffService, VoucherService, SessionService, AccountService, CashService. Done
  when tests cover activation, countdown, expiry, idle, max devices, rate limiting, shift totals.
- **Phase 2 — Network setup.** Detect adapters, choose WAN/LAN; Mode A: ICS via `HNetCfg.HNetShare`; Mode B
  (optional): static LAN IP + built-in DHCP server; connected clients from neighbour table and DHCP; clear
  errors (not admin, no LAN adapter, IP conflict, ICS failure). Done when a phone joining the AP gets an IP and
  appears in "Connected devices" (internet still open); manual test checklist.
- **Phase 3 — Packet gate (WinDivert) and DNS.** Capture loop on LAN traffic only, in-memory table of
  authorised MACs/IPs synced with SessionService, allow/redirect/block rules, DNS forwarder, captive-portal
  detection, free-access and blocked domains, byte counting. Done when Android, iPhone and Windows laptop show
  the sign-in page automatically, a debug "authorise" button gives internet, "revoke" blocks within 2 s.
- **Phase 4 — Captive portal.** Mobile-first, fast, no external CDN, FR/EN, business name/logo. Pages: voucher
  login, username/password login, status (time/data left, speed, log out), errors, terms of use, blocked site.
  Redirect to the original URL or a welcome page. Done when a printed voucher gives internet, the countdown is
  right and the device is cut off when time runs out.
- **Phase 5 — Admin dashboard.** Staff login (Admin / Cashier), live devices (add time, log out, block,
  whitelist), tariffs CRUD, voucher batches (A4 cards and 58/80 mm thermal format with QR, CSV export,
  disable/delete), user accounts with deposits, settings (business name, logo, colours, texts, languages,
  network mode, idle timeout), database backup/restore. Done when a cashier sells a voucher on a phone and sees
  it in their shift.
- **Phase 6 — Bandwidth control and web filter.** Per-tariff limits, fair share, WAN speed test; web filter
  (block domains/IPs, ready-made "Social networks" and "Adult content" lists, walled garden, MAC white/black
  lists). Done when a phone speed test matches the limit (±15 %) and a blocked domain cannot be opened.
- **Phase 7 — Business tools.** Shifts, products and groups with stock, POS (products + vouchers), receipts
  (ESC/POS thermal and PDF), reports with date filters (cash per shift/staff, transactions, voucher sales by
  tariff, turnover by day/month, session statistics), CSV/PDF export, audit log viewer. Done when closing a
  shift prints a report whose totals match the transactions.
- **Phase 8 — Free access and SMS login.** Each on/off with its own tariff and max N logins per device per day:
  accept terms; shared access password (changeable daily); SMS code (`+242…` number, 6-digit code). SMS via a
  GSM/USB modem with AT commands (serial port, SIM PIN disabled) or an HTTP SMS provider (URL template). Limits:
  per number per day, per device per day, total per day; allowed prefixes. Social login (Google/Facebook): do
  not build — write a note in `docs/` on what it requires and ask. Done when SMS login works end-to-end with the
  configured modem or provider and limits are enforced.
- **Phase 9 — Mobile Money.** `IPaymentProvider` with MTN MoMo (Collection API, Request to Pay) and Airtel Money
  (Collections API), sandbox first. Portal flow: tariff → MTN/Airtel → phone number → payment request → customer
  approves with PIN → callback or polling → voucher created, shown, sent by SMS, device logged in. Handle
  timeouts, declines, duplicate callbacks (idempotency), daily reconciliation report. Reports show method MTN
  MoMo / Airtel Money. **Before starting, ask**: operators with merchant/API accounts, credentials, public URL
  for callbacks. Done when a sandbox payment produces a working voucher that appears in reports.
- **Phase 10 — Licensing, trial and activation** (section 6). Licensing replacing the stub, LicenseManager,
  LicenseServer (API + admin page). Dashboard activation screen (FR/EN): Machine ID with Copy/WhatsApp, code
  box, `.lic` import, edition and expiry. Fake clock in tests. Done when tests prove: (1) code for another PC
  rejected, (2) edited/forged code rejected, (3) Basic hides Pro features, (4) trial → warnings → expiry →
  7-day grace → stop, (5) reinstall does not reset the trial, (6) clock moved back does not extend the trial,
  (7) revoked licence stops after grace, (8) offline works until expiry; then a real code is activated on a test
  PC by WhatsApp copy-paste.
- **Phase 11 — Windows service, installer, updates.** Service with auto-restart; tray app. Inno Setup: admin,
  Program Files, data in ProgramData, WinDivert, firewall rules for 53/80/443/8080 on the LAN adapter only,
  service, desktop shortcut. Uninstaller removes service and network changes, asks before deleting the database,
  keeps trial/licence records. First-run wizard (language, business name, admin password, WAN/LAN, network mode,
  first tariffs). Daily backup (keep 14) + manual export. Two outputs: customer installer (build check fails if
  the private key or LicenseManager files are inside) and `LicenseManager-Setup.exe`. Done when a clean Windows 11
  install + wizard gives a working hotspot without editing files.
- **Phase 12 — Hardening and final testing.** Password hashing, admin session timeout, CSRF, input validation,
  dashboard only from LAN/localhost, rate limiting on all login forms, no secrets in logs; configurable retention
  for session logs (who/when/MAC/phone); load test with 50 devices; final manual checklist (Android, iPhone,
  Windows and Mac laptops, PC reboot, WAN lost/restored, AP unplugged, power cut during a session, time running
  out mid-download); user manuals `docs/manual-fr.md` and `docs/manual-en.md`.

## 8. Ask the owner when reached

Exact tariff prices and names; final Basic/Pro split and licence-server hosting; business name, logo and
colours; Wi-Fi access point model (AP/bridge mode); SMS modem model or provider account; Mobile Money merchant
accounts and API keys; receipt printer model.

## 9. Deliverables

Source in Git with a clean history (commits per phase, private key never committed); `StonesHotspot-Setup.exe`,
`LicenseManager-Setup.exe` (owner only) and the deployable LicenseServer; README, FR/EN manuals, manual test
checklists; `docs/architecture.md` (packet gate, portal, sessions, payments).

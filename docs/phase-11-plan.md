# Phase 11 — Windows service, installer and updates (plan)

Goal: on a clean Windows 11 PC, running `StonesHotspot-Setup.exe` and answering the first-run wizard gives a working
hotspot, without editing any file. A second installer, `LicenseManager-Setup.exe`, is for the owner only.

## Installer (Inno Setup 6, `installer/StonesHotspot.iss`)

- Administrator, 64-bit, French and English, installs to `C:\Program Files\Stone's Hotspot`, data stays in
  `%ProgramData%\StonesHotspot`.
- Files: the self-contained published service and tray app (no .NET to install), WinDivert driver + licence,
  documentation.
- Creates the Windows service `StonesHotspot` (LocalSystem, automatic delayed start) with **restart on failure**
  (5 s, 10 s, 30 s; counter reset after a day); starts it.
- Tray app started for every user at sign-in; desktop shortcut "Stone's Hotspot" (dashboard) and the tray app.
- **Upgrade** (new version over an old one): the service is stopped, files replaced, service started again; data,
  settings and licence untouched.
- **Uninstall**: stops the service (which switches internet sharing off and closes the gate), deletes the service,
  removes the firewall rules and the tray start-up entry, then **asks** whether to delete the data (database,
  backups, logs) — default **No**. The trial/licence records (`%ProgramData%\StonesHotspot.Licence`, registry) are
  always kept.
- Inno Setup only runs on Windows: `build.ps1 -Installer` builds both installers when Inno Setup is installed.

## Build check (two outputs)

`tools/Test-ReleaseContent.ps1` runs on the staged files before Inno Setup and **fails the build** when the customer
installer would contain anything of the owner: LicenseManager / LicenseIssuer files, `*.shkey`, `tools/dev-keys`,
private keys, `appsettings.Local.json`, or the **development public keys** (unless `-AllowDevelopmentKeys` for a test
build). `LicenseManager-Setup.exe` (`installer/LicenseManager.iss`) is built separately.

## Firewall (by the service, LAN adapter only)

At start and whenever the network setup is applied, the service creates (or corrects) inbound rules in the group
"Stone's Hotspot", limited to the LAN adapter: TCP 80 (portal), 443 (HTTPS dashboard/portal), 8080 (dashboard), UDP
53 (DNS). Windows Firewall COM API (`HNetCfg.FwPolicy2`), so the rule follows the adapter's name. Removed by the
uninstaller.

## HTTPS

A unique self-signed certificate is created on the first start (RSA 2048, 5 years, names: the PC name, localhost,
192.168.137.1), kept in the data folder (password protected by DPAPI). Kestrel serves HTTPS on port 443 (setting
`StonesHotspot:HttpsPort`, 0 = off) for the optional HTTPS dashboard (`https://192.168.137.1/admin`); the browser
shows a warning once because the certificate is self-signed.

## First-run wizard (`/setup`, only from the PC itself, until completed)

1. Language (FR/EN) · 2. Business name and Wi-Fi name · 3. Administrator password (replaces the random first
password) · 4. Internet (WAN) and customer (LAN) connections, applied at once (ICS or Windows Mobile hotspot,
detected) · 5. First plans: the three sample plans with prices to adjust (or keep) · Done → dashboard.
Until completed, the dashboard on the PC opens the wizard; from other devices it shows "finish the setup on the PC".

## Backups

Automatic backup every day (first one 10 minutes after start, then every 24 h), keeping the 14 latest automatic
ones; manual backups (Sauvegarde page) are never deleted automatically. Manual export already exists.

## Updates

- The licence server publishes the latest version: `GET /api/v1/update` → **signed** (server key) manifest:
  version, download address, SHA-256, notes (owner sets them in the server settings after uploading the installer).
- The hotspot checks once a day; admins see "Version x.y disponible" on the dashboard.
- Tray menu **Installer la mise à jour**: downloads the installer, checks its SHA-256 against the signed manifest,
  runs it (Windows asks for administrator rights). The installer upgrades in place.

## Tests

Release content check (forbidden files, development keys); firewall rule plan for an adapter; certificate creation
and reuse; wizard (steps, only local, completes, password set); daily backup rotation keeps 14 and never deletes
manual ones; update manifest signature and version comparison; tray download verification (hash). Manual checklist:
clean Windows 11 install, upgrade, uninstall (keep / delete data), crash restart.

## Also in this phase

- Offline admin password reset (promised in the README troubleshooting): `StonesHotspot.Service.exe
  --reset-admin-password` from an administrator command prompt writes a new temporary password to
  `initial-admin-password.txt`.
- Desktop shortcut = tray app with `--dashboard` (opens the dashboard, starts the tray icon if needed).

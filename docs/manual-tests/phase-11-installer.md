# Manual test — Phase 11: installer, first-run wizard, service restart, updates

Needs: a **clean Windows 11 PC** (or a VM with two network adapters: internet + customer side), the access point or
Windows Mobile hotspot, a phone. Build on a Windows PC with Inno Setup 6: `.\build.ps1 -Installer` (owner keys), or
`.\build.ps1 -Installer -AllowDevelopmentKeys` for a test-only build.

## Build

| # | Step | Expected |
|---|------|----------|
| 1 | `.\build.ps1 -Installer` with the development keys still in `src\StonesHotspot.Licensing\Keys` | Fails at "Release check": `StonesHotspot.Licensing.dll — built with the DEVELOPMENT public keys` |
| 2 | Same with `-AllowDevelopmentKeys` | `installer\Output\StonesHotspot-Setup-x.y.z.exe` and `LicenseManager-Setup-x.y.z.exe`, each with its SHA-256 |
| 3 | Open the customer installer with 7-Zip (or `innounp -v`) | No LicenseManager, LicenseIssuer, LicenseServer, `.shkey`, `appsettings.Local.json` |

## Clean install

| # | Step | Expected |
|---|------|----------|
| 4 | Run `StonesHotspot-Setup.exe` (French Windows) | French installer, asks for administrator rights, installs to `C:\Program Files\Stone's Hotspot` |
| 5 | Finish with "Ouvrir le tableau de bord" ticked | Browser opens `localhost:8080` → **Première installation**, step 1 of 6; tray icon present |
| 6 | `services.msc` → Stone's Hotspot | Automatic (delayed start), running, LocalSystem; Recovery tab: restart the service, 1st/2nd/subsequent failures |
| 7 | Wizard: language English | Next steps in English |
| 8 | Business name + Wi-Fi name | Saved (later visible in **Paramètres**) |
| 9 | Password "court" | Error, still step 3. A good password → step 4; `%ProgramData%\StonesHotspot\initial-admin-password.txt` is gone |
| 10 | Network: internet adapter + customer adapter (suggested ones) → **Partager Internet** | Step 5. `Get-NetFirewallRule -Group "Stone's Hotspot"` lists 4 inbound rules, each limited to the customer adapter |
| 11 | Change "1 heure" to 250, **Suivant**, **Terminer** | Sign-in page "Installation terminée"; sign in as `admin` with the new password; **Forfaits** shows 250 FCFA |
| 12 | Phone joins the Wi-Fi | Portal opens; a code from **Vendre** gives internet. No file was edited by hand |
| 13 | Open `http://192.168.137.1:8080/setup` on the phone **before** step 11 (repeat on a fresh install) | "Finish the setup on the PC", no form |
| 14 | `https://192.168.137.1/admin` on the phone | Certificate warning once (self-signed, issued to "Stone's Hotspot (PC name)"), then the dashboard |

## Crash restart, backups

| # | Step | Expected |
|---|------|----------|
| 15 | Task Manager → end `StonesHotspot.Service.exe` | Within ~5 s the service runs again (new PID); phones get internet back after the restart; log shows the new start |
| 16 | Leave the PC on 10 minutes after a start | `%ProgramData%\StonesHotspot\backups\stoneshotspot-…-auto.db` appears; restarts the same day do not add another |
| 17 | (Optional) set the clock forward a day at a time 16 times, restarting the service each time | Never more than 14 `-auto` files; manual backups untouched |

## Updates

| # | Step | Expected |
|---|------|----------|
| 18 | Build version 0.1.1, upload it over HTTPS, set `LicenceServer:Update` on the licence server, restart it; restart the hotspot service, wait 2 minutes | Admin dashboard: green banner "Nouvelle version 0.1.1…"; tray balloon and menu **Installer la mise à jour 0.1.1** |
| 19 | Put a wrong `Sha256` on the server, restart both, click the tray item | "La mise à jour n'a pas pu être installée : … SHA-256 …"; nothing was run |
| 20 | Correct hash, tray → **Installer la mise à jour** → Yes → accept the administrator prompt | Installer runs silently, service back within a minute, `/health` shows 0.1.1; data, plans, licence unchanged |
| 21 | Run the same installer again by hand (upgrade over the same version) | Works; service stopped and restarted; no second service, no duplicated firewall rules |

## Uninstall

| # | Step | Expected |
|---|------|----------|
| 22 | Settings → Apps → Stone's Hotspot → Uninstall, answer **Non** to "Supprimer aussi les données" | Service gone (`sc query StonesHotspot` fails), internet sharing off, no "Stone's Hotspot" firewall rules, no tray at next sign-in; `%ProgramData%\StonesHotspot` still there |
| 23 | Reinstall | Wizard does **not** open (existing database), same plans and sales; licence/trial unchanged |
| 24 | Uninstall again, answer **Oui** | `%ProgramData%\StonesHotspot` deleted; `%ProgramData%\StonesHotspot.Licence` and `HKLM\SOFTWARE\StonesHotspot\Licence` still there |
| 25 | Reinstall | Wizard opens (new database); the trial does **not** restart at 30 days |

## Lost admin password

| # | Step | Expected |
|---|------|----------|
| 26 | Administrator command prompt: `"C:\Program Files\Stone's Hotspot\StonesHotspot.Service.exe" --reset-admin-password` | Prints the file path; `initial-admin-password.txt` holds a new temporary password; sign-in with it forces a new password. The running service is not disturbed |

## LicenseManager installer (owner's PC only)

| # | Step | Expected |
|---|------|----------|
| 27 | Run `LicenseManager-Setup.exe` | Installs to `C:\Program Files\Stone's Hotspot LicenseManager`, shortcut; first start asks for the password; keys stay in `%LOCALAPPDATA%\StonesHotspot.LicenseManager` |

# Manual test — Phase 3: packet gate and DNS

Needs the Phase 2 setup working (see `phase-2-network.md`) on a real Windows 10/11 PC. Tick each line; for
anything unexpected, send a screenshot of <http://localhost:8080/admin/gate> plus the latest
`service-*.log` — the counters on that page show exactly where packets go.

## Before you start

From an **elevated** PowerShell in the repository folder:

```powershell
.\build.ps1 -Publish            # downloads WinDivert 2.2.2 (checked by SHA-256) and publishes the service
# Allow the portal (80) and dashboard (8080) from the customer network (the installer will do this later):
New-NetFirewallRule -DisplayName "Stone's Hotspot portal + dashboard" -Direction Inbound -Protocol TCP -LocalPort 80,8080 -Action Allow
# Port 80 must be free: if this prints something, stop that program/service (IIS, Skype, another web server…)
Get-NetTCPConnection -LocalPort 80 -State Listen -ErrorAction SilentlyContinue
.\artifacts\publish\StonesHotspot.Service\StonesHotspot.Service.exe
```

Open <http://localhost:8080/admin/network>, choose WAN/LAN, click **Partager Internet**. Then open
<http://localhost:8080/admin/gate>: state must be **En marche**.

Devices: an **Android** phone, an **iPhone**, a **Windows laptop**. Forget the Wi-Fi on each first, so they
start fresh.

## Tests

| # | Step | Expected |
|---|------|----------|
| 1 | Android joins the Wi-Fi | Within ~10 s a notification / screen "Se connecter au réseau" / "Sign in to network" appears; tapping it shows the **Stone's Hotspot portal** page with the phone's MAC and IP. On the gate page "Connexions web redirigées" goes up |
| 2 | iPhone joins the Wi-Fi | The login sheet opens by itself and shows the portal page |
| 3 | Windows laptop joins the Wi-Fi | A notification "Action requise / Action needed" (or the browser opens) leading to the portal |
| 4 | On a phone, open `http://neverssl.com` in the browser | The portal page appears (redirect), with the original address in the URL |
| 5 | Open `https://www.google.com` | Fails at once (no long wait): HTTPS cannot be redirected |
| 6 | On the gate page, click **Autoriser** next to the phone | Within **2 seconds** the phone browses normally (open several sites, a YouTube video). The phone's "sign in" notification goes away. The counters "Paquets envoyés / reçus" grow |
| 7 | Click **Retirer l'accès** while a video plays | Within **2 seconds** the video stops loading; new pages land on the portal again |
| 8 | Add rule `facebook.com` → **Bloqué**; wait 10 s; authorise the phone | `http://facebook.com` shows "Site bloqué"; the Facebook app / `https://facebook.com` does not load. Other sites work. Remove the rule → Facebook works again (allow the phone's DNS cache ~10 s) |
| 9 | Revoke the phone. Add rule `wikipedia.org` → **Accès libre** | Without being authorised, `https://www.wikipedia.org` loads; other sites still go to the portal. "Adresses en accès libre apprises" > 0 |
| 10 | On the phone, set Private DNS / DNS to 8.8.8.8 (Android: Settings → Private DNS → off; Wi-Fi → IP settings static, DNS 8.8.8.8) | Same behaviour as before (DNS is answered by the hotspot whatever server the phone asks) |
| 11 | With the phone authorised, run a speed test (fast.com) | Works; speed close to the PC's own internet (no limit yet — phase 6) |
| 12 | The PC itself: browse, run a speed test | The PC's own internet is never affected |
| 13 | Stop the service (Ctrl+C or `sc stop`) | The log says "internet sharing switched off, packet gate closed"; the phone has **no** internet at all (not open). Start it again → sharing comes back by itself within ~5 s, still filtered |
| 14 | Stop the service, rename `WinDivert64.sys` to `WinDivert64.sys.off`, start the service | Network page shows red "Le filtrage des paquets (WinDivert) n'a pas pu démarrer…"; sharing is **off** (phone has no internet). Rename back, restart → works again |
| 15 | With the phone authorised, switch it off and on Wi-Fi (it may get a new IP) | It still has internet within a few seconds (MAC is the identity) |
| 16 | Kill the service process in Task Manager (simulated crash) | Known limit: the Wi-Fi stays shared **without filter** until the service starts again (phase 11 makes Windows restart it automatically). Restart it by hand now |

## What to send back if something fails

- Screenshot of the gate page (state, counters).
- `service-*.log` from `%ProgramData%\StonesHotspot\logs` (or `src\StonesHotspot.Service\data\logs` when run
  from source).
- Which test number, which phone/OS version.

The least certain points (cannot be tested without Windows): **(a)** that the reverse traffic to the phones is seen
(counter "Paquets reçus" grows in test 6), **(b)** DNS answers and resets sent "from the internet" reach the phone
(test 5 fails quickly; test 10), and **(c)** the redirect delivery to the portal (test 4). "Erreurs d'envoi de
paquets" above 0 points to (b) or (c).

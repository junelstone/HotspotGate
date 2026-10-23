# Phase 4 — Captive portal (plan)

Goal: a customer joins the Wi-Fi, the portal opens, they type the code from their ticket (or their account
username/password), and they have internet; a status page counts down their time/data and lets them log out;
when the time runs out they are cut off and the portal tells them why.

## Pages (all under `/portal`, served on the gateway, port 80)

| Page | Content |
|---|---|
| `/portal` | Business name + logo, **Code** form (default) and **Account** form, link to the terms, FR/EN switch. Shows why the last session ended ("time used up", "idle", …) when that happened in the last 15 min. `?code=ABCD-EFGH` pre-fills the code (QR on printed tickets, phase 5). Already logged in → status page. Licence stopped → "Service temporairement indisponible". |
| `/portal/status` | Time left (live countdown), data left, valid until, plan name, current download/upload speed, speed limit, account balance, **Se déconnecter / Log out**. Polls a small JSON endpoint every 3 s; at 0 it reloads and the device is back on the login page with the reason. Right after login it shows "Connecté" and forwards to the page the customer wanted. |
| `/portal/terms` | Terms of use (default text FR/EN, editable later in settings). |
| `/portal/blocked` | Exists since phase 3. |

Errors (French/English): invalid code, wrong username/password, code disabled, code expired, time/data used up,
"already used on the maximum number of devices", too many tries (with minutes to wait), device blocked, account
disabled/expired, insufficient balance, device not identified (no MAC found — reconnect the Wi-Fi).

Pages stay small: one inline stylesheet, ~30 lines of inline JavaScript on the status page only, no external files
(the customer has no internet yet). Accent colour and logo come from settings (`Portal.AccentColor`, logo file in
`%ProgramData%\StonesHotspot\branding\logo.png|jpg|svg`; upload UI in phase 5).

## After login

1. The engine is woken at once (no waiting for the next 1-second tick), so the device passes the gate within
   milliseconds.
2. Destination: the configured **welcome page** (`Portal.WelcomeUrl`) if set; otherwise the page originally asked for
   (`url=`), unless it is an OS connectivity check (`generate_204`, `hotspot-detect`, `connecttest`…) or not
   http/https; otherwise the status page.

## Core additions

- `SessionService.GetStatusForDeviceAsync(mac)` — live remaining time (includes the seconds since the last tick),
  data, validity end, plan, balance.
- `SessionService.GetRecentlyEndedAsync(mac, within)` — to explain a cut-off.
- `Units` — durations ("1 h 05 min"), sizes ("850 Mo" / "850 MB"), speeds ("1,5 Mbit/s").
- `LiveTrafficMonitor` (Network) — current speed per session, fed by the engine.
- `AccessRefreshSignal` (Network) — lets a login/logout trigger an immediate engine tick.

## Development convenience

Run from source (Development), a request from the PC itself (localhost) gets a fixed test MAC
`02:00:00:00:00:01`, so the portal can be tried in a desktop browser without a phone. Never in production.

## Tests

Login with code (success, every error), account login, lockout after 5 tries, too many devices, status JSON and
countdown values, logout, ended-session message, terms page, service-unavailable page, welcome/original URL
choice (probe URLs and `javascript:` refused), unit formatting in FR/EN.

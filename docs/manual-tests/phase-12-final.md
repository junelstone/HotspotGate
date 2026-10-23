# Final manual test — Phase 12: real devices, failures, security, 50 devices

Run on the real shop set-up (installed hotspot PC, access point, internet line) after the earlier checklists
(phases 2–11). Note the date, the version (`/health`) and every failure with the time, so the service log
(`C:\ProgramData\StonesHotspot\logs`) can be read for it.

## Devices (each with a new 30-minute code)

| # | Device | Steps | Expected |
|---|--------|-------|----------|
| 1 | Android phone (Chrome) | Join the Wi-Fi | "Sign in to Wi-Fi network" notification within seconds; it opens the portal; code → internet; **Status** shows time left |
| 2 | iPhone (Safari) | Join the Wi-Fi | The captive sheet opens by itself; code → "Done" closes it; internet in Safari and apps |
| 3 | Windows laptop (Edge) | Join the Wi-Fi | "Action needed" / browser opens `msftconnecttest` → portal; code → internet |
| 4 | Mac laptop (Safari) | Join the Wi-Fi | Captive window; code → internet |
| 5 | Any | Type the code in lower case with a dash (`abcd-efgh`) | Accepted |
| 6 | Any | 6 wrong codes | 5th → "wait 5 minutes"; then "Too many tries" if the page is posted again quickly |
| 7 | Second phone with the same 1-device code | Log in while the first phone is online | Refused (code in use) |
| 8 | Any, Mobile Money (sandbox or 100 FCFA real) | Buy a plan | Code created, internet without typing |

## Time and data running out

| # | Step | Expected |
|---|------|----------|
| 9 | Data plan of 50 MB: start a big download | Download stops when the quota is used (± 1 s of traffic); the portal comes back on the next page |
| 10 | 30-minute code, start a long video / download near the end | At the end the stream stops; the phone goes back to the portal; **Live** no longer lists it |
| 11 | "Usage only" plan: log out from **Status** after 10 minutes, log in again later | Remaining ~20 minutes |

## Failures

| # | Step | Expected |
|---|------|----------|
| 12 | **PC reboot** with 3 customers online | After Windows starts (service delayed start ~2 min) the same customers get internet again without a new code; time used while off is not charged |
| 13 | **Internet lost** (unplug the box / 4G) for 5 minutes, then back | Customers keep their session (no internet meanwhile); when the line is back internet returns without a new login; the dashboard and portal stay reachable during the cut |
| 14 | **Access point unplugged** 2 minutes, then back | Phones reconnect to the Wi-Fi and get internet again with their running session; **Network** shows the LAN problem while unplugged |
| 15 | **Power cut** during a session (pull the PC plug) | At power-on everything restarts by itself; the database opens (no "corrupt"); running sessions continue; at most the last second of usage is lost |
| 16 | Kill `StonesHotspot.Service.exe` in Task Manager | Restarted within ~5 s; customers lose internet only during the restart (never open without the gate for more than a few seconds) |
| 17 | Change the PC clock back by 2 days | Yellow clock banner; plans and licence do not get longer |

## Security

| # | Step | Expected |
|---|------|----------|
| 18 | From a PC on the **internet box side** (WAN network): open `http://<hotspot PC address>:8080/admin` | Not reachable (404 or no answer) |
| 19 | From a phone on the hotspot Wi-Fi: `http://192.168.137.1:8080/admin` | Sign-in page (staff can use their phone) |
| 20 | Sign in, wait 2 hours without clicking | Asked to sign in again |
| 21 | 5 wrong staff passwords | "Wait 5 minutes"; posting again quickly gives "Too many tries" |
| 22 | Cashier account: open `/admin/staff` or `/admin/settings` by typing the address | "Access denied" |
| 23 | Mobile Money / SMS keys saved, then open **Journal** and the log files | Only "(secret changed)"; no key, password or full phone number in the log files |
| 24 | **Settings** → keep connection logs 30 days; (test PC) move the date 31 days forward, wait for the daily run (or restart the service and wait 10 minutes) | Old sessions in **Reports → Sessions** show `ANON-…` instead of the MAC; amounts unchanged |

## 50 devices (one afternoon, or 50 phones/laptops borrowed for an hour)

| # | Step | Expected |
|---|------|----------|
| 25 | 50 devices log in within a few minutes (codes from one batch) | All get internet; portal pages answer in under 2 s |
| 26 | Everyone browses / watches video for 30 minutes | Speeds follow the plans and fair share; **Live** stays fluid; the PC's CPU stays reasonable (Task Manager); no error in the service log |
| 27 | **Reports → Sessions** afterwards | 50 sessions with their data used |

Reference numbers from the automated load test (`LoadTests`, development PC): 50 simultaneous portal logins ≈ 2 s
in total, about 700 000 packets/s processed, engine tick with 50 sessions ≈ 20 ms.

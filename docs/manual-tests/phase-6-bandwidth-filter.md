# Manual test — Phase 6: bandwidth control and web filter

Needs Phases 2–5 working. Run the **published** service as administrator. Use an Android phone or iPhone with the
**Speedtest by Ookla** or **fast.com** app/site, and sign in to the dashboard as admin.

## Speed limits

| # | Step | Expected |
|---|------|----------|
| 1 | **Débit** → **Lancer le test** (few customers online) | After ~15 s: download and upload speed of the internet line, e.g. "↓ 12 Mbit/s · ↑ 3 Mbit/s". If it fails, the message says why (no internet, blocked site) |
| 2 | **Forfaits** → "1 heure": download 1024, upload 512 kbit/s (the defaults). Sell a "1 heure" code, log the phone in | — |
| 3 | On the phone, run a speed test 3 times | Download **0.87–1.18 Mbit/s** (1 Mbit/s ±15 %), upload **0.44–0.59 Mbit/s**. On <http://localhost:8080/admin/gate> "Paquets retardés" grows; "Paquets écartés" grows a little (normal: it makes TCP slow down) |
| 4 | Change the plan to 2048 / 1024 kbit/s while the phone stays logged in, test again | ~2 Mbit/s / ~1 Mbit/s within a second, no new login needed |
| 5 | Plan with no limits (empty fields), test again | Close to the line speed measured in test 1 |
| 6 | **Débit** → fair share on, totals = 4000 / 1000 kbit/s, min 256, max 0. Two phones logged in on a plan without limit, both run a speed test at the same time | Each gets ~2 Mbit/s down / ~0.5 Mbit/s up (±15 %). With only one phone testing: ~4 Mbit/s |
| 7 | After test 1, **Utiliser ce résultat pour le partage** | Totals filled with 90 % of the measured speeds |
| 8 | Browse normally (YouTube, WhatsApp call) on a 1 Mbit/s plan | Works smoothly at that speed (video drops to low quality, calls stay usable) |

## Web filter

| # | Step | Expected |
|---|------|----------|
| 9 | **Filtrage web** → "Réseaux sociaux" → **Bloquer cette liste**. On a logged-in phone open facebook.com, the Facebook, Instagram and TikTok apps | Nothing loads (browser at `http://facebook.com`: "Site bloqué"). WhatsApp still works |
| 10 | **Débloquer la liste** | Facebook works again (allow ~10 s for the phone's DNS cache, or switch Wi-Fi off/on) |
| 11 | Add `youtube.com` as blocked | YouTube site and app do not load |
| 12 | **DNS familial** → **Activer**, open a well-known adult site not in our list | Does not open. Normal sites work |
| 13 | Not logged in: add `wikipedia.org` to **Accès libre** | Wikipedia opens without a code; other sites still go to the portal |
| 14 | **Liste blanche**: add the cashier phone's MAC (shown on **Appareils connectés**) with a note | That phone has internet without a code, at full speed |
| 15 | **Liste noire**: add a phone's MAC | That phone gets no internet and the portal refuses its codes ("Cet appareil n'est pas autorisé…"). Remove it → it can log in again |
| 16 | Sign in as a cashier, open `/admin/filter` and `/admin/bandwidth` | "Accès refusé" |

If speeds are off by more than 15 %: send the plan limits, the speed test screenshots, the counters of
<http://localhost:8080/admin/gate> and the latest `service-*.log`.

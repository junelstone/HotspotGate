# Manual test — Phase 4: captive portal

Needs Phases 2–3 working (`phase-2-network.md`, `phase-3-gate.md`). Run the **published** service as administrator
(`.\build.ps1 -Publish`, then `artifacts\publish\StonesHotspot.Service\StonesHotspot.Service.exe`), with the
firewall rule for ports 80 and 8080 in place.

**Creating test codes** (until the dashboard in phase 5): on the PC open <http://localhost:8080/admin/gate>,
section *Codes de test*, choose a plan and click **Créer un code**. Write the code on paper ("printed ticket"). For test 8 use the *30 minutes* plan and wait (plans become
editable in the dashboard in phase 5).

Devices: Android phone, iPhone, Windows laptop. Forget the Wi-Fi on each first.

| # | Step | Expected |
|---|------|----------|
| 1 | Android joins the Wi-Fi | "Sign in to network" opens the portal: business name, big code field, "Compte" section, terms link. Page loads instantly (no internet needed) |
| 2 | Tap **EN** then **FR** | Texts switch language; the choice stays when moving between pages |
| 3 | Type a wrong code, **Se connecter** | Red "Code invalide…". After 5 wrong codes: "Trop d'essais. Réessayez dans 5 minute(s)." — other phones are not blocked |
| 4 | Type the code from the paper (lower case and/or with the dash is fine) | "Vous êtes connecté", then within 3 s the page asked for opens (or the status page stays if the phone only did its connectivity check). The "Sign in" notification disappears; the internet works |
| 5 | Open `http://192.168.137.1/portal/status` | Plan name, code, **Temps restant** counting down every second, "Valable jusqu'au", current speed moving while a video plays, "Consommé" growing |
| 6 | Compare the countdown with a clock for 5 minutes | Drops by 5 min (±2 s) |
| 7 | Type the **same code** on a second phone (plan allows 1 device) | "Ce code est déjà utilisé sur le nombre maximum d'appareils…" |
| 8 | Let the time run out (30-minute plan, or a short plan) while browsing | At 0 the phone is cut off within 1–2 s; the status page goes back to the portal with "Votre temps ou vos données sont épuisés…". The code can't be used again ("épuisés") |
| 9 | New code, log in, then **Se déconnecter** on the status page | Back to the portal with "Vous êtes déconnecté."; no internet. Log in again with the same code → the time continues where it stopped (30-minute and 1-hour plans pause when logged out) |
| 10 | iPhone: steps 1, 4, 5 | Same; the login sheet closes by itself (or shows "Done") after login |
| 11 | Windows laptop: steps 1, 4, 5 | Browser opens the portal; after login the "Action needed" notification goes away |
| 12 | Open `http://192.168.137.1/portal/terms` | Terms of use in the chosen language |
| 13 | Leave a logged-in phone with Wi-Fi on but unused for 10+ minutes | It gets logged out ("Vous avez été déconnecté après une période sans activité…"); the same code works again |

If something fails: test number, phone model/OS, screenshot of the phone and of
<http://localhost:8080/admin/gate>, and the latest `service-*.log`.

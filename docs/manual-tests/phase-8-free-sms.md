# Manual test — Phase 8: free access, password of the day, SMS login

Run the **published** service on Windows with the hotspot working (Phases 2–4). Use two phones on the hotspot
Wi-Fi. In **Forfaits**, create a plan "Gratuit 15 min" (15 minutes, time runs from first login).

## Free access and password

| # | Step | Expected |
|---|------|----------|
| 1 | **Accès gratuit** → free access on, plan "Gratuit 15 min", 1 per day. Phone A opens the portal | Card "Essayez gratuitement" with the terms checkbox |
| 2 | Tap **Accès gratuit** without ticking | "Cochez la case…" |
| 3 | Tick, **Accès gratuit** | Connected, 15 min countdown; no code shown on the status page |
| 4 | Log out, come back to the portal, free access again | Connected again with the time that was left (no new free login used) |
| 5 | After the 15 minutes, try free access again | "Vous avez déjà utilisé l'accès gratuit aujourd'hui…". Phone B still can |
| 6 | Password mode on, "Nouveau mot de passe chaque jour" ticked. Cashier opens the dashboard home | "Mot de passe Wi-Fi du jour : XXXXXX" |
| 7 | Phone B types it in lower case | Connected. A wrong password → "Mot de passe incorrect"; 5 wrong → locked 5 minutes |
| 8 | Next morning | The home page shows a different password; yesterday's no longer works |

## SMS login with a USB modem

| # | Step | Expected |
|---|------|----------|
| 9 | Put the SIM in a phone, **disable its PIN**, check it has SMS credit. Put it in the USB modem, plug it in, close the modem's own software. Note the COM port (Device Manager → Ports, the "Application Interface" or "PC UI" port is usually **not** the right one; try the "Modem"/"AT" port) | — |
| 10 | **Accès gratuit** → SMS on, plan, sending = Modem, port `COM?`, speed 115200, save. **Envoyer le test** to your own number | SMS "… : SMS de test. L'envoi fonctionne." within ~30 s. The log says "Envoyé" |
| 11 | Wrong port or modem software open | Red message explaining (no answer / cannot open the port); log says "Échec" with the reason |
| 12 | SIM with PIN still on | "The SIM card asks for its PIN code…" |
| 13 | Phone A: portal → number `06 …` → **Recevoir un code** | "Code envoyé au +242 …"; the SMS arrives; type the code → connected |
| 14 | Ask again within 60 s | "Patientez … secondes" |
| 15 | Ask 4 times for the same number (waiting a minute each time) | 4th: "Trop de codes demandés aujourd'hui…" |
| 16 | Number starting with 01 | "Ce numéro n'est pas accepté ici." |
| 17 | Wait 11 minutes, then type the code | "Ce code n'est plus valable…" |

## SMS login with an HTTP provider

| # | Step | Expected |
|---|------|----------|
| 18 | Sending = provider, paste the URL template from the provider's documentation, e.g. `https://…/send?apikey=…&to={phone}&text={message}` (or a JSON body). Save | The page shows only "Adresse enregistrée (host)", never the key |
| 19 | **Envoyer le test** | SMS arrives; if not, the message shows the provider's answer (HTTP code and text) |
| 20 | Repeat 13 with the provider | Same as with the modem |
| 21 | Open `service-*.log` | SMS lines show the number masked (`+24206****567`), never the code or the API key |

## Edition

| # | Step | Expected |
|---|------|----------|
| 22 | With a Basic licence (Phase 10) | Portal shows only code and account; **Accès gratuit** says "édition Pro" |

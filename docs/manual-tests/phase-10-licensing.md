# Manual test — Phase 10: licensing, trial and activation

Needs: the owner's PC with LicenseManager, a test PC with the hotspot installed (published service), WhatsApp on a
phone. Use the owner's real keys (see `docs/licensing.md` → "Before the first release") or the development keys for a
dry run.

## LicenseManager (owner's PC)

| # | Step | Expected |
|---|------|----------|
| 1 | First start of LicenseManager | Explanation, password twice (≥ 12 characters), then it insists on saving a copy of the key file (cancelling twice cancels the key creation) |
| 2 | Close, start again, wrong password 3 times | "Mot de passe incorrect", waits a little more each time; right password opens it |
| 3 | **Clés et sauvegarde** → copy both public keys, back up (zip) | Zip with `licences.db` and the key file |
| 4 | **Clients** → new customer with phone and business name | Listed |

## Trial and activation (test PC)

| # | Step | Expected |
|---|------|----------|
| 5 | Fresh install, open the dashboard | Yellow banner "Votre essai gratuit se termine dans 30 jour(s)"; **Licence** shows Trial and the Machine ID |
| 6 | **Envoyer par WhatsApp** | WhatsApp opens with "… Machine ID : MID-…" (set `Licensing:OwnerWhatsApp` to the owner's number to pre-fill the contact) |
| 7 | LicenseManager → **Nouvelle licence**: the customer, paste the Machine ID, Pro, 1 an → **WhatsApp** | The code is sent as a message |
| 8 | On the test PC, paste the code from WhatsApp → **Activer** | "Licence activée : Pro, valable jusqu'au …"; banner gone; tray shows no warning |
| 9 | Paste the same code on another PC | "Ce code a été fait pour un autre PC" |
| 10 | Change one character of the code | "Code invalide" |
| 11 | Uninstall, reinstall (delete the data folder too), open the dashboard | Still licensed after activating again; with no licence, the trial is **not** back to 30 days |
| 12 | Basic licence on a test PC | Caisse, Produits, Mobile Money, SMS, free access, shifts, extra staff show "édition Pro"; codes, accounts, plans, filter, bandwidth, basic reports work |

## Ends, grace, stop (use a 3-day licence, or change the PC date on a test PC only)

| # | Step | Expected |
|---|------|----------|
| 13 | 3-day licence; set the date to the day after the end | Red banner "le hotspot s'arrêtera le …" (7 days); everything still works; tray balloon |
| 14 | Date + 8 days | "Service arrêté"; phones get "Service temporairement indisponible"; dashboard only offers Licence, Rapports, Sauvegarde, Journal; data intact |
| 15 | Activate a new code | Works at once, banner gone |
| 16 | Set the date back 3 days | Yellow "L'horloge de ce PC a été reculée…"; nothing gets longer |

## Licence server (once deployed)

| # | Step | Expected |
|---|------|----------|
| 17 | `Licensing:ServerUrl` set on the hotspot, restart; wait 1–2 minutes | Owner page `/admin`: the trial or the licence check-in appears; **Licence** shows "Dernière vérification en ligne" |
| 18 | LicenseManager → **Renouveler** (365 days) a running licence → sync | Next check-in (or restart of the service): new end date on the hotspot without typing anything |
| 19 | LicenseManager → **Révoquer** → sync, restart the hotspot service | Red banner "Cette licence a été révoquée…", stops 7 days later |
| 20 | Server unreachable (unplug the internet) for days | Licence keeps working until its end date |

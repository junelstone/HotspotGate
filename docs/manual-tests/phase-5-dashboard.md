# Manual test — Phase 5: admin dashboard

Run the **published** service as administrator (see `phase-3-gate.md` for building and the firewall rule). The
dashboard is at <http://localhost:8080/admin> on the PC and `http://192.168.137.1:8080/admin` from a phone on the
hotspot Wi-Fi.

Have ready: the PC, one phone for the cashier, one phone as a customer, and (optional) a printer.

| # | Step | Expected |
|---|------|----------|
| 1 | Open `%ProgramData%\StonesHotspot\initial-admin-password.txt` (as administrator), sign in as `admin` with that password | You must choose a new password before anything else. Afterwards the file is gone |
| 2 | Sign in 5 times with a wrong password | "…incorrect", then "Trop d'essais. Réessayez dans 5 minute(s)." |
| 3 | **Paramètres**: business name, Wi-Fi name, colour, logo (PNG/JPG under 512 KB), save. Open the portal on the customer phone | Name, logo and colour appear on the portal |
| 4 | **Forfaits**: create "Test 3 min" — 50 FCFA, Temps, quota 3, 1 appareil | Listed with "3 min" |
| 5 | **Personnel**: create a cashier with a temporary password | Listed with the "mot de passe temporaire" badge |
| 6 | On the **cashier phone** (connected to the hotspot Wi-Fi) open `http://192.168.137.1:8080/admin`, sign in as the cashier | Must change password first; the menu shows only Accueil, Vendre, Ma caisse, Comptes; opening `/admin/live` shows "Accès refusé" |
| 7 | Cashier: **Vendre** | Warning "Ouvrez d'abord votre caisse"; the sell button is disabled |
| 8 | Cashier: **Ma caisse** → starting cash 5 000 → **Ouvrir la caisse** | "Caisse ouverte" |
| 9 | Cashier: **Vendre** → "Test 3 min", quantity 2, Espèces → **Vendre et imprimer** | Ticket page with 2 codes, QR codes, receipt number, total 100 FCFA. On a PC with a printer: **Imprimer** prints the codes as A4 cards |
| 10 | Cashier: **Ma caisse** | The 2 sales are listed; expected cash 5 100 FCFA |
| 11 | **Customer phone**: scan one QR (or type the code) on the portal | Logged in; internet works |
| 12 | Admin: **En direct** | The customer phone is listed with time left counting down and its speed; **+15 min** adds 15 minutes (visible on the customer's status page); **Déconnecter** logs it out; **Bloquer l'appareil** stops it logging in again (unblock later on the gate page) |
| 13 | Admin: **Tickets** → new batch of 21 codes for "1 heure" → **Imprimer en cartes A4** | One A4 page of 21 cards with QR codes. **CSV** downloads a file that opens in Excel with 21 rows |
| 14 | Cashier: **Vendre** → "Vendre un ticket déjà imprimé" → type one of the printed codes | "Ticket … vendu"; selling it again is refused |
| 15 | Admin: **Comptes** → new account with a first payment of 1 000 FCFA. Cashier: open it, top up 500 FCFA (MTN MoMo) | Balance 1 500 FCFA; the customer phone can log in with the username/password (the plan price is taken from the balance) |
| 16 | Cashier: **Ma caisse** → counted cash 5 000 → **Fermer la caisse** | Report with difference −100 FCFA (the MoMo top-up is listed separately, not in the cash) |
| 17 | Admin: **Personnel** → **Désactiver** the cashier while they are signed in, then the cashier clicks anything | The cashier is sent back to the sign-in page |
| 18 | Admin: **Sauvegarde** → **Créer une sauvegarde** | A `.db` file downloads. Change the Wi-Fi name in Paramètres, then **Restaurer** that file → the old Wi-Fi name is back; the list shows a "before-restore" copy |
| 19 | Leave the dashboard unused for more than 2 hours, then click | Back to the sign-in page |
| 20 | Switch FR / EN | Every dashboard page is translated |

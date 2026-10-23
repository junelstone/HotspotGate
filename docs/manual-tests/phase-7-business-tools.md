# Manual test — Phase 7: products, till, receipts, printing, reports

Run the **published** service on Windows and sign in to the dashboard as admin. Have a receipt printer ready if you
can (any ESC/POS 58 or 80 mm model: network, USB or Bluetooth). Without one, do the browser and PDF steps only.

## Products and till

| # | Step | Expected |
|---|------|----------|
| 1 | **Produits** → groups "Boissons", "Services". New product "Coca 33 cl", Boissons, 500, TVA 18,9, stock 3. "Impression", Services, 100, *Compter le stock* unticked | Both listed; Coca shows stock 3, Impression "Service, pas de stock" |
| 2 | **Livraison** 10 on Coca, then **Inventaire** 12 | "stock 13" then "stock 12". **Journal** shows both changes with your name |
| 3 | **Caisse**: tap "1 heure", Coca ×3, Impression ×2. Cash received 5 000 | Cart total = plan + 1 500 + 200; "À rendre" = 5 000 − total |
| 4 | **Encaisser** | Receipt page: one receipt number, 3 lines, "dont TVA 18,9 % 238 FCFA" (VAT inside 1 500), the Wi-Fi code with a QR. Coca stock is 9 |
| 5 | Scan the receipt's QR with a phone on the hotspot Wi-Fi | The portal opens with the code filled in; it logs in |
| 6 | Try to sell 10 Coca (stock 9) by tapping: the + button stops at 9 | — |
| 7 | Sign in as a cashier **without** an open shift, open **Caisse** | "Ouvrez d'abord votre caisse…", Encaisser disabled |
| 8 | Open the shift (1 000), sell 2 Coca paid with MTN MoMo, close the shift counting 1 000 | Shift report: cash expected 1 000, MTN MoMo 1 000, difference 0; the receipt is listed |
| 9 | **PDF** on the receipt page and on the shift page | PDFs open in Edge/Acrobat; accents (é, ç) correct; the receipt PDF is a narrow ticket |

## Receipt printer

| # | Step | Expected |
|---|------|----------|
| 10 | Network printer: print its self-test page (hold FEED while powering on) to read its IP. **Imprimante** → `IP:9100`, save, **Imprimer une page de test** | Page prints: name, header, digit ruler fills exactly one line, accents, bold, big "ABCD-1234", QR code, then the paper is cut |
| 11 | If the ruler wraps or is short, switch 58/80 mm. If accents print as odd signs, choose ASCII. If the QR prints as garbage or nothing, untick QR | Test page correct after the change |
| 12 | USB printer: install its driver (or "Generic / Text Only" on its USB port). **Imprimante** → type its exact Windows name (the list under the field shows the installed printers) → test page | Prints as in 10 |
| 13 | Bluetooth printer: pair it in Windows, then Bluetooth settings → More options → COM Ports: note the **Outgoing** port. **Imprimante** → `COM5:9600` (your port) → test page | Prints as in 10 (if nothing: try 115200 baud) |
| 14 | Tick "Imprimer le reçu automatiquement", make a sale | The receipt prints by itself within 2 s; the receipt page still opens |
| 15 | Switch the printer off and make a sale | The sale is saved; a red message says printing failed and why. Switch on, **Imprimer sur l'imprimante à tickets** → prints |
| 16 | Tick "Imprimer le rapport automatiquement à la fermeture", close a shift | The shift report prints: totals, each receipt with time and amount |

## Reports and audit log

| # | Step | Expected |
|---|------|----------|
| 17 | **Rapports** → each tab, this month | Sales by plan, sessions (peak hours), shifts and sales per staff, transactions, turnover by day; "Par mois" groups by month |
| 18 | **CSV (Excel)** on turnover, open in Excel | Columns split correctly (";"), accents correct, amounts are plain numbers that Excel can sum |
| 19 | **PDF** on transactions with many lines | Several A4 pages, column titles repeated, "1 / N" page numbers, footer "Imprimé le … par …" |
| 20 | A sale at 23:30 and one at 00:30 | Turnover counts them on two different days (local time) |
| 21 | **Journal**: search "Product" or a receipt number; filter by a cashier; CSV | Matching entries only |
| 22 | Sign in as a cashier: open `/admin/reports`, `/admin/products`, `/admin/audit`, `/admin/printer` | "Accès refusé" |

If a printer does not work: note its brand/model, connection, the exact setting typed, the message shown, and send
the latest `service-*.log` (it says which printer was tried and why it failed).

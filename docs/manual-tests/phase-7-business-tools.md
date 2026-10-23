# Manual test — Phase 7: products, till, receipts, printing, reports

Run the **published** service on Windows and sign in to the dashboard as admin. Have an ordinary A4 printer
installed in Windows. All printing is on A4.

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

## A4 printing

| # | Step | Expected |
|---|------|----------|
| 10 | **Paramètres** → receipt header "Avenue …, Brazzaville / Tél. …", footer "Merci !", save. Open a receipt | Header under the business name, footer at the bottom |
| 11 | On the receipt page, **Imprimer** (Ctrl+P) → your A4 printer | One A4 page: header, lines table, total, VAT, each Wi-Fi code as a dashed card with its QR; no menu or buttons printed |
| 12 | **PDF** on the receipt, open and print it | Same content; accents (é, ç) correct; the QR scans from the paper |
| 13 | A sale of 6 Wi-Fi codes | Cards 2 per row; a page break never cuts a card |
| 14 | **Vendre** (new codes) → ticket page → **Imprimer** | Codes as A4 cards, 3 per row, with the receipt summary under them |
| 15 | **Tickets** → a batch → **Imprimer (A4)** | 21 cards per A4 page, cut lines visible |
| 16 | **Ma caisse** → a closed shift → **PDF** and **Imprimer** | A4 report: summary, totals by type, every transaction |

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

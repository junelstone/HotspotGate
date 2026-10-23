# Phase 7 — Business tools (plan)

Goal: sell drinks, snacks, printing… together with Wi-Fi codes at one till, print receipts, close shifts with a
printed report whose totals match the transactions, and read the business from reports.

## Products and stock (Pro)

- Groups (Boissons, Snacks, Services…) and products: name, group, price (FCFA, VAT included), VAT rate, stock,
  **track stock** on/off (off for services like printing), active.
- Stock goes down at each sale; a sale above the stock is refused for tracked products. Admins add stock (delivery)
  or correct it (count) — every change is in the audit log.

## Till (point of sale, Pro)

One screen for products **and** Wi-Fi plans: tap items to fill the cart, choose cash / MTN MoMo / Airtel Money, type
the cash received to see the change, **Encaisser**. One receipt number for the whole sale; Wi-Fi codes are created
and sold in the same database transaction as the product lines (all or nothing). Cashiers need an open shift.

## Receipts

- Receipt page (HTML, print from the browser) with **PDF** download and **Imprimer** on the thermal printer.
- Thermal printing (ESC/POS) without knowing the model in advance — the settings choose how to reach it:
  | Connection | Setting |
  |---|---|
  | Windows printer (USB printers installed in Windows, "Generic / Text Only" driver works) | printer name |
  | Network printer | `IP:9100` |
  | Serial / Bluetooth (shows as a COM port) | `COM3:9600` |
  Paper 58 mm (32 characters) or 80 mm (48 characters); character set PC850 (French accents) or plain ASCII
  (accents removed) for printers that print garbage; QR codes for Wi-Fi codes on/off (some clones lack them);
  automatic printing after a sale and at shift close; a **test page** button.
- Wi-Fi codes on the receipt: plan, code in large print, QR to the portal.

## Shift close

Closing prints the shift report (thermal if configured, otherwise the browser page) and offers the PDF: opening cash,
cash taken, expected cash, counted, difference, totals by payment method and by type, every receipt. Totals are
computed from the same transactions the report lists.

## Reports (dates from/to; CSV and PDF for each)

| Report | Edition |
|---|---|
| Voucher sales by plan (count, amount) | Basic |
| Sessions: users, distinct devices, data used, average duration, by login method, **peak hours** (sessions active per hour of the day) | Basic |
| Cash per shift and per staff member | Pro |
| Transactions (filter by type, payment, staff) | Pro |
| Turnover by day or by month, by type | Pro |

Days are local time (Congo, UTC+1). PDF files are written by a small built-in PDF writer (A4, standard fonts,
French accents) — no third-party PDF library and no licence to manage.

## Audit log viewer (admin)

Who did what and when: filter by dates, staff member and text; CSV export.

## Change after review

The customer prints everything on **A4** with an ordinary printer: the thermal (ESC/POS) receipt printing, its
settings page and transports were removed. Receipts are an A4 page (browser print or PDF) with the Wi-Fi codes as
cards to cut out; voucher tickets are A4 cards only. Receipt header/footer texts moved to **Paramètres**.

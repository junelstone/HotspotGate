# Phase 5 — Admin dashboard (plan)

Goal: staff manage the hotspot from a browser on the PC or a phone on the Wi-Fi
(`http://192.168.137.1:8080/admin`). Done when a cashier logs in on a phone, sells a voucher, and sees it in their
shift.

## Sign-in and roles

- Cookie sign-in at `/admin/login` (BCrypt passwords from phase 1). Session ends after **2 hours without
  activity**. Wrong passwords: 5 tries per address, then 5 minutes wait.
- First login with a temporary password (the seeded `admin`, new staff, reset passwords) must change it first.
  When the `admin` sets their own password, `initial-admin-password.txt` is deleted.
- Every request re-checks that the account still exists and is enabled, so disabling a cashier logs them out.
- The localhost-only rule of phases 2–4 is replaced by sign-in: setup pages need the **Admin** role.

| Page | Admin | Cashier |
|---|---|---|
| Home (today's figures, connected devices) | ✔ | ✔ (own figures) |
| **Sell** codes (new codes for a plan, or a pre-printed card), print the ticket | ✔ | ✔ |
| **My shift** (open with starting cash, sales, close with counted cash) — Pro | ✔ | ✔ own only |
| User accounts: search, top up | ✔ | ✔ |
| User accounts: create, edit, reset password | ✔ | |
| **Live**: connected customers, time/data left, speed; add time, log out, block, whitelist | ✔ | |
| Plans (tariffs): create, edit, deactivate, delete unused | ✔ | |
| Voucher batches: create N codes, print A4 cards / 58 mm / 80 mm tickets with QR, CSV export, disable, delete unused | ✔ | |
| Staff (multiple accounts = Pro) | ✔ | |
| Settings: business name, Wi-Fi name, logo, colour, welcome page, terms FR/EN, default language, idle timeout | ✔ | |
| Backup (download / restore) | ✔ | |
| Network, devices, gate diagnostics (phases 2–3) | ✔ | |

## Printing

Print pages are plain HTML with print CSS (`@page`), opened in the browser and printed with Ctrl+P / the phone's
share → print. QR codes are inline SVG (QRCoder, MIT) and open `http://<gateway>/portal?code=XXXX-XXXX`.
Direct ESC/POS printing to a thermal printer comes with receipts in phase 7.

## Backup / restore

- Backup: SQLite online backup of the live database into `backups\stoneshotspot-YYYYMMDD-HHMMSS.db`, then download.
- Restore: upload a backup; it is checked (SQLite file with our tables), the current database is backed up first,
  then copied over with the SQLite backup API (no file swapping while the service runs) and migrated.
  Active sessions are restored as in a restart.

## Core additions

`SessionService.AddTimeAsync`, live status of all sessions, `VoucherService.SellNewAsync` (create + sell in one
step) and selling a pre-printed code, batch summaries, `AccountService.UpdateAsync`, `StaffService.SetEnabledAsync`,
shift transaction list, `BackupService`.

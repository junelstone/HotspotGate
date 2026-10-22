# Business rules (Core)

Implemented in `src/StonesHotspot.Core`, covered by `tests/StonesHotspot.Tests`.

## Tariffs

| Field | Meaning |
|---|---|
| Type | `TimeBased` (quota in minutes), `DataBased` (quota in MB, 1 MB = 1 048 576 bytes, upload + download), `Unlimited` |
| ValidityHours | Window that starts at first login. Optional |
| ValidUntil | Fixed last date. Optional. The earlier of the two ends the voucher |
| CountdownMode | `UsageOnly`: time runs only while a device is logged in. `WallClock`: time keeps running from first login |
| Download/Upload kbit/s | Speed limits (applied by the packet engine, phase 6). Empty = no limit |
| MaxDevices | Devices that may be online at the same time on one voucher/account |
| IdleTimeoutMinutes | Empty = default setting (10 minutes) |

An `Unlimited` tariff must have a validity or end date. A tariff that was ever used cannot be deleted, only
deactivated (hidden from sale, kept for history).

## Vouchers

- Codes: 8 characters from `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no O/0/I/1/L), typed in any case, with or
  without the dash (`ABCD-EFGH`). Batches of 1–1 000.
- Status: `New` → `Sold` (cashier sale, recorded as a transaction) → `Active` (first login) → `Expired`.
  `Disabled` by staff at any time. A `New` voucher can also be used directly (e.g. cards handed out without
  going through the till) — say if you want to forbid that.
- The validity window and quota start at **first login**, not at printing or sale.
- Single-use vouchers expire as soon as their session ends (logout, idle, …).
- Only never-sold, never-used vouchers can be deleted.

## Sessions

- One session per device (identified by MAC). Logging in with another code logs out the previous session.
  Logging in again with the same code on the same device reuses the session (new IP is recorded).
- Every tick (≈ 1 s) adds the bytes counted by the packet engine and the elapsed **whole seconds**.
- Time and data are taken **once per voucher/account**, however many of its devices are online.
- Ends: `Expired` (end date passed), `QuotaUsed` (no time/data left) — both end every device on that
  voucher/account; `Idle` (no traffic for the idle timeout) ends that device only; `Logout`; `KickedByStaff`
  (staff action, blocked device, disabled account).
- After a restart, active sessions continue; the time the service was down is not charged to usage-only
  tariffs (wall-clock tariffs keep running, by definition).

## Login protection

5 wrong codes or passwords from one device within 15 minutes → that device is locked for 5 minutes (the right
code is refused too). Other devices are not affected. Blocked devices can never log in.

## User accounts

- Balance in FCFA, filled by deposits (cash, MTN MoMo, Airtel Money).
- At login, if no package is running, one package of the account's tariff is bought from the balance; the
  balance may go down to −overdraft. When the package is used up while online, the next one is bought
  automatically; if the balance is too low, the session ends.
- Disabled or past their expiry date → refused (and active sessions end).

## Cash and shifts

- One open shift per staff member. Transactions by a staff member with an open shift belong to that shift;
  without a shift (e.g. Basic edition) they are still recorded, with no shift.
- Closing: expected cash = opening cash + sum of **cash** transactions of the shift; difference = counted −
  expected (negative = missing). Mobile Money and account-balance movements are reported separately.
- Cashiers can close only their own shift; admins can close any.
- Receipt numbers: `YYYY-NNNNNN`, sequential, restarting each year. Lines sold together share one number.

## Staff

- Passwords: BCrypt, at least 8 characters. New staff and reset passwords must be changed at next login.
- The first `admin` gets a random password on first start (see README → First login).
- Staff actions (tariffs, vouchers, sales, deposits, shifts, settings, kicks, device block/whitelist) are
  written to the audit log in the same database transaction as the change.

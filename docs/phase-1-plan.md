# Phase 1 — Data layer and business rules (plan)

## Where things go

Everything lives in `StonesHotspot.Core` (EF Core + SQLite are cross-platform, so Core stays free of Windows
dependencies and fully unit-testable):

```
Core/
  Data/
    HotspotDbContext.cs          DbSets, mappings, conventions (enums as text, UTC dates, Xaf as integer)
    DesignTimeDbContextFactory.cs  for `dotnet ef migrations add`
    Migrations/                  generated
    DatabaseInitializer.cs       migrate + WAL mode + seed
    Entities/                    Tariff, Voucher, UserAccount, Device, Session, Staff, Shift,
                                 Product, ProductGroup, Transaction, WebFilterRule, Setting, AuditLog,
                                 Allowance (owned: remaining time/data of a voucher or account)
  Rules/
    AllowanceRules.cs            pure functions: start, consume, check expiry/quota
    VoucherCodeGenerator.cs      8 chars, alphabet without O/0/I/1/L
    MacAddress.cs                normalise "aa-bb-…" → "AA:BB:CC:DD:EE:FF"
  Security/
    IPasswordHasher.cs, BcryptPasswordHasher.cs
  Services/
    TariffService, VoucherService, SessionService, AccountService, CashService,
    StaffService, DeviceService, SettingsService, AuditService, ReceiptNumbers
    LoginAttemptLimiter (5 wrong tries → 5-minute lockout, per device)
  BusinessRuleException.cs       carries an error code (localised later in the UI)
```

The Service applies migrations and seeds on start, and resumes active sessions (the downtime is not charged
to "usage only" tariffs).

## Key rules

| Rule | Implementation |
|---|---|
| Voucher activates at first login | `New`/`Sold` → `Active`, `FirstUsedAt` = now, allowance starts (validity window starts) |
| Time per second | `SessionService.Tick(now, traffic)` counts whole elapsed seconds since the last tick |
| Usage-only countdown | Time is taken from the voucher/account once per second while **at least one** of its devices is online (not once per device) |
| Wall-clock countdown | Time end = first login + quota; keeps running when logged out |
| Data per byte | Upload + download bytes reported by the packet engine (phase 3) are taken from the allowance. 1 MB = 1 048 576 bytes |
| Validity | Ends at `first login + ValidityHours` and/or the tariff's fixed `ValidUntil` date, whichever is first |
| End of session | Expired / QuotaUsed end **all** sessions of that voucher/account; Idle ends one session; Logout / KickedByStaff on request |
| Max devices | Login refused when the voucher/account already has `MaxDevices` active sessions on other devices; re-login from the same device reuses its session |
| Wrong codes | 5 wrong codes/passwords per device → locked 5 minutes (stored on the device, survives restarts) |
| Cash per shift | Expected cash = opening cash + sum of **cash** transactions in that shift; difference = counted − expected |
| Single-use voucher | Becomes `Expired` as soon as its session ends |

## User accounts (decision to confirm)

An account holds a **balance in FCFA** and a **tariff**. At login, if the account has no running package, one
package of its tariff is bought from the balance (balance may go down to −overdraft). When the package is used
up and the device is still online, the next one is bought automatically if the balance allows it, otherwise
the session ends. The account's own expiry date disables it entirely.

## Seed data

- Staff `admin`, role Admin, **random initial password** written once to
  `%ProgramData%\StonesHotspot\initial-admin-password.txt` (readable by Administrators only on Windows) and
  must be changed at first login. No shared default password, because customers on the LAN can reach the
  dashboard port.
- Tariffs (prices editable): 30 min — 100 FCFA, 1 h — 200 FCFA (usage-only, valid 7 days), Pass 24 h — 1 000 FCFA
  (unlimited for 24 h after first login).
- Settings: business name, default language `fr`, default idle timeout 10 minutes.

## Tests

Voucher activation, time countdown (usage-only and wall-clock), data countdown, validity expiry, idle timeout,
max devices, rate limiting, shift cash totals, plus code alphabet/uniqueness, tariff validation, account
deposit + automatic package purchase, migrations apply on a fresh database.

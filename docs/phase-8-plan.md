# Phase 8 — Free access and SMS login (plan)

Goal: besides paid codes and accounts, the portal can offer three ways in, each switched on separately with its
own plan (tariff) and its own limit of logins per device per day:

| Mode | Customer does | Typical use |
|---|---|---|
| **Free access** | ticks "I accept the terms" → **Connect** | 15 free minutes to attract customers |
| **Shared password** | types today's password (shown to staff on the dashboard home) | customers of the bar/restaurant |
| **SMS code** | types a phone number (+242…), receives a 6-digit code by SMS, types it | identify users by phone number |

All three are Pro features (`FreeAccessModes`, `SmsLogin`), included in the trial.

## How a free login works

A free login creates a hidden voucher for the device (source = free / password / SMS) on the mode's plan and
logs the device in with it. Time, data, validity, speed limits and the status-page countdown then work exactly as
for paid codes, with no new accounting code. These vouchers never appear in batches or sales reports (no money).

- **Logins per device per day** (local day): each new free voucher counts as one. Logging out and in again while
  the free voucher still has time does not use a new one.
- Blocked devices are refused; wrong passwords and wrong SMS codes count towards the existing lock-out (5 errors →
  5 minutes).
- The sessions report already counts sessions by login method (Free, Password, SMS).

## Shared password

Admin sets it in the settings page, or ticks **"New password every day"**: the password of the day is then
derived from a secret and the date (6 characters, easy to read: no 0/O/1/I), so it changes at midnight without
any job running. The dashboard home shows today's password to every staff member.

## SMS code

1. Phone number: Congo format, `06 123 45 67`, `+242 06 123 45 67`, `242061234567` → `+242061234567`. Only numbers
   starting with the **allowed prefixes** (setting, e.g. `+24206, +24205, +24204`) are accepted.
2. Limits before sending (settings): per number per day, per device per day, total per day (protects the SMS
   budget), and at least 60 s between two codes for the same number.
3. 6-digit code, valid 10 minutes, at most 5 attempts; stored hashed, never logged. SMS text (FR/EN, the
   customer's language): "Stone's Hotspot : votre code Wi-Fi est 123456. Valable 10 min."
4. Correct code → login on the SMS plan. The phone number is kept with the voucher/session (who used the hotspot).

### Sending SMS — two ways, chosen in the settings

| Way | Setting | How |
|---|---|---|
| **GSM/USB modem** (a SIM card in a USB key) | COM port and speed | AT commands: `AT`, `AT+CPIN?` (the SIM PIN must be disabled), `AT+CMGF=1` (text mode), `AT+CMGS="+242…"`, text, Ctrl-Z, wait for `+CMGS`. One SMS at a time. |
| **HTTP SMS provider** | URL template (+ optional POST body) | e.g. `https://api.example.com/send?key=XXX&to={phone}&text={message}`; success = HTTP 2xx (and optional "success text" in the answer). `{phone}` = +242…, `{phone_digits}` = 242…, `{message}` URL-encoded (JSON-escaped in a JSON body). |

The provider URL (which contains the API key) is **stored encrypted** (Data Protection, DPAPI on Windows), shown
masked in the dashboard and never written to logs. A **test SMS** button sends a message to a number the admin
types. Every SMS sent is recorded (number, purpose, time, success/failure — not the code) for the limits and an
**SMS log** on the settings page; Phase 9 reuses the same sender to send codes bought with Mobile Money.

## Settings page `/admin/access` (admin, "Accès gratuit")

Per mode: on/off, plan, logins per device per day. Password: value or "new every day". SMS: limits, prefixes,
sending way and its settings, test SMS, SMS log (last 50). Plans used for free access are normal plans (create
e.g. "Gratuit 15 min" in **Forfaits**; price is ignored).

## Social login (Google / Facebook)

Not built. `docs/social-login.md` explains what it needs (developer accounts, a public HTTPS domain, letting the
login sites through the gate, privacy policy) so the owner can decide.

## Tests

Phone normalisation and prefixes; daily password stability and change at local midnight; free login creates one
voucher per device per day and reuses it; per-day limit; SMS limits (number, device, total, cooldown); code
expiry and attempts; code hashing; modem AT conversation against a fake modem (success, `ERROR`, SIM PIN asked,
timeout); HTTP provider with a fake server (GET template, POST JSON body, failure); provider URL stored encrypted;
portal end-to-end for each mode; Basic edition hides the modes.

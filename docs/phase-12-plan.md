# Phase 12 — Hardening and final testing (plan)

Goal: close the remaining security gaps, keep personal data only as long as the owner decides, prove the hotspot
holds 50 devices, and hand over the final checklist and the user manuals.

## Already in place (checked again)

| Requirement | Where |
|---|---|
| Password hashing | BCrypt (work factor 11) for staff and customer accounts; SMS codes stored as hashes |
| Admin session timeout | Cookie sign-in ends after 2 h without activity; disabled staff signed out at the next click |
| CSRF | Razor Pages anti-forgery on every form; the only exempt endpoint is the Mobile Money callback, which only triggers a re-check with our own keys |
| Login limits | Staff: 5 wrong passwords per address → 5 min. Portal: 5 wrong codes per device → 5 min |
| Secrets | Keys and provider URLs encrypted (Data Protection), audit log says "(secret changed)", phone numbers masked in logs |

## New in this phase

1. **Dashboard only from the PC or the customer network.** `/admin`, `/setup` (and their posts) answer 404 to any
   address that is not loopback or inside the LAN adapter's subnet (192.168.137.0/24 by default), whatever the
   firewall says. Extra networks can be allowed with `StonesHotspot:AdminNetworks` (e.g. the shop's office LAN).
2. **Rate limiting on every login form**, on top of the per-device/per-address locks (a phone changing its MAC
   address gets no new tries): sign-in, change password, wizard, portal code / account / password of the day / free
   access / SMS send and check, Mobile Money start. Per address: 10 posts per minute; all customers together: 300
   per minute. Over the limit → 429 page "Trop d'essais, réessayez dans une minute".
3. **Secrets never in logs or the audit log**: setting keys that hold secrets are always recorded as "(secret
   changed)", whatever the caller says; a test scans every log call of the source for passwords, codes, keys and
   tokens.
4. **Security headers** on every page: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
   `Referrer-Policy: no-referrer`, a Content-Security-Policy suited to our pages (no outside scripts), no `Server`
   header.
5. **Retention of session logs** (who / when / MAC / IP / phone). Setting "Conserver les journaux de connexion"
   (days, default 365, 30–3650) in **Paramètres**. Every night, for records older than that: sessions lose their IP;
   devices not seen since then (and not whitelisted, blocked or annotated) get their MAC, IP and name replaced by
   `ANON-<n>`; SMS records, SMS-issued codes and Mobile Money payments keep only a masked phone number
   (`+24206****567`); used SMS challenges are deleted. Amounts, plans, dates, receipts and reports stay — sales and
   cash are never deleted. Recorded in the audit log. The owner checks the duration required by Congolese law.
6. **Load test, 50 devices** (automated): 50 phones log in at the same time through the portal, then the engine
   accounts traffic for all of them; packet processing throughput and tick time measured and checked against
   generous limits (numbers printed in the test output).
7. **Final manual checklist** `docs/manual-tests/phase-12-final.md`: Android, iPhone, Windows and Mac laptops, PC
   reboot, internet lost and back, access point unplugged, power cut during a session, time running out
   mid-download, 50-device afternoon.
8. **User manuals** `docs/manual-fr.md` (French, for the shop) and `docs/manual-en.md`.

## Tests

Dashboard refused from the WAN and from unknown networks, allowed from loopback, LAN and configured networks; rate
limit returns 429 after 10 posts and resets; secret settings never show their value in the audit log; source scan
for secrets in log calls; security headers present; retention anonymises exactly what is older than the setting and
keeps money records; 50-device load test.

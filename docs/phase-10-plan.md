# Phase 10 — Licensing, trial and activation (plan)

Goal: the hotspot runs 30 days as a Pro trial, then needs a licence code issued by the owner (Stone's IT
Consulting) for this PC. Codes are signed, tied to the PC, sent by WhatsApp/SMS/e-mail, work offline, and can be
renewed or revoked through a small licence server. `docs/licensing.md` says honestly what this protects and what not.

## Pieces

| Piece | Where | Shipped to customers? |
|---|---|---|
| Licence checking (verify signature, Machine ID, trial, clock, grace, online check-in) | `src/StonesHotspot.Licensing` | yes (public keys only) |
| Licence issuing (owner key vault, signing, licence database, CSV, backup, server sync) | `tools/StonesHotspot.LicenseIssuer` (library) | **never** |
| LicenseManager desktop app (Windows Forms over the issuer) | `tools/StonesHotspot.LicenseManager` | **never** |
| Licence server (trial registration, activation, daily check-in, renewals, revocations, owner page) | `src/StonesHotspot.LicenseServer` | runs on the owner's server |

## Cryptography

- **Ed25519** signatures (BouncyCastle, MIT — .NET has no built-in Ed25519).
- **Owner licence key**: created by LicenseManager on first run, private half encrypted with the owner's password
  (PBKDF2-SHA256 600 000 rounds + AES-256-GCM), with a forced encrypted backup before anything else. Only the public
  half is compiled into the hotspot (`src/StonesHotspot.Licensing/Keys/licence-public.txt`).
- **Server key**: a second owner key pair kept on the licence server to sign its answers (check-in, renewals). It
  cannot create licences: if the server were hacked, nobody could make new codes. Its public half is also compiled
  into the hotspot. (Interpretation of "responses signed with the owner's key": the owner's *server* key.)
- Until the owner creates the real keys, the repository holds **development** public keys (their private halves
  are not in Git); a release build refuses development keys (Phase 11 build check).

## Licence code

`SHS1-` + base32 groups of 5 characters (no 0/O/1/I) of: format version, licence id, edition, issue date, expiry,
Machine ID, customer name (short) + 64-byte signature — about 40 groups, copy-paste friendly. Also as a `.lic`
file (same text) and a QR code. Any changed character → signature fails.

## Machine ID

`MID-XXXX-XXXX-XXXX`: four 15-bit hashes of Windows MachineGuid, board/BIOS serial, system-disk serial and CPU id
(salted). A licence matches when **at least 3 of the 4** still match, so changing one part (new disk) keeps the
licence; a different PC does not match. Moving a licence to another PC = revoke + new code.

## Editions and time

- Trial: Pro features for 30 days from first install. Basic / Pro from the licence. Feature table unchanged
  (`EditionFeatures`).
- Licence: 1 year or custom days; early renewal extends from the current expiry (done by LicenseManager).
- Warnings 30/14/7/3/1 days before the end: dashboard banner (and tray via `/health`).
- After the end: **7 days of grace** (red banner, everything works), then the hotspot **stops**: customers see
  "Service temporairement indisponible", nobody gets internet, the dashboard only offers activation, reports and
  backup. Data is never deleted or locked. A valid code works at once.
- Revoked by the owner (seen at check-in): grace of 7 days from the revocation, then stop.

## Trial and clock protection

- First-run record `{machine id, first run, latest time seen}` HMAC-sealed and stored in **two** places: a
  DPAPI-protected file in `%ProgramData%\StonesHotspot\.trial` and the registry
  (`HKLM\SOFTWARE\StonesHotspot\Trial`); both survive uninstall/reinstall. The earliest valid first-run date wins; a
  record from another machine is ignored.
- Latest time seen is updated every hour. If the clock goes back more than 24 h: warning on the dashboard and all
  durations count from the latest time seen (moving the clock back does not extend anything).
- Online: the licence server's signed time (and HTTPS `Date`) moves "now" forward if the PC is behind. The trial is
  registered on the server at first start (best effort).

## Online check-in

Daily (and at activation): `POST /api/v1/checkin {licenceId, machineId, version}` → signed answer `valid`,
`revoked (date)`, or `renewed (new code)`, plus server time. Unreachable → retry in 1 h; the licence keeps working
offline until its expiry. Endpoints: `trial`, `activate`, `checkin`, `renewal/{licenceId}`; owner-only
`/admin` page (password) and `/api/v1/manager/*` (token) used by LicenseManager to upload licences and revocations.

## Hotspot dashboard

**Licence** page (all staff can see it, admins activate): Machine ID with **Copier** and **Envoyer par WhatsApp**
(pre-filled message to the owner's number), code box, `.lic` import, current edition, expiry, days left, last
check-in. Banners in the layout for warnings, grace, clock problem, stopped.

## Tests (fake clock)

(1) code for another PC rejected, (2) changed or forged code rejected, (3) Basic hides Pro features, (4) trial →
warnings → expiry → 7-day grace → stop, (5) reinstall keeps the trial, (6) clock moved back does not extend the
trial, (7) revoked licence stops after grace, (8) offline works until expiry; plus code format round trip, Machine
ID 3-of-4 matching, key vault (wrong password, tampered file), renewal from current expiry, server endpoints and
signed answers, activation page.

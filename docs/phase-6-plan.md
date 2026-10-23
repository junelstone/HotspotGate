# Phase 6 — Bandwidth control and web filter (plan)

Goal: each customer gets the speed of their plan (a phone speed test shows the plan's limit ±15 %), the
connection can be shared fairly between active customers, the WAN speed can be measured, and staff manage blocked
sites, free-access sites and MAC white/black lists from one page.

## Speed limits (in the packet gate)

- Every authorised device gets a download and an upload limit each second:
  `limit = min(plan limit, fair share)` (either may be absent = no limit).
- **Shaping, not just dropping.** Each device/direction has a GCRA token bucket (a "theoretical arrival time"):
  a packet that is within the rate passes at once; a packet slightly over is **delayed** (kept in memory and
  re-injected when its turn comes); a packet that would wait more than 400 ms is dropped so TCP slows down. This
  gives TCP a steady rate close to the limit instead of the saw-tooth of pure dropping. Burst allowance: 20 ms.
- A delay line (one thread, ordered by release time) re-injects delayed packets on the handle they came from; on
  Windows the timer resolution is raised to 1 ms while the gate runs.
- The plan's limit is read at every tick, so editing a plan changes running sessions within a second.

## Fair share (optional, Pro and Basic)

Settings: on/off, total download and upload of the internet line (kbit/s — the WAN speed test can fill them in),
minimum and maximum per device. Each second: `share = clamp(total / active devices, min, max)`, where "active" =
authorised devices that sent or received traffic in the last 10 s (at least 1).

## WAN speed test

Dashboard button: downloads from `speed.cloudflare.com` with 4 parallel streams for ~8 s, then uploads for ~6 s
(the PC's own traffic, not filtered), shows Mbit/s and offers to use the result as the fair-share totals. The test
URLs are configurable in `appsettings.json` (`Bandwidth:TestDownloadUrl` / `TestUploadUrl`).

## Web filter page (`/admin/filter`, admin)

| Section | Content |
|---|---|
| Blocked sites | domains / IPs / ranges, with category; add, remove; **ready-made lists** "Réseaux sociaux" and "Contenus adultes" added in one click, then editable like any rule |
| Family DNS | option: resolve customers' DNS through Cloudflare for Families (1.1.1.3 / 1.0.0.3), which blocks adult and malware sites far beyond any list we can keep |
| Free access (walled garden) | domains / IPs reachable without login (e.g. MTN MoMo, the business's website) |
| MAC whitelist | devices always free (staff phones, the cash PC); add by MAC with a note, remove |
| MAC blacklist | devices never allowed; add, remove |

The test rules section of the gate diagnostics page moves here. WhatsApp is deliberately **not** in the social
networks list (it is how customers communicate in Congo); it can be added by hand.

## Tests

GCRA shaper over 10 simulated seconds achieves the limit within 2 %; delayed vs dropped packets; limits follow the
plan and fair share; active-device counting; delay line releases in order; speed test with a fake HTTP server;
ready-made lists added once; family DNS uses the family servers; filter page actions; bandwidth page saves.

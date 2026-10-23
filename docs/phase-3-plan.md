# Phase 3 — Packet gate (WinDivert) and DNS (plan)

Goal: devices on the customer Wi-Fi have no internet until they are authorised; any web page they open lands on
the portal; phones show "Sign in to network" by themselves; authorising/revoking a device takes effect within
2 seconds; bytes are counted per device.

## Where packets go (ICS mode)

```
phone 192.168.137.x ──LAN──► PC (gateway 192.168.137.1) ──ICS NAT──► WAN ──► internet
        │                          │
        │  to the internet         │  to the PC itself (DNS, portal :80, dashboard :8080)
        ▼                          ▼
  WinDivert FORWARD layer     WinDivert NETWORK layer (LAN interface only)
```

Two WinDivert handles, both limited to the LAN subnet so **the PC's own traffic is never touched**:

| Handle | Layer | Filter (simplified) | Purpose |
|---|---|---|---|
| Forward | `NETWORK_FORWARD` | `ip.SrcAddr` or `ip.DstAddr` in the LAN subnet | everything between customers and the internet |
| Local | `NETWORK` | LAN interface: inbound UDP 53 to the gateway, outbound TCP from the portal port | DNS answers, reverse half of the HTTP redirect; also used to inject packets |

## Rules (section 5 of the brief)

| Traffic from a customer device | Not logged in | Logged in / whitelisted |
|---|---|---|
| DHCP, broadcast, multicast | pass | pass |
| Anything to the gateway itself (portal 80, dashboard 8080, ping) | pass | pass |
| DNS (UDP 53) to the gateway **or any other server** | answered by our DNS responder | answered by our DNS responder |
| TCP 80 to the internet | **redirected to the portal** (see below) | pass, counted |
| Free-access (walled-garden) IPs, incl. IPs learnt from free-access domains | pass | pass, counted |
| Blocked IPs (web filter) | refused | refused |
| Other TCP (HTTPS…) | refused with a TCP reset (fails at once instead of hanging) | pass, counted |
| Other UDP / ICMP | dropped | pass, counted |
| Replies from the internet | dropped | pass, counted (download) |

**HTTP redirect.** A customer's TCP 80 packet to any internet address is rewritten to the gateway's portal port
and injected locally; the portal's replies are rewritten back to the original address (a small destination-NAT
table keyed by client IP + port). The portal sees the original `Host:` and path and answers
`302 → http://192.168.137.1/portal?mac=…&url=<original URL>` with `Connection: close`. This works for cached DNS
answers and literal IPs too, and it is what triggers the OS "Sign in" pop-ups (Android `generate_204`, Apple
`hotspot-detect.html`, Windows `connecttest.txt`, Firefox `detectportal`): before login they get a 302 instead of
their "success" answer; after login their traffic is no longer touched, so the pop-up closes.

**DNS responder** (instead of a socket on port 53, which Windows' ICS DNS proxy already owns): queries are taken
out of the packet flow, resolved with the WAN's DNS servers (fallback 1.1.1.1 / 8.8.8.8), and the answer is
injected back to the device. Blocked domains get the gateway's address (HTTP then shows "Site bloqué / Site
blocked"; HTTPS just fails). Answers for free-access domains teach the gate their IPs (kept for the record's
TTL, at least 5 minutes). Real answers are given before login too, so nothing stale is cached after login.

## Access table and session engine

- `HotspotEngineService` (Service), every second:
  1. takes the per-IP byte counters from the gate and calls `SessionService.TickAsync` (time/data/idle);
  2. rebuilds the access table: MACs with an active session or whitelisted (never blocked ones) → their current
     IPs from the neighbour (ARP) table (+ the session's last IP if no other MAC claims it). **MAC is the
     identity**, so a device that changes IP keeps its access;
  3. every 10 s reloads the web-filter rules.
- The gate swaps in the new snapshot atomically; revoked devices are cut off at the next packet (≤ 1–2 s).

## Fail-closed behaviour

- The gate is started **before** ICS is switched on; if WinDivert cannot start (missing files, not admin,
  driver blocked by antivirus/Secure Boot), ICS is **switched off** and the error is shown — never an open LAN.
- When the service stops normally it switches ICS off. After a crash, ICS stays on without the gate: the LAN is
  open until the service restarts. Phase 11 sets the service to restart immediately on failure. With Windows
  Mobile hotspot mode the service cannot switch the hotspot off, so a stop leaves it open: documented.
- The PC's own internet is never filtered (only the LAN subnet is captured).

## Code

- `Network/Gate/Packets`: IPv4/TCP/UDP parsing, rewriting, checksums, TCP reset and UDP reply builders (pure C#,
  unit-tested on Linux).
- `Network/Gate/PacketProcessor`: the rules above as a pure function: packet in → actions out (reinject,
  inject, drop, DNS query). `GateSnapshot`, `IpSet` (CIDRs + learnt IPs with expiry), DNAT flow table, counters.
- `Network/Dns`: DNS message parsing/building, `DnsPolicy` (blocked / free-access matching), `DnsResponder`
  with an upstream resolver.
- `Network/Gate/WinDivert`: P/Invoke to WinDivert 2.2.2 and the two receive loops (Windows only).
- `Network/Gate/AccessTableBuilder`: sessions + whitelist + neighbour table → snapshot (pure, tested).
- `Network/Gate/ClientIdentityResolver`: client IP → MAC for the portal.
- `Web`: portal redirect middleware, minimal `/portal` page (full login in phase 4), blocked-site page,
  debug page `/admin/gate` (gate status, counters, authorise/revoke a device, add/remove a blocked or
  free-access domain) — localhost only.
- WinDivert 2.2.2 (LGPLv3) is downloaded by `build.ps1` from the official GitHub release, checked against a
  pinned SHA-256, and copied next to the service. Not committed to Git.

## Configuration

`Kestrel:Endpoints:Portal` = `http://0.0.0.0:80` (customers), `Admin` = `:8080`. In Development the portal uses
`http://localhost:8081`.

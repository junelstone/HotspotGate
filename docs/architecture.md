# Architecture

How the packet gate, the portal, the sessions and (later) payments fit together. Kept up to date phase by phase.

## Processes

| Process | Runs as | Role |
|---|---|---|
| `StonesHotspot.Service` | Windows service (LocalSystem) | Everything below: database, network, gate, DNS, portal (port 80), dashboard (port 8080) |
| `StonesHotspot.Tray` | the logged-in user | Shows the service state, Start/Stop, opens the dashboard |

## Network path

```
 phone ── Wi-Fi AP (bridge) ── LAN card 192.168.137.1 ─┐
                                                        │   Windows ICS (NAT) ── WAN card ── internet
                                      ┌─────────────────┴───────────────────────┐
                                      │ StonesHotspot.Service                    │
                                      │  WinDivert FORWARD handle  (customer ↔   │
                                      │    internet: allow / count / redirect /  │
                                      │    reset / drop)                         │
                                      │  WinDivert NETWORK handle  (LAN: DNS to  │
                                      │    the gateway, portal replies, inject)  │
                                      │  DNS responder → WAN DNS servers         │
                                      │  Kestrel :80 portal  :8080 dashboard     │
                                      └──────────────────────────────────────────┘
```

- **ICS** (`Network/Ics`) does addressing (DHCP 192.168.137.x) and NAT. Configured through the `HNetCfg.HNetShare`
  COM object.
- **The packet gate** (`Network/Gate`) only captures the customer subnet; the PC's own traffic never goes through
  it. `PacketProcessor` is a pure function (packet → verdict) holding an immutable `GateSnapshot`; the WinDivert
  loops (`Gate/WinDivert`) just apply the verdicts.
- **HTTP redirect**: a not-logged-in device's TCP 80 packet is rewritten to the gateway's portal port and injected
  locally; replies are rewritten back (small destination-NAT table). The portal middleware
  (`Web/CaptivePortalMiddleware`) sees the original `Host:` and answers `302 → /portal?mac=…&url=…`, which is what
  makes phones show "Sign in to network".
- **DNS**: every customer DNS query (to the gateway or any server) is taken out of the flow and answered by
  `DnsResponder`: blocked domains → gateway address ("site blocked" page), otherwise the real answer from the
  WAN's DNS servers; answers for free-access domains teach the gate their IPs.

## Sessions and the access table

```
 portal login ─► SessionService (Core) ─► database (sessions, devices, vouchers…)
                                              │
 HotspotEngineService, every second:          ▼
   1. byte counters from the gate ─► SessionService.TickAsync (time, data, idle, expiry → end sessions)
   2. active sessions + whitelist − blocked, by MAC ─► neighbour (ARP) table ─► IPs ─► new GateSnapshot
 every 5 s: NetworkSetupService.Reconcile (gate first, then ICS; ICS off if the gate cannot run)
 every 10 s: web-filter rules reloaded
```

MAC is the identity of a device; its IPs are looked up in the neighbour table at every tick, so an IP change does
not cut the customer off, and a revoked device is cut off at the next tick (≤ 1–2 s).

## Portal flow

```
 phone opens any http page ─► gate rewrites it to the portal ─► 302 /portal?mac=…&url=…
 /portal: code or account ─► SessionService.LoginWith… (by MAC from the ARP table)
        ─► AccessRefreshSignal wakes the engine ─► device in the access table within milliseconds
        ─► /portal/status (countdown polled every second, logout) ─► forward to welcome page / original page
 time/data/validity/idle runs out ─► engine ends the session ─► gate drops the device ─► status page returns to
 /portal, which explains why (last session ended < 15 min ago)
```

## Dashboard

Razor Pages under `Web/Pages/Admin`, cookie sign-in (`Web/Auth`): every request re-checks that the staff account
is still enabled; roles Admin/Cashier map to the policies `Admin`/`Staff`; temporary passwords force the
change-password page (`ForcePasswordChangeFilter`). Pro features (shifts, extra staff) ask `ILicenseService`.
Data Protection keys (cookies, form tokens) live in `%ProgramData%\StonesHotspot\keys`, DPAPI-protected on
Windows. Printing is HTML + print CSS with inline-SVG QR codes (QRCoder).

## Fail-closed rules

- Gate started before ICS; gate failure → ICS switched off.
- Service stop → ICS switched off, gate closed.
- Crash → ICS stays on without filter until the service restarts (phase 11: automatic restart on failure). With
  Windows Mobile hotspot mode the service cannot switch the hotspot off.
- Licence stopped (phase 10) → empty access table: nobody passes, the portal shows "service unavailable".

## Code map

| Project | Main parts |
|---|---|
| Core | entities + EF Core (`Data`), rules (`Rules`), services (`Services`: sessions, vouchers, accounts, cash, web filter…) |
| Network | adapters, ICS, neighbour table, setup/reconcile, gate (`Gate`), DNS (`Dns`) |
| Web | Razor Pages: portal (`Pages/Portal`), dashboard (`Pages/Admin`), captive-portal middleware |
| Service | host, `HotspotEngineService`, `DeviceDiscoveryService`, Kestrel ports, logging |
| Licensing | `ILicenseService` (stub until phase 10) |

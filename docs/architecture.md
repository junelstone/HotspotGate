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

## Free access and SMS login

```
 portal "Accès gratuit" / password / SMS code ─► FreeAccessService.GrantAsync
     ├─ device still has a valid free voucher of this mode ─► log in with it
     └─ else, under the daily limit ─► VoucherService.CreateForDeviceAsync (hidden: Source = Free/SharedPassword/Sms)
                                       ─► SessionService voucher login (Method = Free/SharedPassword/Sms)
 SMS: SmsLoginService (number rules, limits, code hash, 10 min) ─► SmsService (log) ─► ISmsTransport
          ├─ ModemSmsTransport (Network, COM port) ─► AtModem (AT+CMGF=1, AT+CMGS…)
          └─ HttpSmsTransport (URL/body template)
```

Secret settings (the provider URL with its API key) go through `ISecretProtector` (ASP.NET Data Protection,
DPAPI-protected keys on Windows).

## Mobile Money

```
 portal /portal/pay ─► MobileMoneyService.StartAsync ─► MobilePayment (Pending, our reference)
                                                     └─► IPaymentProvider.RequestToPayAsync (MTN / Airtel HTTP API)
 checks: waiting page (every 4 s) │ PaymentWatcherService (every 5 s) │ /api/payments/{mtn|airtel} notification
     └─► MobileMoneyService.CheckAsync ─► provider.GetStatusAsync (our keys; at most every 3 s per payment)
           ├─ paid   ─► claim row (Pending/TimedOut → Processing, one conditional UPDATE) ─► one DB transaction:
           │            hidden voucher (Source = MobileMoney) + sale (VoucherSale, MTN MoMo / Airtel Money,
           │            operator transaction id) ─► device login ─► SMS with the code
           ├─ failed ─► Failed (reason)
           └─ pending past the time limit ─► TimedOut (can still turn paid within 24 h)
```

Operator access tokens are cached until they expire (`TokenCache`). Keys go through `ISecretProtector`.

## Speed limits

Per authorised device and direction, `PacketProcessor.Shape` runs a GCRA token bucket (theoretical arrival time,
20 ms burst): within rate → pass; up to 400 ms over → verdict `Delay`, the WinDivert gate copies the packet into a
`PacketDelayLine` (one thread, release-time order, 1 ms timer resolution) and re-injects it on its own handle; beyond
→ drop. Limits come from the session's plan, lowered by `BandwidthPolicy` (fair share over devices active in the
last 10 s). The engine rebuilds them every second, and at once when settings change (`AccessRefreshSignal`).

## Dashboard

Razor Pages under `Web/Pages/Admin`, cookie sign-in (`Web/Auth`): every request re-checks that the staff account
is still enabled; roles Admin/Cashier map to the policies `Admin`/`Staff`; temporary passwords force the
change-password page (`ForcePasswordChangeFilter`). Pro features (shifts, extra staff) ask `ILicenseService`.
Data Protection keys (cookies, form tokens) live in `%ProgramData%\StonesHotspot\keys`, DPAPI-protected on
Windows. Printing is HTML + print CSS with inline-SVG QR codes (QRCoder).

## Sales, receipts and printing

```
 till cart ─► SalesService.CheckoutAsync (one DB transaction: stock −, codes created + sold, one receipt number)
 receipt number ─► SalesService.GetReceiptAsync ─► Receipt (lines merged, VAT included per rate, Wi-Fi codes)
     ├─ Receipt page (HTML, A4 print CSS)
     └─ ReceiptLayouts.ReceiptPdf (A4 PDF)
 reports ─► ReportService (typed rows, local days) ─► ReportTable (display + raw CSV value per cell)
     ─► HTML table │ ReportExport.Csv (";", UTF-8 BOM) │ ReportExport.Pdf (Core/Documents/PdfDocument)
```

- `Core/Documents/PdfDocument` writes PDF 1.4 itself: standard Helvetica fonts with WinAnsi encoding (French
  accents), Flate-compressed pages, text/lines/rectangles; no third-party PDF library.
- All printing is A4 (decided with the customer): browser print or PDF, no receipt-printer driver to support.

## Fail-closed rules

- Gate started before ICS; gate failure → ICS switched off.
- Service stop → ICS switched off, gate closed.
- Crash → ICS stays on without filter until the service restarts (phase 11: automatic restart on failure). With
  Windows Mobile hotspot mode the service cannot switch the hotspot off.
- Licence stopped (phase 10) → empty access table: nobody passes, the portal shows "service unavailable".

## Code map

| Project | Main parts |
|---|---|
| Core | entities + EF Core (`Data`), rules (`Rules`), services (`Services`: sessions, vouchers, accounts, cash, sales, products, reports, web filter…), PDF/CSV (`Documents`) |
| Network | adapters, ICS, neighbour table, setup/reconcile, gate (`Gate`), DNS (`Dns`) |
| Web | Razor Pages: portal (`Pages/Portal`), dashboard (`Pages/Admin`), captive-portal middleware, receipt/report layouts (`Documents`) |
| Service | host, `HotspotEngineService`, `DeviceDiscoveryService`, Kestrel ports, logging |
| Licensing | `ILicenseService` (stub until phase 10) |

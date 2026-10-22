# Phase 2 — Network setup (plan)

Goal: the admin picks the WAN and LAN adapters, the service shares the internet to the LAN with Windows
Internet Connection Sharing (ICS), and devices that join the access point appear in a "Connected devices"
list. No filtering yet: the internet is still open to everyone on the LAN.

## Network project (`StonesHotspot.Network`)

Everything Windows-specific sits behind a small interface so the rules can be unit-tested on any OS.

| Piece | Windows implementation | Testable logic |
|---|---|---|
| `INetworkAdapterProvider` | `System.Net.NetworkInformation` (adapters, MAC, IPv4, gateway, up/down) | — |
| `AdapterAdvisor` | — | suggests WAN (up, has a default gateway, prefers Ethernet) and LAN candidates; flags the Mobile Hotspot virtual adapter |
| `NetworkSetupValidator` | — | errors/warnings: not admin, no LAN, WAN = LAN, LAN down, WAN without internet, WAN already on 192.168.137.0/24 (IP conflict with ICS) |
| `IIcsController` | COM `HNetCfg.HNetShare` (late-bound): enable public = WAN, private = LAN; disable; read state. Also turns on Windows' "keep ICS after reboot" setting and sets the ICS service to automatic | — |
| `INeighborTable` | `GetIpNetTable2` (iphlpapi.dll) | parser of the raw table bytes |
| `IcsHostsFile` | reads `C:\Windows\System32\drivers\etc\hosts.ics` (hostnames ICS's DHCP gave out) | parser |
| `ConnectedDeviceScanner` | — | keeps LAN-interface entries, drops broadcast/multicast and the gateway, adds hostnames; detects an IP conflict on the gateway address and devices outside the LAN subnet (access point in router mode) |
| `NetworkSetupService` | — | validate → apply ICS → save settings + audit; `EnsureAppliedAsync` at service start |

## Service

- `NetworkStartupService`: on start, re-applies ICS if it was configured and Windows lost it (known ICS
  problem after reboot or Windows updates).
- `DeviceDiscoveryService`: every 10 s reads the neighbour table on the LAN adapter, keeps the current list in
  memory and updates the devices table (last IP, hostname, last seen).

## Temporary admin pages (until the real dashboard in phase 5)

Razor Pages in `StonesHotspot.Web` (this becomes the base of the dashboard):

- `/admin` — home with links.
- `/admin/network` — status (admin rights, ICS state, LAN gateway IP), adapter list with suggested roles,
  WAN/LAN selection, "Share the internet" / "Stop sharing" buttons, clear error messages (FR/EN).
- `/admin/devices` — connected devices: MAC, IP, hostname, "private/random MAC" marker, last seen; refreshes
  every 5 s; note about random MACs.

There is no staff login until phase 5, so **these pages answer only on the PC itself** (localhost); from any
other address they return 403. Forms use anti-forgery tokens.

## Mode B (static IP + built-in DHCP server)

Not built in this phase — see the question in the phase report: Windows only does NAT (sharing the internet)
through ICS, which brings its own DHCP server, so Mode B needs a separate NAT solution first.

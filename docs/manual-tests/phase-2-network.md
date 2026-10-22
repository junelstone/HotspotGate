# Manual test — Phase 2: network setup

The network code can only be tested on a real Windows PC. Tick each line; write down anything unexpected
(screenshot of the page + the latest `service-*.log`).

## What you need

- Windows 10 or 11 PC with **two** network connections:
  - **WAN**: the internet (Ethernet to the box/router, a 4G USB modem, or Wi-Fi).
  - **LAN**: a second network card (e.g. USB-Ethernet adapter) with a cable to the Wi-Fi access point.
- The access point in **access point / bridge mode**: its own DHCP server **off**, its router mode **off**.
  (If you only have the PC's Wi-Fi: see test 6, Mobile hotspot.)
- An Android phone and, if possible, an iPhone and a laptop.
- The .NET 8 SDK and this repository (or the published service).

## Start the service

From an **elevated** PowerShell ("Run as administrator") in the repository folder:

```powershell
.\build.ps1
dotnet run --project src\StonesHotspot.Service --launch-profile StonesHotspot.Service
```

Open <http://localhost:8080/admin/network> **on the PC itself**.

## Tests

| # | Step | Expected |
|---|------|----------|
| 1 | Open the Network page | Status shows "Droits administrateur : Oui". All adapters are listed with state, MAC, IP. The internet adapter is pre-selected as WAN "(suggéré)" |
| 2 | Run the service from a **non-elevated** PowerShell and open the page | Red message: the service is not running with administrator rights. "Partager Internet" is refused with the same message |
| 3 | Pick the same adapter for WAN and LAN, click "Partager Internet" | Red message: two different adapters are required. Nothing changes in Windows |
| 4 | Pick WAN + LAN correctly, click "Partager Internet" | Green "Partage activé.". "Partage actuel dans Windows" shows the WAN as *Internet (public)* and the LAN as *Clients (privé)*. In Windows (`ncpa.cpl` → LAN adapter → Properties → Sharing tab) the box is ticked. The LAN adapter now has IP **192.168.137.1** |
| 5 | Connect the phone to the access point's Wi-Fi | Phone gets an IP **192.168.137.x** and has internet (no filtering yet). Within ~10 s it appears on <http://localhost:8080/admin/devices> with its MAC, IP and (often) its name; Android/iPhone show the "MAC privée" badge |
| 6 | (Optional) Mobile hotspot: turn on *Settings → Network & internet → Mobile hotspot*, then choose the "Wi-Fi Direct Virtual Adapter" as LAN and click "Partager Internet" | Mode shows "Point d'accès mobile Windows". Phones joining the PC's hotspot appear in Connected devices. Turn Mobile hotspot off → the Network page shows the "turned off" warning |
| 7 | Reboot the PC, start the service again | Sharing is still on, or the log says "Internet sharing was not active and has been switched back on". Phone gets internet again |
| 8 | Untick sharing by hand in `ncpa.cpl`, restart the service | The service switches it back on (log line as in 7) |
| 9 | Click "Arrêter le partage" | Green "Partage arrêté.", sharing is off in Windows, the phone loses internet. Clicking "Partager Internet" again restores it |
| 10 | Unplug the LAN cable, reload the Network page | Orange warning: the customer network adapter is disconnected |
| 11 | Set the access point back to **router mode** (DHCP on), reconnect the phone | Within ~10 s the Connected devices page shows a warning about devices outside the hotspot network, or an IP conflict on the gateway address (depending on the access point) |
| 12 | Make the WAN use 192.168.137.x (e.g. change the box's network), click "Partager Internet" | Red message: IP address conflict with 192.168.137.x; nothing changed |
| 13 | From the phone, open `http://192.168.137.1:8080/admin/network` | "403" text: the page is only available on the hotspot PC |
| 14 | Switch the language with FR / EN at the top right, then change page | The chosen language stays on the next pages |

## Known limits of this phase

- No filtering: every device on the Wi-Fi has internet (the captive portal comes in phases 3–4).
- A device only appears after it has exchanged data with the PC (normally right after it gets its IP).
- Device names come from ICS's own DHCP list (`C:\Windows\System32\drivers\etc\hosts.ics`); some phones send no
  name.

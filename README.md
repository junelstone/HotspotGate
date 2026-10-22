# Stone's Hotspot

Windows Wi-Fi hotspot manager (captive portal) by **Stone's IT Consulting**: turns a Windows 10/11 PC into a
managed, paid Wi-Fi hotspot — vouchers, user accounts, tariffs, Mobile Money, admin dashboard.

> **Status: Phase 2 — network setup.** The service shares the internet from the WAN adapter to the LAN
> adapter with Windows Internet Connection Sharing and lists the devices on the customers' Wi-Fi. No access
> control yet: every device on the Wi-Fi has internet. See [Build phases](#build-phases).

## Requirements

| For | You need |
|---|---|
| Building | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.100 or newer 8.0.x), PowerShell 7+ or Windows PowerShell 5.1 |
| Running the hotspot | Windows 10/11 x64, Administrator rights, two network connections (WAN + LAN) — details arrive in phase 2 |

## Build and test

```powershell
.\build.ps1                    # restore, build (Release), run all tests
.\build.ps1 -Configuration Debug
.\build.ps1 -SkipTests
.\build.ps1 -Publish           # also publish Service + Tray (win-x64, self-contained) to .\artifacts\publish
```

Test results are written to `artifacts\test-results\tests.trx`. GitHub Actions runs the same script on
`windows-latest` for every push (`.github/workflows/build.yml`) and uploads the published apps.

## Run from source (development)

```powershell
dotnet run --project src\StonesHotspot.Service --launch-profile StonesHotspot.Service
```

Then open <http://localhost:8080/admin> (add `?lang=en` for English) and <http://localhost:8080/health>.
In Development the data folder is `src\StonesHotspot.Service\data\` instead of `%ProgramData%\StonesHotspot`.

Tray app (Windows only): `dotnet run --project src\StonesHotspot.Tray`. It shows the state of the
`StonesHotspot` Windows service, opens the dashboard, and starts/stops the service (needs admin rights).

### Installing the service by hand (until the installer exists)

From an elevated PowerShell, after `.\build.ps1 -Publish`:

```powershell
$exe = (Resolve-Path .\artifacts\publish\StonesHotspot.Service\StonesHotspot.Service.exe).Path
sc.exe create StonesHotspot binPath= "`"$exe`"" start= auto DisplayName= "Stone's Hotspot"
sc.exe start StonesHotspot
# remove:  sc.exe stop StonesHotspot ; sc.exe delete StonesHotspot
```

To reach the dashboard from a phone on the LAN, allow TCP 8080 in Windows Firewall (the installer will do this
later): `New-NetFirewallRule -DisplayName "Stone's Hotspot admin" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow`.

## Configuration

| File | Committed? | Purpose |
|---|---|---|
| `src/StonesHotspot.Service/appsettings.json` | yes | Defaults: ports, log levels, data folder (empty = `%ProgramData%\StonesHotspot`) |
| `appsettings.Local.json` | **no** (git-ignored) | Secrets (SMS, Mobile Money keys). Copy `appsettings.Local.example.json`. Read from the exe folder and from `%ProgramData%\StonesHotspot\` |

Never put API keys or passwords in `appsettings.json`.

Runtime data (`%ProgramData%\StonesHotspot\`):

| Path | Content |
|---|---|
| `logs\service-YYYYMMDD.log` | Service log, one file per day, 30 days kept |
| `backups\` | Database backups (phase 5) |
| `stoneshotspot.db` | SQLite database (WAL mode, so also `-wal`/`-shm` files while running) |
| `initial-admin-password.txt` | Created once, on first start: the random password of the `admin` account. Readable by Administrators only. Delete it after the first login |

## First login

On first start the service creates the staff account **`admin`** with a random password, written to
`%ProgramData%\StonesHotspot\initial-admin-password.txt` (from source: `src\StonesHotspot.Service\data\`). The
password must be changed at first login. There is deliberately no default password: customers on the
hotspot can reach the dashboard port.

## Network setup

The PC needs two connections: **WAN** (the internet) and **LAN** (a second card cabled to a Wi-Fi access point in
access point/bridge mode, or the PC's own Wi-Fi in Windows *Mobile hotspot* mode).

1. Start the service with administrator rights (the Windows service always has them).
2. On the PC, open <http://localhost:8080/admin/network>, choose WAN and LAN, click **Partager Internet**.
   Windows Internet Connection Sharing (ICS) gives the LAN card **192.168.137.1** and hands out
   192.168.137.x addresses to the customers.
3. Connected devices: <http://localhost:8080/admin/devices>.

These two pages only answer on the PC itself until staff login exists (phase 5). The service switches sharing
back on at start if Windows dropped it. Full test list: [`docs/manual-tests/phase-2-network.md`](docs/manual-tests/phase-2-network.md).

## Database

SQLite through EF Core; migrations are in `src/StonesHotspot.Core/Data/Migrations` and are applied
automatically when the service starts. To change the model:

```powershell
dotnet tool restore                      # once: installs dotnet-ef from .config/dotnet-tools.json
dotnet ef migrations add <Name> --project src/StonesHotspot.Core --output-dir Data/Migrations
```

Business rules are summarised in [`docs/business-rules.md`](docs/business-rules.md).

## Solution layout

| Project | Purpose |
|---|---|
| `src/StonesHotspot.Core` | Domain and business rules, no Windows dependency: EF Core model + migrations (`Data/`), rules (`Rules/`), services (`Services/`: tariffs, vouchers, sessions, accounts, cash, staff, devices, settings, audit). FR/EN resources, `Xaf` money type |
| `src/StonesHotspot.Licensing` | `ILicenseService` (the single "is this feature allowed?" gate), editions, Basic/Pro feature table. Phase 0: always "Pro trial" |
| `src/StonesHotspot.Network` | Adapters, ICS (`HNetCfg.HNetShare` COM), neighbour/ARP table, connected-device scan; WinDivert packet gate and DNS in phase 3 |
| `src/StonesHotspot.Web` | Razor Pages: captive portal and admin dashboard (today: home, network setup, connected devices) |
| `src/StonesHotspot.Service` | Windows Service host (Kestrel, Serilog, DI) |
| `src/StonesHotspot.Tray` | Tray icon: status, Start/Stop, Open dashboard |
| `src/StonesHotspot.LicenseServer` | Online licence API (skeleton) |
| `tools/StonesHotspot.LicenseManager` | **Owner-only** licence generator. Never shipped to customers |
| `tests/StonesHotspot.Tests` | xUnit tests |
| `legacy/python-prototype` | The earlier Python *HotspotGate* prototype, kept for reference only |

## Languages and currency

- All user-facing text lives in `src/StonesHotspot.Core/Localization/Strings.resx` (French, default) and
  `Strings.en.resx` (English). Add every new key to both files — a test fails otherwise.
- Web pages pick the language from `?lang=fr|en`, then the browser; French otherwise.
- Amounts use `Xaf` (whole FCFA, `long`): `new Xaf(1000).ToString()` → `1 000 FCFA` (no-break space);
  `ToString(XafStyle.Ascii)` for thermal printers and CSV.

## Build phases

| Phase | Content | State |
|---|---|---|
| 0 | Project skeleton | done |
| 1 | Data layer and business rules | done |
| 2 | Network setup (adapters, ICS, connected devices) | **done** — Mode B (own DHCP) pending a decision |
| 3 | Packet gate (WinDivert) and DNS | — |
| 4 | Captive portal | — |
| 5 | Admin dashboard | — |
| 6 | Bandwidth control and web filter | — |
| 7 | Business tools (shifts, POS, reports) | — |
| 8 | Free access and SMS login | — |
| 9 | Mobile Money payments | — |
| 10 | Licensing, trial and activation | — |
| 11 | Windows service, installer and updates | — |
| 12 | Hardening and final testing | — |

Plans for each phase are in `docs/phase-N-plan.md`.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `build.ps1` says the .NET SDK is missing | Install the .NET 8 SDK, open a new terminal |
| Port 8080 already in use | Change `Kestrel:Endpoints:Admin:Url` in `appsettings.json` (or `appsettings.Local.json`) |
| Service won't start | Read `%ProgramData%\StonesHotspot\logs\service-*.log` and the Windows Event Viewer (Application log) |
| Network page: "not running with administrator rights" | Run the service as the Windows service, or start it from a PowerShell opened with "Run as administrator" |
| Network page: "IP address conflict … 192.168.137.x" | Your internet box/modem uses the same network as Windows sharing. Change the box's LAN network (e.g. to 192.168.1.x) |
| Phones get 192.168.0.x / 192.168.1.x instead of 192.168.137.x | The access point is in router mode or its DHCP is on: set it to access point/bridge mode |
| "database is locked" in the log | Another program (e.g. DB Browser for SQLite) has the database open for writing; close it |
| Lost the admin password | Another admin can reset it; an offline reset tool for the last admin comes with the dashboard (phase 5) |
| Tray "Start/Stop" shows an administrator message | Run the tray app as administrator (the installer will grant service rights later) |

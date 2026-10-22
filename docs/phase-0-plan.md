# Phase 0 — Project skeleton (plan)

Goal: a .NET 8 solution that builds with one command (`build.ps1`) and a test project that passes.
No network, database or licensing logic yet — only the structure that later phases fill in.

## Repository layout

```
StonesHotspot.sln
global.json                     pins the .NET 8 SDK
Directory.Build.props           shared settings (nullable, warnings as errors, version)
Directory.Packages.props        central NuGet package versions
build.ps1                       restore → build → test (CI and local)
.github/workflows/build.yml     runs build.ps1 on windows-latest
src/
  StonesHotspot.Core/           domain + business rules (no Windows dependency)
    Localization/Strings.resx       French (default / neutral language)
    Localization/Strings.en.resx    English
    Localization/Languages.cs       supported cultures, default = fr
    Money/Xaf.cs                    whole-number FCFA amount type + "1 000 FCFA" formatting/parsing
    AppPaths.cs                     %ProgramData%\StonesHotspot (data, logs), overridable
  StonesHotspot.Licensing/      ILicenseService, Edition, Feature, EditionFeatures table, trial stub
  StonesHotspot.Network/        placeholder (WinDivert, ICS, DNS, ARP arrive in phases 2–3)
  StonesHotspot.Web/            ASP.NET Core endpoints for portal + admin (only /health and a
                                 placeholder page now)
  StonesHotspot.Service/        Windows Service host: Kestrel, Serilog, DI, config
  StonesHotspot.Tray/           WinForms tray icon: status, open dashboard, start/stop service
  StonesHotspot.LicenseServer/  ASP.NET Core API skeleton (/health only)
tools/
  StonesHotspot.LicenseManager/ owner-only WinForms tool, placeholder window. NEVER shipped.
tests/
  StonesHotspot.Tests/          xUnit: XAF formatting, FR/EN resource parity, licence stub,
                                 service /health smoke test
legacy/python-prototype/        the earlier Python HotspotGate prototype, kept for reference
```

## Decisions

- **Default language French.** `Strings.resx` holds French and is the neutral resource
  (`[assembly: NeutralResourcesLanguage("fr")]`); `Strings.en.resx` holds English. A test fails if a key
  exists in one file but not the other.
- **XAF format.** `1 000 FCFA` in both languages (the usual local style), group separator is a
  no-break space so amounts never wrap; `Xaf.ToString(XafStyle.Ascii)` uses a plain space for thermal
  printers. Amounts are `long`, no decimals. Parsing accepts `1000`, `1 000`, `1.000`, `1 000 FCFA`,
  `1000 XAF`, and rejects decimals.
- **Licensing hook.** `ILicenseService.IsAllowed(Feature)` is the single gate. Phase 0 registers
  `TrialStubLicenseService` (always "Pro trial, 30 days left"). The Basic/Pro table lives in
  `EditionFeatures` so a feature can move between editions in one line.
- **Secrets.** `appsettings.json` (committed, no secrets) + `appsettings.Local.json` (git-ignored) with
  `appsettings.Local.example.json` next to it. Same pattern for the LicenseServer.
- **Logging.** Serilog, daily rolling files in `%ProgramData%\StonesHotspot\logs`, 30 files kept.
- **Ports.** Admin dashboard on 8080 now; the portal on port 80 is added in phases 3–4.
- **Windows-only projects** (Tray, LicenseManager) target `net8.0-windows` with
  `EnableWindowsTargeting`, so the whole solution still compiles on a Linux build agent.

# Third-party components

| Component | Version | Licence | Used for | How it is obtained |
|---|---|---|---|---|
| [WinDivert](https://github.com/basil00/WinDivert) | 2.2.2 | LGPL v3 (or GPL v2) | Packet capture/filter driver of the hotspot gate | `tools/Get-WinDivert.ps1` downloads the official release and checks SHA-256 `63cb4176…3f15`; not stored in Git. Its `LICENSE` is shipped next to the service as `WinDivert-LICENSE.txt` |
| .NET 8, ASP.NET Core, EF Core | 8.0 | MIT | Runtime, web server, database access | NuGet |
| Serilog (+ AspNetCore, File sink) | see `Directory.Packages.props` | Apache 2.0 | Logging | NuGet |
| BCrypt.Net-Next | 4.0.3 | MIT | Password hashing | NuGet |

WinDivert is used unmodified and loaded dynamically (`WinDivert.dll`), which the LGPL allows in a closed-source
product as long as its licence text is shipped and users can replace the DLL/driver with their own build.

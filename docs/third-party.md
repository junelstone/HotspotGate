# Third-party components

| Component | Version | Licence | Used for | How it is obtained |
|---|---|---|---|---|
| [WinDivert](https://github.com/basil00/WinDivert) | 2.2.2 | LGPL v3 (or GPL v2) | Packet capture/filter driver of the hotspot gate | `tools/Get-WinDivert.ps1` downloads the official release and checks SHA-256 `63cb4176…3f15`; not stored in Git. Its `LICENSE` is shipped next to the service as `WinDivert-LICENSE.txt` |
| .NET 8, ASP.NET Core, EF Core | 8.0 | MIT | Runtime, web server, database access | NuGet |
| Serilog (+ AspNetCore, File sink) | see `Directory.Packages.props` | Apache 2.0 | Logging | NuGet |
| BCrypt.Net-Next | 4.0.3 | MIT | Password hashing | NuGet |
| QRCoder | 1.6.0 | MIT | QR codes on printed vouchers and receipt PDFs (rendered by our code) | NuGet |
| System.IO.Ports | 8.0.0 | MIT | USB GSM modem (COM port) for SMS | NuGet |
| BouncyCastle.Cryptography | 2.4.0 | MIT | Ed25519 signatures of licence codes and licence-server answers | NuGet |
| System.Management | 8.0.0 | MIT | Hardware serial numbers for the Machine ID (WMI) | NuGet |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | DPAPI protection of the trial record | NuGet |
| Microsoft.Data.Sqlite | 8.0.11 | MIT | Licence server and LicenseManager databases | NuGet |

WinDivert is used unmodified and loaded dynamically (`WinDivert.dll`), which the LGPL allows in a closed-source
product as long as its licence text is shipped and users can replace the DLL/driver with their own build.

PDF files (receipts, reports) are written by our own small writer using the 14 standard PDF fonts, which are not
embedded: no font or PDF library licence is involved.

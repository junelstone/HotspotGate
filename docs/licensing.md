# Licensing — how it works and what it does not protect

## The pieces

| Piece | Who has it | Contains |
|---|---|---|
| Hotspot (service) | every customer | the two **public** keys; checks codes and server answers |
| LicenseManager (`tools/StonesHotspot.LicenseManager`) | only the owner, on his own PC | the **private** licence key and server key, encrypted with his password; the customers and licences book |
| Licence server (`src/StonesHotspot.LicenseServer`) | the owner's server | the **server** private key only (it cannot create licences); trials, licences, check-ins |

## Before the first release (owner's keys)

The repository holds **development** keys (`src/StonesHotspot.Licensing/Keys/*.txt`, marked `DEVELOPMENT`); their
private halves exist only on the developer's PC (`tools/dev-keys/`, never committed). A build with them must never be
shipped (Phase 11 makes the release build fail).

1. On the owner's PC: build and run LicenseManager (`dotnet run --project tools/StonesHotspot.LicenseManager`).
   First run: choose a password (≥ 12 characters), the keys are created, save the **backup copy** of the key file on a
   USB stick kept somewhere else.
2. **Clés et sauvegarde** tab: copy the two public keys into `src/StonesHotspot.Licensing/Keys/licence-public.txt`
   and `server-public.txt` (replace the whole file; no "DEVELOPMENT" line), commit, build.
3. Copy the server private key into the licence server's `appsettings.Local.json` (`LicenceServer:ServerPrivateKey`).
4. From then on, every code is signed by the owner's key; development codes stop working.

## Licence server deployment

ASP.NET Core app with its own SQLite database. Runs on a small Linux VPS (or Windows) behind HTTPS (e.g. Caddy or
nginx with a Let's Encrypt certificate). Settings in `appsettings.Local.json` (see the example file): server private
key, owner page password, LicenseManager token, database path. Hotspots find it through
`Licensing:ServerUrl` in their `appsettings.json` (`https://licences.example.cg/`); LicenseManager through its
**Serveur** tab. Owner page: `https://…/admin` (any user name + the password). Back up the server database like any
other.

Without a server, everything works offline: codes are typed by hand, renewals are new codes, but revocations
cannot reach a hotspot (it then simply runs until its licence ends).

## What the protection gives — and its limits

What it does well:
- A code cannot be made or changed without the private key (Ed25519 signatures). The hotspot only contains public keys.
- A code works on one PC (Machine ID, 3 of 4 hardware parts — or all readable parts on PCs with fewer).
- The trial is stored in two protected places outside the data folder and on the server; reinstalling or deleting
  the data does not restart it. Moving the clock back does not extend anything while the service runs (real
  elapsed time is counted) and never goes before the latest time seen.
- Server answers are signed and bound to a random value of the request: an old "valid" answer cannot be replayed to
  hide a revocation.

What it does **not** stop (no licensing system does):
- **Modifying the program.** .NET code can be decompiled and patched to skip the check. Mitigations (Phase 11/12):
  obfuscation, Authenticode code signing, an integrity check at start, checks in several places. They make it
  harder, never impossible.
- **Virtual machines or cloned disks**: a clone copies Windows' MachineGuid and disk serial; CPU ids are the same for
  every CPU of a model. A careful cloner can run one licence on two PCs offline. The daily check-in lets the owner
  see two PCs using one licence on the owner page.
- **Blocking the server**: a hotspot that never reaches the server is not told about a revocation; it runs until
  the licence end date (that is why licences are given for a limited time, e.g. one year).
- **Clock games across restarts**: if the PC is switched off, its clock set back, and switched on again, the time
  while it was off cannot be known offline; it counts from the latest time seen (never earlier). The server time
  corrects this at the next check-in.
- **Deleting both trial copies (registry and hidden file)** with administrator rights while offline restarts the
  trial locally; the server still knows the first start of that Machine ID and corrects it at the first contact.
- **A leaked owner password + key file** lets anyone make licences. Keep the key file and its backup away from the
  password; if it leaks, create new keys and re-issue every licence (new build with the new public key).

The goal is to keep honest customers honest and make piracy more trouble than a licence costs, not to promise
something unbreakable.

using Serilog;
using StonesHotspot.LicenseServer;
using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Online;

// Online licence API: trial registration, activation, daily check-in (valid / revoked / renewed), renewals, and the
// owner's page. Runs on the owner's server (Linux or Windows) behind HTTPS. Secrets in appsettings.Local.json.
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Services.AddSerilog((_, lc) => lc.ReadFrom.Configuration(builder.Configuration));
builder.Services.AddSingleton(TimeProvider.System);

var config = builder.Configuration.GetSection("LicenceServer");
var serverKey = config["ServerPrivateKey"];
if (string.IsNullOrWhiteSpace(serverKey))
    throw new InvalidOperationException("LicenceServer:ServerPrivateKey is missing (appsettings.Local.json, from LicenseManager → Clés).");
var licencePublic = config["LicencePublicKey"] is { Length: > 0 } key
    ? Convert.FromBase64String(key)
    : LicensingOptions.WithEmbeddedKeys(".").LicencePublicKey;
var update = config.GetSection("Update") is var u && !string.IsNullOrWhiteSpace(u["Version"])
    ? new UpdateManifest(u["Version"]!, u["Url"] ?? "", (u["Sha256"] ?? "").Trim().ToLowerInvariant(), u["Notes"],
        u.GetValue<DateTimeOffset?>("PublishedAt") ?? DateTimeOffset.UtcNow)
    : null;
if (update is { IsWellFormed: false })
    throw new InvalidOperationException("LicenceServer:Update needs a version (1.2.3), an https:// Url and the installer's Sha256 (64 hex characters).");
var options = new ServerOptions(Convert.FromBase64String(serverKey), licencePublic, config["AdminPassword"] ?? "", config["ManagerToken"] ?? "", update);
builder.Services.AddLicenceServer(options, new ServerStore(config["Database"] is { Length: > 0 } db ? db : "data/licences-server.db"));

var app = builder.Build();
app.MapLicenceServer();
app.Run();

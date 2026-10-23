using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using StonesHotspot.Licensing.Codes;
using StonesHotspot.Licensing.Online;

namespace StonesHotspot.LicenseServer;

/// <param name="ServerPrivateKey">Signs every answer (the owner's server key, not the licence key).</param>
/// <param name="LicencePublicKey">Checks licence codes sent at activation.</param>
/// <param name="AdminPassword">Owner page (<c>/admin</c>, HTTP Basic over HTTPS, any user name).</param>
/// <param name="ManagerToken">Bearer token LicenseManager uses to upload licences and revocations.</param>
public sealed record ServerOptions(byte[] ServerPrivateKey, byte[] LicencePublicKey, string AdminPassword, string ManagerToken);

/// <summary>The licence server's endpoints. Every answer to a hotspot is signed with the server key.</summary>
public static class LicenceServerApi
{
    public const string PublicPolicy = "public";

    public static IServiceCollection AddLicenceServer(this IServiceCollection services, ServerOptions options, ServerStore store)
    {
        services.AddSingleton(options);
        services.AddSingleton(store);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(PublicPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }

    public static WebApplication MapLicenceServer(this WebApplication app)
    {
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        var api = app.MapGroup("/api/v1").RequireRateLimiting(PublicPolicy);

        api.MapPost("/trial", (TrialRequest r, ServerStore store, ServerOptions o, TimeProvider clock, HttpContext http) =>
        {
            if (!MachineId.TryParse(r.MachineId, out _))
                return Results.BadRequest();
            var now = clock.GetUtcNow();
            var trial = store.RegisterTrial(r.MachineId, r.FirstRun > now ? now : r.FirstRun, now, r.Version);
            store.RecordCheckIn(null, r.MachineId, "trial", ServerStatus.TrialRegistered, now, Ip(http), r.Version);
            return Signed(new ServerAnswer(ServerStatus.TrialRegistered, null, r.MachineId, r.Nonce, now, TrialFirstRun: trial.FirstRun), o);
        });

        api.MapPost("/activate", (ActivateRequest r, ServerStore store, ServerOptions o, TimeProvider clock, HttpContext http) =>
        {
            var licence = LicenceCode.Verify(r.Code, o.LicencePublicKey);
            if (licence is null || !MachineId.TryParse(r.MachineId, out _))
                return Results.BadRequest();
            // Codes made while LicenseManager was offline become known here.
            store.AddIfNewer(new ServerLicence(licence.Id, licence.Customer, licence.MachineId.ToString(), licence.Edition.ToString(),
                licence.IssuedOn, licence.ExpiresOn, r.Code.Trim(), null));
            return Answer(licence.Id, r.MachineId, licence.ExpiresOn, r.Nonce, "activate", r.Version, store, o, clock, http);
        });

        api.MapPost("/checkin", (CheckInRequest r, ServerStore store, ServerOptions o, TimeProvider clock, HttpContext http) =>
            Answer(r.LicenceId, r.MachineId, r.ExpiresOn, r.Nonce, "checkin", r.Version, store, o, clock, http));

        api.MapGet("/renewal/{licenceId}", (uint licenceId, string machineId, DateOnly expiresOn, string nonce, ServerStore store, ServerOptions o, TimeProvider clock, HttpContext http) =>
            Answer(licenceId, machineId, expiresOn, nonce, "renewal", null, store, o, clock, http));

        app.MapPost("/api/v1/manager/licences", (LicenceUpload[] uploads, ServerStore store, ServerOptions o, HttpContext http) =>
        {
            if (!Authorized(http.Request.Headers.Authorization.ToString(), "Bearer ", o.ManagerToken))
                return Results.Unauthorized();
            store.UpsertLicences(uploads.Select(u => new ServerLicence(u.Id, u.Customer, u.MachineId, u.Edition, u.IssuedOn, u.ExpiresOn, u.Code, u.RevokedAt)));
            return Results.Ok(new { received = uploads.Length });
        });

        app.MapGet("/admin", (ServerStore store, ServerOptions o, HttpContext http) =>
        {
            if (!BasicAuthorized(http.Request.Headers.Authorization.ToString(), o.AdminPassword))
            {
                http.Response.Headers.WWWAuthenticate = "Basic realm=\"Stone's Hotspot licences\", charset=\"UTF-8\"";
                return Results.Unauthorized();
            }
            return Results.Content(AdminPage.Render(store), "text/html; charset=utf-8");
        });
        return app;
    }

    private static IResult Answer(uint licenceId, string machineIdText, DateOnly clientExpiry, string nonce, string kind, string? version,
        ServerStore store, ServerOptions o, TimeProvider clock, HttpContext http)
    {
        var now = clock.GetUtcNow();
        var licence = store.Find(licenceId);
        string status;
        if (licence is null || !MachineId.TryParse(machineIdText, out var machine) || !MachineId.TryParse(licence.MachineId, out var licensed) || !licensed.Matches(machine))
            status = ServerStatus.Unknown;
        else if (licence.RevokedAt is not null)
            status = ServerStatus.Revoked;
        else if (licence.ExpiresOn > clientExpiry)
            status = ServerStatus.Renewed;
        else
            status = ServerStatus.Valid;

        store.RecordCheckIn(licenceId, machineIdText, kind, status, now, Ip(http), version);
        return Signed(new ServerAnswer(status, licenceId, machineIdText, nonce, now,
            status == ServerStatus.Revoked ? licence!.RevokedAt : null,
            status == ServerStatus.Renewed ? licence!.Code : null), o);
    }

    private static IResult Signed(ServerAnswer answer, ServerOptions o) => Results.Json(SignedAnswer.Sign(answer, o.ServerPrivateKey));

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    private static bool Authorized(string header, string scheme, string secret) =>
        secret.Length > 0 && header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(header[scheme.Length..].Trim())), SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static bool BasicAuthorized(string header, string password)
    {
        if (password.Length == 0 || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            var colon = decoded.IndexOf(':');
            return colon >= 0 && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(decoded[(colon + 1)..])), SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>The owner's page: licences, trials and latest check-ins.</summary>
internal static class AdminPage
{
    public static string Render(ServerStore store)
    {
        static string E(object? v) => WebUtility.HtmlEncode(v?.ToString() ?? "");
        var sb = new StringBuilder("""
            <!doctype html><html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Licences — Stone's Hotspot</title><style>body{font-family:system-ui,sans-serif;margin:1rem;color:#1d2330}table{border-collapse:collapse;width:100%;margin-bottom:2rem;font-size:.9rem}
            th,td{border-bottom:1px solid #ddd;padding:.35rem;text-align:left}th{color:#5d6675}.revoked{color:#b42318}.scroll{overflow-x:auto}</style></head><body>
            <h1>Licences — Stone's Hotspot</h1>
            """);
        sb.Append("<h2>Licences</h2><div class=scroll><table><tr><th>N°</th><th>Client</th><th>Machine ID</th><th>Édition</th><th>Émise</th><th>Expire</th><th>Révoquée</th></tr>");
        foreach (var l in store.Licences())
            sb.Append($"<tr class=\"{(l.RevokedAt is null ? "" : "revoked")}\"><td>{l.Id}</td><td>{E(l.Customer)}</td><td>{E(l.MachineId)}</td><td>{E(l.Edition)}</td><td>{l.IssuedOn:dd/MM/yyyy}</td><td>{l.ExpiresOn:dd/MM/yyyy}</td><td>{l.RevokedAt:dd/MM/yyyy}</td></tr>");
        sb.Append("</table></div><h2>Essais</h2><div class=scroll><table><tr><th>Machine ID</th><th>Premier démarrage</th><th>Dernier contact</th><th>Version</th></tr>");
        foreach (var t in store.Trials())
            sb.Append($"<tr><td>{E(t.MachineId)}</td><td>{t.FirstRun:dd/MM/yyyy}</td><td>{t.LastSeen:dd/MM/yyyy HH:mm}</td><td>{E(t.Version)}</td></tr>");
        sb.Append("</table></div><h2>Derniers contacts</h2><div class=scroll><table><tr><th>Date (UTC)</th><th>Type</th><th>Licence</th><th>Machine ID</th><th>Réponse</th><th>IP</th><th>Version</th></tr>");
        foreach (var c in store.CheckIns(200))
            sb.Append($"<tr><td>{c.At:dd/MM/yyyy HH:mm}</td><td>{E(c.Kind)}</td><td>{c.LicenceId}</td><td>{E(c.MachineId)}</td><td>{E(c.Result)}</td><td>{E(c.Ip)}</td><td>{E(c.Version)}</td></tr>");
        sb.Append("</table></div></body></html>");
        return sb.ToString();
    }
}

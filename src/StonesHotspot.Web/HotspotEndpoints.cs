using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StonesHotspot.Core.Payments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StonesHotspot.Core;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web;

/// <summary>Non-page HTTP endpoints. Pages (portal, dashboard) are Razor Pages under <c>Pages/</c>.</summary>
public static class HotspotEndpoints
{
    public static string Version { get; } =
        typeof(HotspotEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static IEndpointRouteBuilder MapHotspotEndpoints(this IEndpointRouteBuilder app)
    {
        // Used by the tray app and by monitoring to know the service is up.
        app.MapGet("/health", (ILicenseService licence, TimeProvider clock) =>
        {
            var status = licence.GetStatus();
            return Results.Ok(new
            {
                status = "ok",
                version = Version,
                edition = status.Edition.ToString(),
                daysLeft = status.DaysLeft(clock.GetUtcNow()),
                hotspotAllowed = status.HotspotAllowed,
            });
        });

        // Business logo for the portal (uploaded in the dashboard, phase 5).
        app.MapGet("/portal/logo", (AppPaths paths) =>
        {
            var file = PortalBranding.FindLogo(paths);
            if (file is null)
                return Results.NotFound();
            var type = Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                _ => "image/jpeg",
            };
            return Results.File(file, type, lastModified: File.GetLastWriteTimeUtc(file));
        });

        // Mobile Money operator notifications. Only a hint: the payment's real state is asked to the operator with our
        // own keys, so a forged notification cannot create a code. Always 200, nothing is revealed.
        app.MapMethods("/api/payments/{operatorName:regex(^(mtn|airtel)$)}", ["POST", "PUT"], async (
            string operatorName, HttpRequest request, MobileMoneyService payments, ILicenseService licence, ILoggerFactory logs) =>
        {
            if (!licence.IsAllowed(Feature.MobileMoneyPayments))
                return Results.Ok();
            string? reference = null;
            try
            {
                using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
                var root = doc.RootElement;
                reference = operatorName == "mtn"
                    ? Text(root, "externalId") ?? request.Headers["X-Reference-Id"].FirstOrDefault()
                    : Text(root, "transaction", "id");
            }
            catch (JsonException)
            {
            }
            if (reference is { Length: > 0 and <= 40 })
            {
                logs.CreateLogger("StonesHotspot.Payments").LogInformation("Callback from {Operator} for {Reference}", operatorName, reference);
                await payments.CheckAsync(reference, ct: request.HttpContext.RequestAborted);
            }
            return Results.Ok();
        }).DisableAntiforgery();

        app.MapGet("/", () => Results.Redirect("/admin"));
        return app;
    }

    private static string? Text(JsonElement e, params string[] path)
    {
        foreach (var name in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e))
                return null;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}

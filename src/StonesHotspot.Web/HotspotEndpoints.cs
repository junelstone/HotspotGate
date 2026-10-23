using System.Reflection;
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

        app.MapGet("/", () => Results.Redirect("/admin"));
        return app;
    }
}

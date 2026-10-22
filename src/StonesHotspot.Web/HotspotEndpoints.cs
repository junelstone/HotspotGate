using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StonesHotspot.Licensing;

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

        app.MapGet("/", () => Results.Redirect("/admin"));
        return app;
    }
}

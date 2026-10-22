using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StonesHotspot.Core.Localization;
using StonesHotspot.Licensing;

namespace StonesHotspot.Web;

/// <summary>HTTP endpoints of the portal and admin dashboard. Phase 0: health check and a placeholder page.</summary>
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

        app.MapGet("/admin", (ILicenseService licence, TimeProvider clock) =>
        {
            var days = licence.GetStatus().DaysLeft(clock.GetUtcNow()) ?? 0;
            var culture = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
            var html = $"""
                <!doctype html>
                <html lang="{culture}">
                <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{Encode(Strings.AppName)}</title></head>
                <body style="font-family:system-ui,sans-serif;margin:2rem">
                <h1>{Encode(Strings.AppName)}</h1>
                <p>{Encode(string.Format(Strings.Licence_TrialDaysLeft, days))}</p>
                <p>{Encode(Strings.Placeholder_ComingSoon)}</p>
                <p><a href="?lang=fr">{Encode(Strings.Language_French)}</a> · <a href="?lang=en">{Encode(Strings.Language_English)}</a></p>
                </body></html>
                """;
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/", () => Results.Redirect("/admin"));
        return app;
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}

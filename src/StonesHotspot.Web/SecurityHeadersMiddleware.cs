using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace StonesHotspot.Web;

/// <summary>
/// Browser protections on every answer: no MIME sniffing, no framing (clickjacking of the dashboard), no referrer
/// (codes in URLs never leak to other sites), and a content policy allowing only our own scripts, styles and images
/// (inline ones included: the pages are self-contained) — no outside script can be loaded.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            return Task.CompletedTask;
        });
        return next(context);
    });
}

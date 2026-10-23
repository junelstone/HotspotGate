using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StonesHotspot.Network.Gate;

namespace StonesHotspot.Web;

/// <summary>
/// Requests from customer devices that name another web site in <c>Host:</c> reached us through the gate's HTTP
/// redirect (or a blocked domain's DNS answer). Blocked sites get the "site blocked" page; everything else is
/// sent to the portal with <c>302</c> — this is what makes phones show "Sign in to network".
/// </summary>
public sealed class CaptivePortalMiddleware(RequestDelegate next)
{
    public const string BlockedPagePath = "/portal/blocked";
    public const string BlockedHostItem = "StonesHotspot.BlockedHost";

    public async Task InvokeAsync(HttpContext context, PortalLocation portal, IPacketGate gate, ClientIdentityResolver identities)
    {
        var remote = context.Connection.RemoteIpAddress;
        var host = context.Request.Host.Host;
        if (!portal.IsCustomer(remote) || IsPortalHost(host, portal))
        {
            await next(context);
            return;
        }

        // The device's browser thinks it is talking to another site: never let it keep this connection.
        context.Response.Headers.Connection = "close";
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

        if (gate.Processor.Snapshot?.Rules.BlockedDomains.Matches(host) == true)
        {
            context.Items[BlockedHostItem] = host;
            context.Request.Path = BlockedPagePath;
            context.Request.QueryString = QueryString.Empty;
            await next(context);
            return;
        }

        var original = $"http://{host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
        var mac = identities.ResolveMac(remote);
        var target = portal.PortalUrl() + QueryString.Create(new Dictionary<string, string?>
        {
            ["mac"] = mac,
            ["url"] = original,
        }.Where(p => p.Value is not null));
        context.Response.Redirect(target);
    }

    private static bool IsPortalHost(string host, PortalLocation portal) =>
        portal.Gateway?.ToString() == host;
}

public static class CaptivePortalExtensions
{
    public static IApplicationBuilder UseCaptivePortal(this IApplicationBuilder app) => app.UseMiddleware<CaptivePortalMiddleware>();
}

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Gate;

namespace StonesHotspot.Web;

/// <summary>
/// The dashboard, the first-run wizard and the health endpoint answer only to the PC itself, the customer (LAN)
/// network, and the networks listed in <c>StonesHotspot:AdminNetworks</c> (e.g. "10.0.0.0/24"). Anyone else — the
/// internet side included — gets 404, whatever the firewall lets through. The portal and the Mobile Money callbacks
/// are not concerned.
/// </summary>
public sealed class DashboardAccessMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private static readonly PathString[] Protected = ["/admin", "/setup", "/health"];

    private readonly Ipv4Subnet[] _extra = (configuration.GetSection("StonesHotspot:AdminNetworks").Get<string[]>() ?? [])
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => Ipv4Subnet.Parse(n.Trim()))
        .ToArray();

    public Task InvokeAsync(HttpContext context, PortalLocation portal)
    {
        if (Protected.Any(p => context.Request.Path.StartsWithSegments(p)) && !IsAllowed(context.Connection.RemoteIpAddress, portal.Lan))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return next(context);
    }

    internal bool IsAllowed(IPAddress? ip, Ipv4Subnet? lan)
    {
        if (ip is null)
            return false;
        if (IPAddress.IsLoopback(ip))
            return true;
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        return lan is { } l && l.Contains(ip) || _extra.Any(n => n.Contains(ip));
    }
}

public static class DashboardAccessExtensions
{
    public static IApplicationBuilder UseDashboardAccessRestriction(this IApplicationBuilder app) => app.UseMiddleware<DashboardAccessMiddleware>();
}

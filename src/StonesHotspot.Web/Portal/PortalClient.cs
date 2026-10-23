using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Network.Gate;

namespace StonesHotspot.Web.Portal;

/// <summary>The customer device behind a portal request: its IP and its MAC (the device identity).</summary>
public sealed record PortalDevice(IPAddress? Ip, string? Mac);

public sealed class PortalClient(ClientIdentityResolver identities, IHostEnvironment environment)
{
    /// <summary>Used for requests from the PC itself when running from source, to try the portal in a desktop browser.</summary>
    public const string DevelopmentTestMac = "02:00:00:00:00:01";

    public PortalDevice Identify(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is { IsIPv4MappedToIPv6: true })
            ip = ip.MapToIPv4();

        if (ip is not null && IPAddress.IsLoopback(ip) && environment.IsDevelopment())
            return new PortalDevice(ip, DevelopmentTestMac);
        return new PortalDevice(ip, identities.ResolveMac(ip));
    }
}

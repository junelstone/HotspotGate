using System.Net;
using StonesHotspot.Network.Adapters;

namespace StonesHotspot.Network.Gate;

/// <summary>Where customers reach the portal; kept up to date by the engine, read by the web layer.</summary>
public sealed class PortalLocation
{
    private volatile Snapshot _current = new(null, 80);

    private sealed record Snapshot(Ipv4Subnet? Lan, int PortalPort);

    public Ipv4Subnet? Lan => _current.Lan;
    public IPAddress? Gateway => _current.Lan?.Address;
    public int PortalPort => _current.PortalPort;

    public void Update(Ipv4Subnet? lan, int portalPort) => _current = new Snapshot(lan, portalPort);

    /// <summary>True for a customer device on the hotspot network (not the gateway itself).</summary>
    public bool IsCustomer(IPAddress? ip)
    {
        var lan = _current.Lan;
        if (ip is null || lan is null)
            return false;
        ip = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
               && lan.Value.Contains(ip) && !ip.Equals(lan.Value.Address);
    }

    /// <summary>http://192.168.137.1/portal (with the port when it is not 80).</summary>
    public string PortalUrl(string path = "/portal")
    {
        var host = Gateway?.ToString() ?? "localhost";
        return PortalPort == 80 ? $"http://{host}{path}" : $"http://{host}:{PortalPort}{path}";
    }
}

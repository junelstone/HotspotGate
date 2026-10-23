using System.Net;
using StonesHotspot.Network.Neighbors;

namespace StonesHotspot.Network.Gate;

/// <summary>Finds the MAC address (device identity) behind a customer's IP, for the portal.</summary>
public sealed class ClientIdentityResolver(INeighborTable neighbors, TimeProvider clock)
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(1);
    private IReadOnlyList<NeighborEntry> _cache = [];
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    public string? ResolveMac(IPAddress? ip)
    {
        if (ip is null)
            return null;
        ip = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        var mac = Find(Entries(forceRefresh: false), ip);
        // A device that just joined is often not in the cached copy yet: read the table again.
        return mac ?? Find(Entries(forceRefresh: true), ip);
    }

    private IReadOnlyList<NeighborEntry> Entries(bool forceRefresh)
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            if (forceRefresh || now - _cachedAt > CacheTime)
            {
                try
                {
                    _cache = neighbors.GetIpv4Entries();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    _cache = [];
                }
                _cachedAt = now;
            }
            return _cache;
        }
    }

    private static string? Find(IReadOnlyList<NeighborEntry> entries, IPAddress ip) =>
        entries.FirstOrDefault(e => e.Ip.Equals(ip) && e.State != NeighborState.Unreachable && e.State != NeighborState.Incomplete)?.Mac;
}

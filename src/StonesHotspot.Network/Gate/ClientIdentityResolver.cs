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

        IReadOnlyList<NeighborEntry> entries;
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            if (now - _cachedAt > CacheTime)
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
            entries = _cache;
        }

        return entries.FirstOrDefault(e => e.Ip.Equals(ip) && e.State != NeighborState.Unreachable && e.State != NeighborState.Incomplete)?.Mac;
    }
}

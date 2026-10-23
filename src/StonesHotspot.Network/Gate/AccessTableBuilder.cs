using System.Collections.Frozen;
using System.Net;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Neighbors;

namespace StonesHotspot.Network.Gate;

/// <summary>An active session as the gate needs it.</summary>
public sealed record AccessSession(long SessionId, string Mac, string? LastIp);

public sealed record AccessInputs(
    IReadOnlyList<AccessSession> Sessions,
    IReadOnlyCollection<string> WhitelistedMacs,
    IReadOnlyCollection<string> BlockedMacs,
    IReadOnlyList<NeighborEntry> LanNeighbors,
    bool HotspotAllowed);

/// <summary>
/// Turns "who may use the internet" (by MAC, the device identity) into "which IPs pass the gate". IPs come from
/// the neighbour (ARP) table, so a device that gets a new IP keeps its access; the session's last known IP is
/// used too unless the neighbour table says another device now owns it.
/// </summary>
public static class AccessTableBuilder
{
    public static FrozenDictionary<uint, AccessEntry> Build(AccessInputs inputs)
    {
        var table = new Dictionary<uint, AccessEntry>();
        if (!inputs.HotspotAllowed)
            return table.ToFrozenDictionary();

        var blocked = inputs.BlockedMacs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ipsByMac = inputs.LanNeighbors
            .Where(n => n.State != NeighborState.Unreachable && n.State != NeighborState.Incomplete)
            .GroupBy(n => n.Mac, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(n => Ipv4Subnet.ToUInt32(n.Ip)).ToList(), StringComparer.OrdinalIgnoreCase);
        var macByIp = new Dictionary<uint, string>();
        foreach (var (mac, ips) in ipsByMac)
            foreach (var ip in ips)
                macByIp[ip] = mac;

        void Allow(string mac, long? sessionId, string? lastIp)
        {
            if (blocked.Contains(mac))
                return;
            if (ipsByMac.TryGetValue(mac, out var ips))
                foreach (var ip in ips)
                    table[ip] = new AccessEntry(mac, sessionId ?? (table.TryGetValue(ip, out var e) ? e.SessionId : null));

            if (lastIp is not null && IPAddress.TryParse(lastIp, out var parsed))
            {
                var ip = Ipv4Subnet.ToUInt32(parsed);
                var owner = macByIp.GetValueOrDefault(ip);
                if (owner is null || string.Equals(owner, mac, StringComparison.OrdinalIgnoreCase))
                    table.TryAdd(ip, new AccessEntry(mac, sessionId));
            }
        }

        foreach (var session in inputs.Sessions)
            Allow(session.Mac, session.SessionId, session.LastIp);
        foreach (var mac in inputs.WhitelistedMacs)
            Allow(mac, null, null);

        return table.ToFrozenDictionary();
    }
}

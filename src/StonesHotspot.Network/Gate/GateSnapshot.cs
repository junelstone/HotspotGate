using System.Collections.Frozen;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Dns;

namespace StonesHotspot.Network.Gate;

/// <summary>Why a device has internet: a running session, or a whitelisted MAC.</summary>
public sealed record AccessEntry(string Mac, long? SessionId);

/// <summary>The web-filter rules as the gate and DNS responder use them.</summary>
public sealed record FilterRules(DomainSet BlockedDomains, DomainSet FreeDomains, IpRangeSet BlockedIps, IpRangeSet FreeIps)
{
    public static FilterRules Empty { get; } = new(DomainSet.Empty, DomainSet.Empty, IpRangeSet.Empty, IpRangeSet.Empty);
}

/// <summary>
/// Everything the gate needs to decide about a packet. Immutable: the engine builds a new one every second and
/// swaps it in, so packet threads never see a half-updated table.
/// </summary>
public sealed record GateSnapshot(
    Ipv4Subnet Lan,
    uint Gateway,
    ushort PortalPort,
    FrozenDictionary<uint, AccessEntry> Authorized,
    FilterRules Rules)
{
    public static GateSnapshot Closed(Ipv4Subnet lan, ushort portalPort) =>
        new(lan, Ipv4Subnet.ToUInt32(lan.Address), portalPort, FrozenDictionary<uint, AccessEntry>.Empty, FilterRules.Empty);

    public uint Broadcast => Lan.Network | ~Lan.Mask;

    public bool IsClient(uint address) => (address & Lan.Mask) == Lan.Network && address != Gateway && address != Broadcast;
}

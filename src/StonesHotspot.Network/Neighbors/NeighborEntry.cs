using System.Net;

namespace StonesHotspot.Network.Neighbors;

/// <summary>Windows NL_NEIGHBOR_STATE.</summary>
public enum NeighborState
{
    Unreachable = 0,
    Incomplete = 1,
    Probe = 2,
    Delay = 3,
    Stale = 4,
    Reachable = 5,
    Permanent = 6,
}

/// <summary>One IPv4 neighbour (ARP) entry: a device the PC has recently talked to on an interface.</summary>
public sealed record NeighborEntry(int InterfaceIndex, IPAddress Ip, string Mac, NeighborState State)
{
    /// <summary>True when the device answered recently enough to count as present.</summary>
    public bool IsLive => State is NeighborState.Reachable or NeighborState.Stale or NeighborState.Delay or NeighborState.Probe;
}

public interface INeighborTable
{
    IReadOnlyList<NeighborEntry> GetIpv4Entries();
}

public sealed class EmptyNeighborTable : INeighborTable
{
    public IReadOnlyList<NeighborEntry> GetIpv4Entries() => [];
}

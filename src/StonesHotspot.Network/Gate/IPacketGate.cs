using StonesHotspot.Network.Adapters;

namespace StonesHotspot.Network.Gate;

/// <param name="LanInterfaceIndex">IPv4 interface index of the LAN adapter (WinDivert ifIdx).</param>
/// <param name="Lan">Gateway address and prefix, e.g. 192.168.137.1/24.</param>
public sealed record GateConfig(int LanInterfaceIndex, Ipv4Subnet Lan, ushort PortalPort);

public enum GateState { Stopped, Running, Failed, NotSupported }

public sealed record GateStatus(GateState State, GateConfig? Config, string? Error, DateTime? Since)
{
    public static GateStatus Stopped { get; } = new(GateState.Stopped, null, null, null);
    public bool IsRunning => State == GateState.Running;
}

/// <summary>The packet filter between the customers and the internet.</summary>
public interface IPacketGate
{
    GateStatus Status { get; }

    PacketProcessor Processor { get; }

    /// <summary>
    /// Starts filtering for <paramref name="config"/> (or keeps running if it already does). Returns false when
    /// it cannot start; <see cref="Status"/> then explains why and no filter is active.
    /// </summary>
    bool Start(GateConfig config);

    void Stop();
}

/// <summary>Used where WinDivert does not exist (Linux development, tests).</summary>
public sealed class UnsupportedPacketGate(PacketProcessor processor) : IPacketGate
{
    public GateStatus Status { get; } = new(GateState.NotSupported, null, "The packet gate needs Windows.", null);
    public PacketProcessor Processor { get; } = processor;
    public bool Start(GateConfig config) => false;
    public void Stop() { }
}

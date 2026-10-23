using System.Net;
using StonesHotspot.Network.Adapters;

namespace StonesHotspot.Network.Gate;

/// <summary>WinDivert filter strings; only the customer subnet is ever captured, never the PC's own traffic.</summary>
public static class GateFilters
{
    public static (string Forward, string Local) Build(GateConfig config)
    {
        var lan = config.Lan;
        var first = Format(lan.Network + 1);
        var last = Format((lan.Network | ~lan.Mask) - 1);
        var gateway = lan.Address.ToString();

        var forward =
            $"ip and ((ip.SrcAddr >= {first} and ip.SrcAddr <= {last}) or (ip.DstAddr >= {first} and ip.DstAddr <= {last}))";
        var local =
            $"ip and ifIdx == {config.LanInterfaceIndex} and (" +
            $"(inbound and udp.DstPort == 53 and ip.DstAddr == {gateway} and ip.SrcAddr >= {first} and ip.SrcAddr <= {last}) or " +
            $"(outbound and tcp.SrcPort == {config.PortalPort} and ip.SrcAddr == {gateway} and ip.DstAddr >= {first} and ip.DstAddr <= {last}))";
        return (forward, local);
    }

    private static string Format(uint address) =>
        new IPAddress([(byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address]).ToString();
}

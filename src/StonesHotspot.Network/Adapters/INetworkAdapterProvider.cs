using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StonesHotspot.Network.Adapters;

public interface INetworkAdapterProvider
{
    IReadOnlyList<NetworkAdapterInfo> GetAdapters();
}

/// <summary>Reads the PC's adapters through .NET (works on any OS; on Windows Id is the adapter GUID).</summary>
public sealed class SystemNetworkAdapterProvider : INetworkAdapterProvider
{
    private static readonly NetworkInterfaceType[] Ignored =
        [NetworkInterfaceType.Loopback, NetworkInterfaceType.Tunnel, NetworkInterfaceType.Unknown];

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => !Ignored.Contains(n.NetworkInterfaceType))
            .Select(ToInfo)
            .OfType<NetworkAdapterInfo>()
            .OrderByDescending(a => a.IsUp).ThenBy(a => a.Name)
            .ToList();

    private static NetworkAdapterInfo? ToInfo(NetworkInterface nic)
    {
        try
        {
            var props = nic.GetIPProperties();
            var ipv4 = nic.Supports(NetworkInterfaceComponent.IPv4) ? props.GetIPv4Properties() : null;
            var mac = nic.GetPhysicalAddress().GetAddressBytes();

            return new NetworkAdapterInfo(
                nic.Id,
                nic.Name,
                nic.Description,
                nic.NetworkInterfaceType,
                nic.OperationalStatus == OperationalStatus.Up,
                ipv4?.Index ?? -1,
                mac.Length == 6 ? string.Join(':', mac.Select(b => b.ToString("X2"))) : "",
                props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => new Ipv4Subnet(a.Address, a.PrefixLength))
                    .ToList(),
                props.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(g => g.Address)
                    .ToList());
        }
        catch (NetworkInformationException)
        {
            return null; // adapter vanished while reading it
        }
    }
}

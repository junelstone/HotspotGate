using System.Net.NetworkInformation;

namespace StonesHotspot.Network.Adapters;

public sealed record AdapterSuggestion(NetworkAdapterInfo? Wan, IReadOnlyList<NetworkAdapterInfo> LanCandidates);

/// <summary>Suggests which adapter is the internet (WAN) and which ones could serve the customers (LAN).</summary>
public static class AdapterAdvisor
{
    public static AdapterSuggestion Suggest(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        // WAN: connected, has a default gateway; prefer cable, then Wi-Fi, then the rest (4G modems…).
        var wan = adapters
            .Where(a => a.IsUp && a.HasDefaultGateway && !a.IsMobileHotspot)
            .OrderBy(a => a.Type switch
            {
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 0,
                NetworkInterfaceType.Wireless80211 => 1,
                _ => 2,
            })
            .FirstOrDefault();

        // LAN: anything else that could reach the access point; the Mobile Hotspot adapter and plain
        // Ethernet without a gateway first.
        var lan = adapters
            .Where(a => a.Id != wan?.Id)
            .OrderBy(a => a.IsMobileHotspot ? 0 : !a.HasDefaultGateway && a.Type != NetworkInterfaceType.Wireless80211 ? 1 : 2)
            .ThenByDescending(a => a.IsUp)
            .ToList();

        return new AdapterSuggestion(wan, lan);
    }
}

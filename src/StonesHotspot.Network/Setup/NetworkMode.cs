namespace StonesHotspot.Network.Setup;

public enum NetworkMode
{
    /// <summary>Not configured, or sharing switched off.</summary>
    None,

    /// <summary>Windows Internet Connection Sharing from the WAN adapter to the LAN adapter (gateway 192.168.137.1).</summary>
    Ics,

    /// <summary>
    /// The PC's own Wi-Fi in Windows "Mobile hotspot" mode. Windows shares the internet itself; we only watch
    /// the hotspot adapter.
    /// </summary>
    MobileHotspot,
}

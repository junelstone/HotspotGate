namespace StonesHotspot.Service;

/// <summary>Ports the service listens on (configuration section <c>StonesHotspot</c>); <c>HttpsPort</c> 0 = no HTTPS.</summary>
internal sealed record ListenPorts(int Admin, int Portal, bool LocalhostOnly, int Https = 0)
{
    public IEnumerable<int> All => Admin == Portal ? [Admin] : [Admin, Portal];

    public static ListenPorts From(IConfiguration configuration) => new(
        configuration.GetValue("StonesHotspot:AdminPort", 8080),
        configuration.GetValue("StonesHotspot:PortalPort", 80),
        configuration.GetValue("StonesHotspot:LocalhostOnly", false),
        configuration.GetValue("StonesHotspot:HttpsPort", 0));
}

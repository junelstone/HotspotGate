using StonesHotspot.Network.Adapters;

namespace StonesHotspot.Network.Setup;

public static class NetworkSetupValidator
{
    /// <summary>The subnet Windows ICS always gives the shared (LAN) adapter.</summary>
    public static readonly Ipv4Subnet IcsSubnet = Ipv4Subnet.Parse("192.168.137.1/24");

    /// <summary>Checks a WAN/LAN choice before (and after) applying it.</summary>
    public static IReadOnlyList<NetworkProblem> Validate(
        IReadOnlyList<NetworkAdapterInfo> adapters, string? wanId, string? lanId, bool isAdministrator, bool isWindows)
    {
        var problems = new List<NetworkProblem>();
        if (!isWindows)
            problems.Add(NetworkProblem.Error(NetworkProblemCode.NotWindows));
        if (!isAdministrator)
            problems.Add(NetworkProblem.Error(NetworkProblemCode.NotAdministrator));

        if (string.IsNullOrEmpty(wanId))
            problems.Add(NetworkProblem.Error(NetworkProblemCode.WanNotSelected));
        if (string.IsNullOrEmpty(lanId))
            problems.Add(adapters.Count(a => a.Id != wanId) == 0
                ? NetworkProblem.Error(NetworkProblemCode.NoLanAdapter)
                : NetworkProblem.Error(NetworkProblemCode.LanNotSelected));
        if (string.IsNullOrEmpty(wanId) || string.IsNullOrEmpty(lanId))
            return problems;

        if (string.Equals(wanId, lanId, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(NetworkProblem.Error(NetworkProblemCode.SameAdapter));
            return problems;
        }

        var wan = adapters.FirstOrDefault(a => SameId(a.Id, wanId));
        var lan = adapters.FirstOrDefault(a => SameId(a.Id, lanId));
        if (wan is null)
            problems.Add(NetworkProblem.Error(NetworkProblemCode.AdapterNotFound, wanId));
        if (lan is null)
            problems.Add(NetworkProblem.Error(NetworkProblemCode.AdapterNotFound, lanId));
        if (wan is null || lan is null)
            return problems;

        if (!wan.IsUp)
            problems.Add(NetworkProblem.Warning(NetworkProblemCode.WanDown, wan.Name));
        else if (!wan.HasDefaultGateway)
            problems.Add(NetworkProblem.Warning(NetworkProblemCode.WanNoGateway, wan.Name));

        if (lan.IsMobileHotspot)
        {
            if (!lan.IsUp)
                problems.Add(NetworkProblem.Warning(NetworkProblemCode.MobileHotspotOff, lan.Name));
        }
        else
        {
            if (!lan.IsUp)
                problems.Add(NetworkProblem.Warning(NetworkProblemCode.LanDown, lan.Name));

            // ICS will put the LAN on 192.168.137.0/24: the internet side must not already use it.
            // (Ipv4Subnet is a struct: cast so "none found" is null, not an empty 0.0.0.0/0.)
            if (wan.Addresses.Where(a => a.Overlaps(IcsSubnet)).Cast<Ipv4Subnet?>().FirstOrDefault() is { } clash)
                problems.Add(NetworkProblem.Error(NetworkProblemCode.IcsSubnetConflict, $"{wan.Name}: {clash}"));
        }

        return problems;
    }

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

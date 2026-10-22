namespace StonesHotspot.Network.Setup;

public enum ProblemSeverity { Info, Warning, Error }

/// <summary>Stable problem codes; the UI shows the French/English text <c>Network_Problem_{Code}</c>.</summary>
public enum NetworkProblemCode
{
    NotAdministrator,
    NotWindows,
    NoLanAdapter,
    WanNotSelected,
    LanNotSelected,
    AdapterNotFound,
    SameAdapter,
    WanDown,
    WanNoGateway,
    LanDown,
    IcsSubnetConflict,
    MobileHotspotOff,
    IcsFailed,
    IcsNotActive,
    GatewayIpConflict,
    DevicesOutsideSubnet,
}

/// <param name="Detail">Technical detail (adapter name, Windows error…), shown under the message.</param>
public sealed record NetworkProblem(NetworkProblemCode Code, ProblemSeverity Severity, string? Detail = null)
{
    public static NetworkProblem Error(NetworkProblemCode code, string? detail = null) => new(code, ProblemSeverity.Error, detail);
    public static NetworkProblem Warning(NetworkProblemCode code, string? detail = null) => new(code, ProblemSeverity.Warning, detail);
}

public static class NetworkProblemExtensions
{
    public static bool HasErrors(this IEnumerable<NetworkProblem> problems) => problems.Any(p => p.Severity == ProblemSeverity.Error);
}

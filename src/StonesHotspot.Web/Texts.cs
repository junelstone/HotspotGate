using System.Globalization;
using StonesHotspot.Core.Localization;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Web;

/// <summary>Maps codes and enums to the French/English texts of <see cref="Strings"/>.</summary>
public static class Texts
{
    public static string Problem(NetworkProblem problem) =>
        Strings.ResourceManager.GetString($"Network_Problem_{problem.Code}", CultureInfo.CurrentUICulture) ?? problem.Code.ToString();

    public static string Mode(NetworkMode mode) => mode switch
    {
        NetworkMode.Ics => Strings.Network_Mode_Ics,
        NetworkMode.MobileHotspot => Strings.Network_Mode_MobileHotspot,
        _ => Strings.Network_Mode_None,
    };

    public static string NeighborState(NeighborState state) => state switch
    {
        Network.Neighbors.NeighborState.Reachable => Strings.Devices_State_Active,
        Network.Neighbors.NeighborState.Stale => Strings.Devices_State_Idle,
        _ => Strings.Devices_State_Checking,
    };

    public static string YesNo(bool value) => value ? Strings.Common_Yes : Strings.Common_No;

    public static string CssClass(ProblemSeverity severity) => severity switch
    {
        ProblemSeverity.Error => "error",
        ProblemSeverity.Warning => "warning",
        _ => "info",
    };
}

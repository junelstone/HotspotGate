using StonesHotspot.Core.Rules;
using StonesHotspot.Network.Gate;

namespace StonesHotspot.Web.Admin;

/// <summary>Portal addresses printed on tickets and receipts.</summary>
public static class PortalLinks
{
    public const string FallbackGateway = "192.168.137.1";

    /// <summary>The portal address, printed for customers whose phone cannot scan the QR.</summary>
    public static string Address(PortalLocation portal)
    {
        var gateway = portal.Gateway?.ToString() ?? FallbackGateway;
        return portal.PortalPort == 80 ? $"http://{gateway}/portal" : $"http://{gateway}:{portal.PortalPort}/portal";
    }

    /// <summary>What a code's QR opens: the portal with the code filled in.</summary>
    public static string ForCode(PortalLocation portal, string code) => $"{Address(portal)}?code={VoucherCodeGenerator.Format(code)}";
}

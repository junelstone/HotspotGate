using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace StonesHotspot.Network.Firewall;

/// <param name="Protocol">6 = TCP, 17 = UDP (Windows Firewall numbers).</param>
public sealed record FirewallRule(string Name, int Protocol, int Port, string InterfaceName, string Description);

/// <summary>The inbound rules the hotspot needs, on the customer (LAN) adapter only.</summary>
public static class FirewallPlan
{
    public const string Group = "Stone's Hotspot";
    public const int Tcp = 6, Udp = 17;

    public static IReadOnlyList<FirewallRule> For(string lanInterfaceName, int portalPort, int adminPort, int httpsPort)
    {
        var rules = new List<FirewallRule>
        {
            new($"{Group} — portail (TCP {portalPort})", Tcp, portalPort, lanInterfaceName, "Captive portal for customers"),
            new($"{Group} — tableau de bord (TCP {adminPort})", Tcp, adminPort, lanInterfaceName, "Admin dashboard from the Wi-Fi"),
            new($"{Group} — DNS (UDP 53)", Udp, 53, lanInterfaceName, "DNS answers for customers"),
        };
        if (httpsPort > 0)
            rules.Add(new($"{Group} — HTTPS (TCP {httpsPort})", Tcp, httpsPort, lanInterfaceName, "HTTPS dashboard and portal"));
        return rules;
    }
}

public interface IFirewallManager
{
    /// <summary>Makes the group's rules exactly <paramref name="rules"/> (cheap when nothing changed).</summary>
    void Apply(IReadOnlyList<FirewallRule> rules);
}

/// <summary>Other systems (development, tests): nothing to do.</summary>
public sealed class NoFirewall : IFirewallManager
{
    public IReadOnlyList<FirewallRule> Applied { get; private set; } = [];

    public void Apply(IReadOnlyList<FirewallRule> rules) => Applied = rules;
}

/// <summary>
/// Windows Firewall through its COM API (HNetCfg.FwPolicy2): inbound allow rules in the "Stone's Hotspot" group,
/// limited to the LAN adapter, so nothing is opened on the internet side. The uninstaller removes the group.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFirewall(ILogger<WindowsFirewall> logger) : IFirewallManager
{
    private const int Inbound = 1, Allow = 1, AllProfiles = 0x7FFFFFFF;
    private IReadOnlyList<FirewallRule> _applied = [];
    private DateTime _lastCheck = DateTime.MinValue;

    public void Apply(IReadOnlyList<FirewallRule> rules)
    {
        // Rechecked every 10 minutes (someone may delete a rule by hand), at once when the plan changes.
        if (rules.SequenceEqual(_applied) && DateTime.UtcNow - _lastCheck < TimeSpan.FromMinutes(10))
            return;
        _lastCheck = DateTime.UtcNow;
        try
        {
            dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
            var existing = new List<string>();
            foreach (dynamic rule in policy.Rules)
            {
                if ((string?)rule.Grouping == FirewallPlan.Group)
                    existing.Add((string)rule.Name);
            }
            if (rules.SequenceEqual(_applied) && existing.Count == rules.Count && rules.All(r => existing.Contains(r.Name)))
                return;

            foreach (var name in existing.Distinct())
                policy.Rules.Remove(name);
            foreach (var r in rules)
            {
                dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
                rule.Name = r.Name;
                rule.Description = r.Description;
                rule.Grouping = FirewallPlan.Group;
                rule.Protocol = r.Protocol;
                rule.LocalPorts = r.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rule.Direction = Inbound;
                rule.Action = Allow;
                rule.Profiles = AllProfiles;
                rule.Interfaces = new object[] { r.InterfaceName };
                rule.Enabled = true;
                policy.Rules.Add(rule);
            }
            _applied = rules;
            logger.LogInformation("Firewall rules set on {Adapter}: {Count}", rules.FirstOrDefault()?.InterfaceName, rules.Count);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            logger.LogWarning("Could not set the firewall rules: {Error}", ex.Message);
        }
    }
}

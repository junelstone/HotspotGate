using System.Collections.Frozen;

namespace StonesHotspot.Network.Gate;

/// <summary>
/// Fair-share settings: the internet line's capacity split evenly between the devices actually using it, never below
/// <paramref name="MinKbps"/> nor above <paramref name="MaxKbps"/> per device.
/// </summary>
public sealed record FairShare(bool Enabled, int TotalDownKbps, int TotalUpKbps, int MinKbps, int MaxKbps)
{
    public static FairShare Off { get; } = new(false, 0, 0, 0, 0);
}

public static class BandwidthPolicy
{
    /// <summary>
    /// Final per-device limits: the plan's limit, lowered to the fair share when fair share is on.
    /// <paramref name="activeIps"/> are the authorised devices with recent traffic.
    /// </summary>
    public static FrozenDictionary<uint, AccessEntry> Apply(
        FrozenDictionary<uint, AccessEntry> table, FairShare fairShare, IReadOnlyCollection<uint> activeIps)
    {
        if (!fairShare.Enabled || table.Count == 0)
            return table;

        var active = Math.Max(1, activeIps.Count(table.ContainsKey));
        var down = Share(fairShare.TotalDownKbps, active, fairShare);
        var up = Share(fairShare.TotalUpKbps, active, fairShare);

        return table.ToFrozenDictionary(
            kv => kv.Key,
            kv => kv.Value with { DownKbps = Lower(kv.Value.DownKbps, down), UpKbps = Lower(kv.Value.UpKbps, up) });
    }

    /// <summary>The share of one device, or null when no total is configured for that direction.</summary>
    public static int? Share(int totalKbps, int activeDevices, FairShare settings)
    {
        if (totalKbps <= 0)
            return null;
        var share = totalKbps / Math.Max(1, activeDevices);
        if (settings.MaxKbps > 0)
            share = Math.Min(share, settings.MaxKbps);
        return Math.Max(share, Math.Max(1, settings.MinKbps));
    }

    private static int? Lower(int? planLimit, int? share) => (planLimit, share) switch
    {
        ({ } p, { } s) => Math.Min(p, s),
        (null, var s) => s,
        (var p, null) => p,
    };
}

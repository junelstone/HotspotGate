namespace StonesHotspot.Core.Data.Entities;

/// <summary>A customer device, identified by its MAC address (format AA:BB:CC:DD:EE:FF).</summary>
public class Device
{
    public int Id { get; set; }
    public string Mac { get; set; } = "";
    public string? LastIp { get; set; }
    public string? Hostname { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    /// <summary>Always gets internet without logging in.</summary>
    public bool Whitelisted { get; set; }

    /// <summary>Never gets internet and cannot log in.</summary>
    public bool Blocked { get; set; }

    public string? Note { get; set; }

    // Wrong code / password protection.
    public int FailedLoginCount { get; set; }
    public DateTime? FailedLoginWindowStart { get; set; }
    public DateTime? LockedUntil { get; set; }
}

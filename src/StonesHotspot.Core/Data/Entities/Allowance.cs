namespace StonesHotspot.Core.Data.Entities;

/// <summary>
/// What is left of a voucher or of an account's current package. Owned by <see cref="Voucher"/> and
/// <see cref="UserAccount"/>; all values are null until the first login.
/// </summary>
public class Allowance
{
    public DateTime? StartedAt { get; set; }

    /// <summary>End of the validity window (and, for wall-clock time tariffs, of the time quota).</summary>
    public DateTime? ExpiresAt { get; set; }

    public long? RemainingSeconds { get; set; }
    public long? RemainingBytes { get; set; }

    /// <summary>Up to when time has been taken from this allowance (usage-only countdown).</summary>
    public DateTime? LastConsumedAt { get; set; }

    public bool IsStarted => StartedAt is not null;
}

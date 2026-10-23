namespace StonesHotspot.Licensing;

public enum LicenceKind { Trial, Licence }

/// <param name="Edition">Effective edition right now (<see cref="Edition.None"/> once stopped).</param>
/// <param name="ExpiresAt">End of the trial or licence (UTC); null when there is none.</param>
/// <param name="InGracePeriod">Expired or revoked, but still inside the 7-day grace period.</param>
/// <param name="HotspotAllowed">False after the grace period: customers can no longer log in.</param>
public sealed record LicenseStatus(
    Edition Edition,
    DateTimeOffset? ExpiresAt,
    bool InGracePeriod,
    bool HotspotAllowed)
{
    public LicenceKind Kind { get; init; } = LicenceKind.Trial;
    public uint? LicenceId { get; init; }
    public string? Customer { get; init; }

    /// <summary>The edition of the licence (or Trial), also once stopped.</summary>
    public Edition LicensedEdition { get; init; } = Edition.Trial;

    public string MachineId { get; init; } = "";
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>When the hotspot stops (end of the grace period).</summary>
    public DateTimeOffset? StopsAt { get; init; }

    /// <summary>The PC clock was set back more than a day: durations count from the latest time seen.</summary>
    public bool ClockProblem { get; init; }

    public DateTimeOffset? LastCheckIn { get; init; }

    /// <summary>Show a warning: 30, 14, 7, 3 or 1 day(s) or less before the end.</summary>
    public int? WarningDaysLeft { get; init; }

    public bool Stopped => !HotspotAllowed;

    public int? DaysLeft(DateTimeOffset now) =>
        ExpiresAt is { } end ? Math.Max(0, (int)Math.Ceiling((end - now).TotalDays)) : null;
}

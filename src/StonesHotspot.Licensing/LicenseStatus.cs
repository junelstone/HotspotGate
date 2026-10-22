namespace StonesHotspot.Licensing;

/// <param name="Edition">Effective edition right now.</param>
/// <param name="ExpiresAt">End of the trial or licence (UTC); null when there is none.</param>
/// <param name="InGracePeriod">Expired or revoked, but still inside the 7-day grace period.</param>
/// <param name="HotspotAllowed">False after the grace period: customers can no longer log in.</param>
public sealed record LicenseStatus(
    Edition Edition,
    DateTimeOffset? ExpiresAt,
    bool InGracePeriod,
    bool HotspotAllowed)
{
    public int? DaysLeft(DateTimeOffset now) =>
        ExpiresAt is { } end ? Math.Max(0, (int)Math.Ceiling((end - now).TotalDays)) : null;
}

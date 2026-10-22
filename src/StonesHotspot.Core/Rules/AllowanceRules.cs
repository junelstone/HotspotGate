using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Rules;

/// <summary>Pure rules for starting, consuming and checking the time/data left on a voucher or account.</summary>
public static class AllowanceRules
{
    public const long BytesPerMb = 1024 * 1024;

    /// <summary>Starts the allowance at first login: validity window and quota begin now.</summary>
    public static void Start(Allowance allowance, Tariff tariff, DateTime now)
    {
        allowance.StartedAt = now;
        allowance.LastConsumedAt = now;
        allowance.RemainingSeconds = null;
        allowance.RemainingBytes = null;

        DateTime? expires = tariff.ValidityHours is { } hours ? now.AddHours(hours) : null;
        if (tariff.ValidUntil is { } fixedEnd)
            expires = Min(expires, fixedEnd);

        switch (tariff.Type)
        {
            case TariffType.TimeBased when tariff.CountdownMode == CountdownMode.WallClock:
                expires = Min(expires, now.AddMinutes(tariff.Quota));
                allowance.RemainingSeconds = SecondsUntil(expires!.Value, now);
                break;
            case TariffType.TimeBased:
                allowance.RemainingSeconds = tariff.Quota * 60;
                break;
            case TariffType.DataBased:
                allowance.RemainingBytes = tariff.Quota * BytesPerMb;
                break;
        }

        allowance.ExpiresAt = expires;
    }

    /// <summary>
    /// Takes time and data. Usage-only time is taken in whole seconds since <see cref="Allowance.LastConsumedAt"/>;
    /// call it once per allowance per tick, not once per device.
    /// </summary>
    public static void Consume(Allowance allowance, Tariff tariff, long bytes, DateTime now)
    {
        if (tariff.Type == TariffType.TimeBased)
        {
            if (tariff.CountdownMode == CountdownMode.WallClock)
            {
                allowance.RemainingSeconds = allowance.ExpiresAt is { } end ? SecondsUntil(end, now) : allowance.RemainingSeconds;
            }
            else if (allowance.LastConsumedAt is { } last && now > last)
            {
                var elapsed = (long)(now - last).TotalSeconds;
                allowance.RemainingSeconds = Math.Max(0, (allowance.RemainingSeconds ?? 0) - elapsed);
                allowance.LastConsumedAt = last.AddSeconds(elapsed);
            }
        }

        if (tariff.Type == TariffType.DataBased && bytes > 0)
            allowance.RemainingBytes = Math.Max(0, (allowance.RemainingBytes ?? 0) - bytes);
    }

    /// <summary>
    /// Why the allowance can no longer be used, or null if it still can. A passed end date (validity, or the
    /// end of a wall-clock time quota) is <see cref="SessionEndReason.Expired"/>; no time or data left is
    /// <see cref="SessionEndReason.QuotaUsed"/>.
    /// </summary>
    public static SessionEndReason? Check(Allowance allowance, DateTime now)
    {
        if (!allowance.IsStarted)
            return null;
        if (allowance.ExpiresAt is { } expires && expires <= now)
            return SessionEndReason.Expired;
        if (allowance.RemainingSeconds is <= 0 || allowance.RemainingBytes is <= 0)
            return SessionEndReason.QuotaUsed;
        return null;
    }

    /// <summary>Called at start-up so the time the PC was off is not charged to usage-only tariffs.</summary>
    public static void Resume(Allowance allowance, DateTime now) => allowance.LastConsumedAt = now;

    private static long SecondsUntil(DateTime end, DateTime now) => Math.Max(0, (long)Math.Ceiling((end - now).TotalSeconds));

    private static DateTime? Min(DateTime? a, DateTime b) => a is { } x && x < b ? x : b;
}

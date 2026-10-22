using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Rules;

namespace StonesHotspot.Tests;

public class AllowanceRulesTests
{
    private static readonly DateTime T0 = new(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);

    private static Tariff Time(long minutes, CountdownMode mode = CountdownMode.UsageOnly, int? validityHours = null) =>
        new() { Type = TariffType.TimeBased, Quota = minutes, CountdownMode = mode, ValidityHours = validityHours };

    [Fact]
    public void Usage_only_time_tariff_starts_with_full_quota_and_validity_window()
    {
        var allowance = new Allowance();
        AllowanceRules.Start(allowance, Time(30, validityHours: 168), T0);

        Assert.Equal(30 * 60, allowance.RemainingSeconds);
        Assert.Equal(T0.AddDays(7), allowance.ExpiresAt);
        Assert.Null(allowance.RemainingBytes);
    }

    [Fact]
    public void Wall_clock_time_ends_at_first_login_plus_quota_or_validity_whichever_is_first()
    {
        var a = new Allowance();
        AllowanceRules.Start(a, Time(60, CountdownMode.WallClock, validityHours: 24), T0);
        Assert.Equal(T0.AddHours(1), a.ExpiresAt);

        var b = new Allowance();
        AllowanceRules.Start(b, Time(600, CountdownMode.WallClock, validityHours: 2), T0);
        Assert.Equal(T0.AddHours(2), b.ExpiresAt);
        Assert.Equal(2 * 3600, b.RemainingSeconds);
    }

    [Fact]
    public void Fixed_end_date_shortens_validity()
    {
        var tariff = Time(60, validityHours: 48);
        tariff.ValidUntil = T0.AddHours(5);
        var allowance = new Allowance();
        AllowanceRules.Start(allowance, tariff, T0);
        Assert.Equal(T0.AddHours(5), allowance.ExpiresAt);
    }

    [Fact]
    public void Data_tariff_counts_megabytes_as_mebibytes()
    {
        var allowance = new Allowance();
        AllowanceRules.Start(allowance, new Tariff { Type = TariffType.DataBased, Quota = 500 }, T0);
        Assert.Equal(500L * 1024 * 1024, allowance.RemainingBytes);
        Assert.Null(allowance.RemainingSeconds);
        Assert.Null(allowance.ExpiresAt);
    }

    [Fact]
    public void Usage_only_consumes_whole_seconds_and_keeps_the_fraction()
    {
        var tariff = Time(1);
        var allowance = new Allowance();
        AllowanceRules.Start(allowance, tariff, T0);

        AllowanceRules.Consume(allowance, tariff, 0, T0.AddMilliseconds(1500));
        Assert.Equal(59, allowance.RemainingSeconds);
        AllowanceRules.Consume(allowance, tariff, 0, T0.AddMilliseconds(2000));
        Assert.Equal(58, allowance.RemainingSeconds);
    }

    [Fact]
    public void Check_reports_quota_used_and_expired()
    {
        var tariff = Time(1, validityHours: 1);
        var allowance = new Allowance();
        Assert.Null(AllowanceRules.Check(allowance, T0)); // not started

        AllowanceRules.Start(allowance, tariff, T0);
        Assert.Null(AllowanceRules.Check(allowance, T0.AddSeconds(30)));

        AllowanceRules.Consume(allowance, tariff, 0, T0.AddSeconds(60));
        Assert.Equal(SessionEndReason.QuotaUsed, AllowanceRules.Check(allowance, T0.AddSeconds(60)));
        Assert.Equal(SessionEndReason.Expired, AllowanceRules.Check(allowance, T0.AddHours(1)));
    }
}

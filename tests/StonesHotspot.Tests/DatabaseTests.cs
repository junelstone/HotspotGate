using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class DatabaseTests
{
    [Fact]
    public async Task Fresh_database_is_migrated_and_seeded()
    {
        await using var host = await TestHost.CreateAsync();

        Assert.False(string.IsNullOrWhiteSpace(host.InitialAdminPassword));
        var admin = await host.AdminAsync();
        Assert.Equal(StaffRole.Admin, admin.Role);
        Assert.True(admin.MustChangePassword);
        Assert.DoesNotContain(host.InitialAdminPassword!, admin.PasswordHash);

        var tariffs = await host.Query(db => db.Tariffs.OrderBy(t => t.Id).ToListAsync());
        Assert.Equal(["30 minutes", "1 heure", "Pass 24 heures"], tariffs.Select(t => t.Name));
        Assert.Equal(new Xaf(1000), tariffs[2].Price);

        Assert.Equal("fr", await host.Run<SettingsService, string?>(s => s.GetAsync(SettingKeys.DefaultLanguage)));
        Assert.Empty(await host.Query(db => db.Database.GetPendingMigrationsAsync()));
    }

    [Fact]
    public async Task Initialising_again_keeps_data_and_creates_no_new_admin()
    {
        await using var host = await TestHost.CreateAsync();
        await host.Run<SettingsService>(s => s.SetAsync(SettingKeys.BusinessName, "Cyber Stone", null));

        var second = await host.Run<DatabaseInitializer, DatabaseInitResult>(i => i.InitializeAsync());

        Assert.Null(second.InitialAdminPassword);
        Assert.Equal(1, await host.Query(db => db.Staff.CountAsync()));
        Assert.Equal(3, await host.Query(db => db.Tariffs.CountAsync()));
        Assert.Equal("Cyber Stone", await host.Run<SettingsService, string?>(s => s.GetAsync(SettingKeys.BusinessName)));
    }

    [Fact]
    public async Task Dates_come_back_as_utc_including_nullable_ones()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(validityHours: 5));
        await host.LoginAsync(TestHost.PhoneMac, voucher.Code);

        var stored = await host.GetVoucherAsync(voucher.Id);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.FirstUsedAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.Allowance.ExpiresAt!.Value.Kind);
        Assert.Equal(host.Now.AddHours(5), stored.Allowance.ExpiresAt);
    }

    [Fact]
    public async Task Setting_changes_are_audited()
    {
        await using var host = await TestHost.CreateAsync();
        var admin = await host.AdminAsync();
        await host.Run<SettingsService>(s => s.SetAsync(SettingKeys.DefaultIdleTimeoutMinutes, "15", admin));

        var entry = await host.Query(db => db.AuditLogs.SingleAsync(a => a.Action == "Setting.Changed"));
        Assert.Equal("admin", entry.Actor);
        Assert.Contains("'10' → '15'", entry.Details);
        Assert.Equal(15, await host.Run<SettingsService, int>(s => s.GetIntAsync(SettingKeys.DefaultIdleTimeoutMinutes, 10)));
    }
}

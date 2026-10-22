using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class TariffAndStaffTests
{
    public static TheoryData<Tariff> InvalidTariffs => new()
    {
        new Tariff { Name = "", Type = TariffType.TimeBased, Quota = 30 },
        new Tariff { Name = "neg", Type = TariffType.TimeBased, Quota = 30, Price = new Xaf(-1) },
        new Tariff { Name = "noquota", Type = TariffType.DataBased, Quota = 0 },
        new Tariff { Name = "devices", Type = TariffType.TimeBased, Quota = 30, MaxDevices = 0 },
        new Tariff { Name = "speed", Type = TariffType.TimeBased, Quota = 30, DownloadKbps = 0 },
        new Tariff { Name = "forever", Type = TariffType.Unlimited },
    };

    [Theory]
    [MemberData(nameof(InvalidTariffs))]
    public async Task Invalid_tariffs_are_rejected(Tariff tariff)
    {
        await using var host = await TestHost.CreateAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<TariffService, Tariff>(s => s.CreateAsync(tariff, null)));
        Assert.Equal(ErrorCodes.Invalid, ex.Code);
    }

    [Fact]
    public async Task Tariff_in_use_cannot_be_deleted_but_can_be_deactivated()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync(name: "Une heure");
        await host.CreateVoucherAsync(tariff);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<TariffService>(s => s.DeleteAsync(tariff.Id, null)));
        Assert.Equal(ErrorCodes.TariffInUse, ex.Code);

        tariff.IsActive = false;
        await host.Run<TariffService, Tariff>(s => s.UpdateAsync(tariff.Id, tariff, null));
        Assert.DoesNotContain(await host.Run<TariffService, List<Tariff>>(s => s.ListAsync()), t => t.Id == tariff.Id);
    }

    [Fact]
    public async Task Unused_tariff_can_be_deleted_and_names_are_unique()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync(name: "Promo");
        await Assert.ThrowsAsync<BusinessRuleException>(() => host.CreateTariffAsync(name: "Promo"));

        await host.Run<TariffService>(s => s.DeleteAsync(tariff.Id, null));
        Assert.Null(await host.Run<TariffService, Tariff?>(s => s.FindAsync(tariff.Id)));
    }

    [Fact]
    public async Task Staff_must_change_the_initial_password()
    {
        await using var host = await TestHost.CreateAsync();
        var password = host.InitialAdminPassword!;

        var admin = await host.Run<StaffService, Staff?>(s => s.AuthenticateAsync("ADMIN", password));
        Assert.NotNull(admin);
        Assert.True(admin.MustChangePassword);
        Assert.Null(await host.Run<StaffService, Staff?>(s => s.AuthenticateAsync("admin", "wrong-password")));

        await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<StaffService>(s => s.ChangePasswordAsync(admin.Id, password, "short")));
        await host.Run<StaffService>(s => s.ChangePasswordAsync(admin.Id, password, "Nouveau-Mot-2026"));

        Assert.False((await host.AdminAsync()).MustChangePassword);
        Assert.NotNull(await host.Run<StaffService, Staff?>(s => s.AuthenticateAsync("admin", "Nouveau-Mot-2026")));
    }

    [Fact]
    public async Task Disabled_staff_cannot_log_in()
    {
        await using var host = await TestHost.CreateAsync();
        var cashier = await host.CreateCashierAsync();
        await host.Run<StonesHotspot.Core.Data.HotspotDbContext>(async db =>
        {
            (await db.Staff.SingleAsync(s => s.Id == cashier.Id)).Enabled = false;
            await db.SaveChangesAsync();
        });

        Assert.Null(await host.Run<StaffService, Staff?>(s => s.AuthenticateAsync("caissier", "motdepasse1")));
    }
}

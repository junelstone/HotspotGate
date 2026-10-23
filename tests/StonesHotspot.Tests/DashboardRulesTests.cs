using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

/// <summary>Core rules added for the dashboard.</summary>
public class DashboardRulesTests
{
    [Fact]
    public async Task Adding_time_extends_the_quota_and_the_end_date()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(quota: 30, validityHours: 1));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;

        Assert.True(await host.Run<SessionService, bool>(s => s.AddTimeAsync(session.Id, 30, null)));

        var stored = await host.GetVoucherAsync(voucher.Id);
        Assert.Equal(60 * 60, stored.Allowance.RemainingSeconds);
        Assert.Equal(host.Now.AddMinutes(90), stored.Allowance.ExpiresAt);
        Assert.Equal(1, await host.Query(db => db.AuditLogs.CountAsync(a => a.Action == "Session.TimeAdded")));
    }

    [Fact]
    public async Task Adding_time_to_a_data_plan_without_end_date_is_refused()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(TariffType.DataBased, quota: 100));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;

        Assert.False(await host.Run<SessionService, bool>(s => s.AddTimeAsync(session.Id, 30, null)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<SessionService, bool>(s => s.AddTimeAsync(session.Id, 0, null)));
    }

    [Fact]
    public async Task Cash_sale_of_new_codes_creates_and_sells_them_on_one_receipt()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync(price: 300);
        var admin = await host.AdminAsync();

        var (receipt, vouchers) = await host.Run<VoucherService, (string, List<Voucher>)>(s => s.SellNewAsync(tariff.Id, 3, PaymentMethod.Cash, admin));

        Assert.Equal(3, vouchers.Count);
        Assert.Equal(3, (await host.Run<VoucherService, List<Voucher>>(s => s.ListByReceiptAsync(receipt))).Count);
        Assert.Equal(new Xaf(900), Xaf.Sum(await host.Query(db => db.Transactions.Where(t => t.ReceiptNumber == receipt).Select(t => t.Amount).ToListAsync())));
        Assert.All(await host.Query(db => db.Vouchers.ToListAsync()), v => Assert.Equal(VoucherStatus.Sold, v.Status));
    }

    [Fact]
    public async Task Failed_sale_leaves_no_orphan_codes()
    {
        await using var host = await TestHost.CreateAsync();
        var admin = await host.AdminAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            host.Run<VoucherService, (string, List<Voucher>)>(s => s.SellNewAsync(9999, 2, PaymentMethod.Cash, admin)));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            host.Run<VoucherService, (string, List<Voucher>)>(s => s.SellNewAsync(1, 2, PaymentMethod.AccountBalance, admin)));

        Assert.Equal(0, await host.Query(db => db.Vouchers.CountAsync()));
    }

    [Fact]
    public async Task Last_admin_and_yourself_cannot_be_disabled()
    {
        await using var host = await TestHost.CreateAsync();
        var admin = await host.AdminAsync();
        var cashier = await host.CreateCashierAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<StaffService>(s => s.SetEnabledAsync(admin.Id, false, admin)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<StaffService>(s => s.SetEnabledAsync(admin.Id, false, cashier)));

        await host.Run<StaffService>(s => s.SetEnabledAsync(cashier.Id, false, admin));
        Assert.False((await host.Run<StaffService, Staff?>(s => s.FindAsync(cashier.Id)))!.Enabled);
    }

    [Fact]
    public async Task Accounts_are_found_by_name_phone_or_username()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync();
        await host.Run<AccountService, UserAccount>(s => s.CreateAsync(new UserAccount { Username = "jmb", FirstName = "Jean-Marc", Phone = "+242061112233", TariffId = tariff.Id }, "1234", null));
        await host.Run<AccountService, UserAccount>(s => s.CreateAsync(new UserAccount { Username = "awa", TariffId = tariff.Id }, "1234", null));

        Assert.Equal("jmb", Assert.Single(await host.Run<AccountService, List<UserAccount>>(s => s.ListAsync("marc"))).Username);
        Assert.Equal("jmb", Assert.Single(await host.Run<AccountService, List<UserAccount>>(s => s.ListAsync("1112233"))).Username);
        Assert.Equal(2, (await host.Run<AccountService, List<UserAccount>>(s => s.ListAsync())).Count);
    }
}

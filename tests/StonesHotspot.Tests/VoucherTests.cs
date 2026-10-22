using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class VoucherTests
{
    [Fact]
    public async Task Batch_generation_creates_unique_new_codes()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync();

        var vouchers = await host.Run<VoucherService, List<Voucher>>(s => s.GenerateBatchAsync(tariff.Id, 300, "Lot 1", false, null));

        Assert.Equal(300, vouchers.Count);
        Assert.Equal(300, vouchers.Select(v => v.Code).Distinct().Count());
        Assert.All(vouchers, v => Assert.Equal(VoucherStatus.New, v.Status));
        Assert.Equal(300, await host.Query(db => db.Vouchers.CountAsync(v => v.BatchName == "Lot 1")));
        Assert.Equal(1, await host.Query(db => db.AuditLogs.CountAsync(a => a.Action == "Vouchers.Generated")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Batch_size_is_limited(int count)
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            host.Run<VoucherService, List<Voucher>>(s => s.GenerateBatchAsync(tariff.Id, count, "x", false, null)));
    }

    [Fact]
    public async Task Activation_happens_at_first_login_not_at_creation_or_sale()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync(quota: 30, validityHours: 24);
        var voucher = await host.CreateVoucherAsync(tariff);
        var admin = await host.AdminAsync();
        await host.Run<VoucherService>(s => s.SellAsync([voucher.Id], PaymentMethod.Cash, admin));

        host.Advance(TimeSpan.FromDays(3)); // sold, kept in a pocket for 3 days
        var result = await host.LoginAsync(TestHost.PhoneMac, voucher.Code.ToLowerInvariant());

        Assert.True(result.Succeeded);
        var activated = await host.GetVoucherAsync(voucher.Id);
        Assert.Equal(VoucherStatus.Active, activated.Status);
        Assert.Equal(host.Now, activated.FirstUsedAt);
        Assert.Equal(host.Now.AddHours(24), activated.Allowance.ExpiresAt);
        Assert.Equal(30 * 60, activated.Allowance.RemainingSeconds);
    }

    [Fact]
    public async Task Code_typed_with_dash_and_lower_case_is_accepted()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync());

        var result = await host.LoginAsync(TestHost.PhoneMac, VoucherCodeGenerator.Format(voucher.Code).ToLowerInvariant());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Selling_records_one_transaction_per_voucher_on_one_receipt()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync(price: 500);
        var vouchers = await host.Run<VoucherService, List<Voucher>>(s => s.GenerateBatchAsync(tariff.Id, 3, "b", false, null));
        var admin = await host.AdminAsync();

        var receipt = await host.Run<VoucherService, string>(s => s.SellAsync(vouchers.Select(v => v.Id).ToList(), PaymentMethod.Cash, admin));

        var transactions = await host.Query(db => db.Transactions.Where(t => t.ReceiptNumber == receipt).ToListAsync());
        Assert.Equal(3, transactions.Count);
        Assert.All(transactions, t => Assert.Equal(new Xaf(500), t.Amount));
        Assert.Equal("2026-000001", receipt);
        Assert.All(await host.Query(db => db.Vouchers.ToListAsync()), v => Assert.Equal(VoucherStatus.Sold, v.Status));

        var again = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            host.Run<VoucherService, string>(s => s.SellAsync([vouchers[0].Id], PaymentMethod.Cash, admin)));
        Assert.Equal(ErrorCodes.VoucherNotSellable, again.Code);
    }

    [Fact]
    public async Task Disabled_voucher_is_refused()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync());
        await host.Run<VoucherService>(s => s.DisableAsync(voucher.Id, null));

        Assert.Equal(LoginStatus.VoucherDisabled, (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Status);
    }

    [Fact]
    public async Task Only_unused_vouchers_can_be_deleted()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync();
        var used = await host.CreateVoucherAsync(tariff);
        var unused = await host.CreateVoucherAsync(tariff);
        await host.LoginAsync(TestHost.PhoneMac, used.Code);

        var deleted = await host.Run<VoucherService, int>(s => s.DeleteUnusedAsync([used.Id, unused.Id], null));

        Assert.Equal(1, deleted);
        Assert.Equal(VoucherStatus.Active, (await host.GetVoucherAsync(used.Id)).Status);
    }

    [Fact]
    public async Task Single_use_voucher_cannot_be_reused_after_logout()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(), singleUse: true);
        var login = await host.LoginAsync(TestHost.PhoneMac, voucher.Code);

        await host.Run<SessionService, bool>(s => s.EndAsync(login.Session!.Id, SessionEndReason.Logout));

        Assert.Equal(VoucherStatus.Expired, (await host.GetVoucherAsync(voucher.Id)).Status);
        Assert.Equal(LoginStatus.VoucherExpired, (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Status);
    }
}

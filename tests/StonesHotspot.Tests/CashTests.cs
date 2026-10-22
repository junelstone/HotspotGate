using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class CashTests
{
    [Fact]
    public async Task Shift_cash_totals_match_the_cash_transactions_of_the_shift()
    {
        await using var host = await TestHost.CreateAsync();
        var cashier = await host.CreateCashierAsync();
        var tariff = await host.CreateTariffAsync(price: 500);
        var vouchers = await host.Run<VoucherService, List<Voucher>>(s => s.GenerateBatchAsync(tariff.Id, 4, "lot", false, null));
        var account = await host.Run<AccountService, UserAccount>(s =>
            s.CreateAsync(new UserAccount { Username = "awa", TariffId = tariff.Id }, "secret1", null));

        // Sale before the shift: not part of it.
        await host.Run<VoucherService, string>(s => s.SellAsync([vouchers[0].Id], PaymentMethod.Cash, cashier));

        var shift = await host.Run<CashService, Shift>(s => s.OpenShiftAsync(cashier, new Xaf(5000)));
        await host.Run<VoucherService, string>(s => s.SellAsync([vouchers[1].Id, vouchers[2].Id], PaymentMethod.Cash, cashier));
        await host.Run<VoucherService, string>(s => s.SellAsync([vouchers[3].Id], PaymentMethod.AirtelMoney, cashier));
        await host.Run<AccountService, string>(s => s.DepositAsync(account.Id, new Xaf(1500), PaymentMethod.Cash, cashier));

        var report = await host.Run<CashService, ShiftReport>(s => s.CloseShiftAsync(shift.Id, new Xaf(7400), cashier));

        var cashInShift = await host.Query(db => db.Transactions
            .Where(t => t.ShiftId == shift.Id && t.PaymentMethod == PaymentMethod.Cash).ToListAsync());
        Assert.Equal(Xaf.Sum(cashInShift.Select(t => t.Amount)), report.CashTransactionsTotal);
        Assert.Equal(new Xaf(2500), report.CashTransactionsTotal);          // 2 × 500 + 1 500
        Assert.Equal(new Xaf(7500), report.ExpectedCash);                   // 5 000 opening + 2 500
        Assert.Equal(new Xaf(7400), report.CountedCash);
        Assert.Equal(new Xaf(-100), report.Difference);                     // 100 FCFA missing
        Assert.Equal(new Xaf(500), report.TotalsByPaymentMethod[PaymentMethod.AirtelMoney]);
        Assert.Equal(new Xaf(1500), report.TotalsByType[TransactionType.VoucherSale]);
        Assert.Equal(4, report.TransactionCount);

        var stored = await host.Query(db => db.Shifts.SingleAsync());
        Assert.False(stored.IsOpen);
        Assert.Equal(new Xaf(7500), stored.ExpectedClosingCash);
    }

    [Fact]
    public async Task Only_one_open_shift_per_staff_member()
    {
        await using var host = await TestHost.CreateAsync();
        var cashier = await host.CreateCashierAsync();
        await host.Run<CashService, Shift>(s => s.OpenShiftAsync(cashier, Xaf.Zero));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<CashService, Shift>(s => s.OpenShiftAsync(cashier, Xaf.Zero)));
        Assert.Equal(ErrorCodes.ShiftAlreadyOpen, ex.Code);
    }

    [Fact]
    public async Task Cashier_cannot_close_someone_elses_shift_but_admin_can()
    {
        await using var host = await TestHost.CreateAsync();
        var first = await host.CreateCashierAsync("caissier1");
        var second = await host.CreateCashierAsync("caissier2");
        var shift = await host.Run<CashService, Shift>(s => s.OpenShiftAsync(first, new Xaf(1000)));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            host.Run<CashService, ShiftReport>(s => s.CloseShiftAsync(shift.Id, new Xaf(1000), second)));
        Assert.Equal(ErrorCodes.NotAllowed, ex.Code);

        var admin = await host.AdminAsync();
        var report = await host.Run<CashService, ShiftReport>(s => s.CloseShiftAsync(shift.Id, new Xaf(1000), admin));
        Assert.Equal(Xaf.Zero, report.Difference);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            host.Run<CashService, ShiftReport>(s => s.CloseShiftAsync(shift.Id, new Xaf(1000), admin)));
    }

    [Fact]
    public async Task Receipt_numbers_are_sequential_and_restart_each_year()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync();
        var admin = await host.AdminAsync();
        async Task<string> SellOne()
        {
            var v = await host.CreateVoucherAsync(tariff);
            return await host.Run<VoucherService, string>(s => s.SellAsync([v.Id], PaymentMethod.Cash, admin));
        }

        Assert.Equal("2026-000001", await SellOne());
        Assert.Equal("2026-000002", await SellOne());
        host.Advance(TimeSpan.FromDays(366));
        Assert.Equal("2027-000001", await SellOne());
    }
}

using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class AccountTests
{
    private static async Task<UserAccount> CreateAccountAsync(TestHost host, Tariff tariff, int overdraft = 0) =>
        await host.Run<AccountService, UserAccount>(s => s.CreateAsync(new UserAccount
        {
            Username = "jean",
            TariffId = tariff.Id,
            Phone = "+242060000000",
            AllowedOverdraft = new Xaf(overdraft),
        }, "secret1", null));

    private static Task<LoginResult> LoginAsync(TestHost host, string password = "secret1", string mac = TestHost.PhoneMac) =>
        host.Run<SessionService, LoginResult>(s => s.LoginWithAccountAsync(mac, "192.168.137.20", "JEAN", password));

    private static async Task DepositAsync(TestHost host, UserAccount account, int amount)
    {
        var admin = await host.AdminAsync();
        await host.Run<AccountService, string>(s => s.DepositAsync(account.Id, new Xaf(amount), PaymentMethod.Cash, admin));
    }

    private static Task<UserAccount> GetAsync(TestHost host) => host.Query(db => db.UserAccounts.AsNoTracking().SingleAsync());

    [Fact]
    public async Task Deposit_adds_to_balance_and_records_a_transaction()
    {
        await using var host = await TestHost.CreateAsync();
        var account = await CreateAccountAsync(host, await host.CreateTariffAsync());
        var admin = await host.AdminAsync();

        await host.Run<AccountService, string>(s => s.DepositAsync(account.Id, new Xaf(2000), PaymentMethod.MtnMoMo, admin));

        Assert.Equal(new Xaf(2000), (await GetAsync(host)).Balance);
        var t = await host.Query(db => db.Transactions.SingleAsync());
        Assert.Equal((TransactionType.AccountDeposit, PaymentMethod.MtnMoMo), (t.Type, t.PaymentMethod));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            host.Run<AccountService, string>(s => s.DepositAsync(account.Id, Xaf.Zero, PaymentMethod.Cash, admin)));
    }

    [Fact]
    public async Task Login_buys_a_package_from_the_balance()
    {
        await using var host = await TestHost.CreateAsync();
        var account = await CreateAccountAsync(host, await host.CreateTariffAsync(quota: 60, price: 200));
        Assert.Equal(LoginStatus.InsufficientBalance, (await LoginAsync(host)).Status);

        await DepositAsync(host, account, 300);
        var login = await LoginAsync(host);

        Assert.True(login.Succeeded);
        Assert.Equal(LoginMethod.Account, login.Session!.Method);
        var stored = await GetAsync(host);
        Assert.Equal(new Xaf(100), stored.Balance);
        Assert.Equal(3600, stored.Allowance.RemainingSeconds);
        Assert.Equal(1, await host.Query(db => db.Transactions.CountAsync(t => t.Type == TransactionType.AccountCharge)));
    }

    [Fact]
    public async Task Overdraft_lets_the_balance_go_negative()
    {
        await using var host = await TestHost.CreateAsync();
        await CreateAccountAsync(host, await host.CreateTariffAsync(price: 200), overdraft: 200);

        Assert.True((await LoginAsync(host)).Succeeded);
        Assert.Equal(new Xaf(-200), (await GetAsync(host)).Balance);
    }

    [Fact]
    public async Task Used_package_is_renewed_automatically_then_session_ends_when_money_runs_out()
    {
        await using var host = await TestHost.CreateAsync();
        var account = await CreateAccountAsync(host, await host.CreateTariffAsync(quota: 1, price: 100));
        await DepositAsync(host, account, 200);
        var session = (await LoginAsync(host)).Session!;

        host.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(await host.TickAsync((session.Id, 100)));
        Assert.Equal(Xaf.Zero, (await GetAsync(host)).Balance);

        host.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(SessionEndReason.QuotaUsed, Assert.Single(await host.TickAsync((session.Id, 100))).EndReason);
    }

    [Fact]
    public async Task Wrong_password_counts_as_a_failed_attempt()
    {
        await using var host = await TestHost.CreateAsync();
        await CreateAccountAsync(host, await host.CreateTariffAsync());

        Assert.Equal(LoginStatus.InvalidCredentials, (await LoginAsync(host, "nope")).Status);
        Assert.Equal(1, (await host.Query(db => db.Devices.SingleAsync())).FailedLoginCount);
    }

    [Fact]
    public async Task Disabled_and_expired_accounts_are_refused()
    {
        await using var host = await TestHost.CreateAsync();
        var account = await CreateAccountAsync(host, await host.CreateTariffAsync(price: 0));

        await host.Run<AccountService>(s => s.SetEnabledAsync(account.Id, false, null));
        Assert.Equal(LoginStatus.AccountDisabled, (await LoginAsync(host)).Status);

        await host.Run<AccountService>(s => s.SetEnabledAsync(account.Id, true, null));
        await host.Run<StonesHotspot.Core.Data.HotspotDbContext>(async db =>
        {
            (await db.UserAccounts.SingleAsync()).ExpiresAt = host.Now;
            await db.SaveChangesAsync();
        });
        Assert.Equal(LoginStatus.AccountExpired, (await LoginAsync(host)).Status);
    }

    [Fact]
    public async Task Usernames_are_unique_ignoring_case()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync();
        await CreateAccountAsync(host, tariff);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<AccountService, UserAccount>(s =>
            s.CreateAsync(new UserAccount { Username = "JEAN", TariffId = tariff.Id }, "secret1", null)));
        Assert.Equal(ErrorCodes.DuplicateName, ex.Code);
    }
}

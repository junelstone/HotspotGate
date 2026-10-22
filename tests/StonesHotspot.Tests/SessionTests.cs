using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class SessionTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Usage_only_time_counts_per_second_and_pauses_when_logged_out()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(quota: 30));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;

        host.Advance(TimeSpan.FromSeconds(90));
        await host.TickAsync((session.Id, 1000));
        Assert.Equal(30 * 60 - 90, (await host.GetVoucherAsync(voucher.Id)).Allowance.RemainingSeconds);
        Assert.Equal(90, (await host.GetSessionAsync(session.Id)).SecondsUsed);

        await host.Run<SessionService, bool>(s => s.EndAsync(session.Id, SessionEndReason.Logout));
        host.Advance(TimeSpan.FromHours(2));
        var again = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;
        host.Advance(TimeSpan.FromSeconds(10));
        await host.TickAsync((again.Id, 1000));

        Assert.Equal(30 * 60 - 100, (await host.GetVoucherAsync(voucher.Id)).Allowance.RemainingSeconds);
    }

    [Fact]
    public async Task Session_ends_when_time_quota_is_used_and_device_is_cut_off()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(quota: 2));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;

        host.Advance(OneMinute);
        Assert.Empty(await host.TickAsync((session.Id, 500)));
        host.Advance(OneMinute);
        var ended = await host.TickAsync((session.Id, 500));

        Assert.Equal(session.Id, Assert.Single(ended).Id);
        var stored = await host.GetSessionAsync(session.Id);
        Assert.Equal(SessionEndReason.QuotaUsed, stored.EndReason);
        Assert.Equal(host.Now, stored.EndedAt);
        Assert.Equal(VoucherStatus.Expired, (await host.GetVoucherAsync(voucher.Id)).Status);
        Assert.Equal(LoginStatus.QuotaUsed, (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Status);
    }

    [Fact]
    public async Task Wall_clock_time_keeps_running_while_logged_out()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(quota: 60, mode: CountdownMode.WallClock));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;
        await host.Run<SessionService, bool>(s => s.EndAsync(session.Id, SessionEndReason.Logout));

        host.Advance(TimeSpan.FromMinutes(59));
        var again = await host.LoginAsync(TestHost.PhoneMac, voucher.Code);
        Assert.True(again.Succeeded);

        host.Advance(OneMinute);
        var ended = Assert.Single(await host.TickAsync((again.Session!.Id, 100)));
        Assert.Equal(SessionEndReason.Expired, ended.EndReason);
        Assert.Equal(LoginStatus.VoucherExpired, (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Status);
    }

    [Fact]
    public async Task Data_quota_is_counted_per_byte_up_and_down()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(TariffType.DataBased, quota: 10));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;
        const long quota = 10 * AllowanceRules.BytesPerMb;

        host.Advance(TimeSpan.FromSeconds(1));
        await host.TickAsync((session.Id, 4 * AllowanceRules.BytesPerMb));
        Assert.Equal(quota - 4 * AllowanceRules.BytesPerMb, (await host.GetVoucherAsync(voucher.Id)).Allowance.RemainingBytes);
        var stored = await host.GetSessionAsync(session.Id);
        Assert.Equal(4 * AllowanceRules.BytesPerMb, stored.BytesUp + stored.BytesDown);

        host.Advance(TimeSpan.FromSeconds(1));
        var ended = Assert.Single(await host.TickAsync((session.Id, 6 * AllowanceRules.BytesPerMb)));
        Assert.Equal(SessionEndReason.QuotaUsed, ended.EndReason);
        Assert.Equal(0, (await host.GetVoucherAsync(voucher.Id)).Allowance.RemainingBytes);
    }

    [Fact]
    public async Task Validity_window_expiry_ends_the_session()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(TariffType.Unlimited, validityHours: 2, idleMinutes: 600));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;

        host.Advance(TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));
        Assert.Empty(await host.TickAsync((session.Id, 10)));
        host.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(SessionEndReason.Expired, Assert.Single(await host.TickAsync((session.Id, 10))).EndReason);
    }

    [Fact]
    public async Task Idle_timeout_ends_a_session_without_traffic()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync(quota: 600, idleMinutes: 5);
        var busy = (await host.LoginAsync(TestHost.PhoneMac, (await host.CreateVoucherAsync(tariff)).Code)).Session!;
        var idle = (await host.LoginAsync(TestHost.LaptopMac, (await host.CreateVoucherAsync(tariff)).Code)).Session!;

        for (var i = 0; i < 4; i++)
        {
            host.Advance(OneMinute);
            Assert.Empty(await host.TickAsync((busy.Id, 100)));
        }
        host.Advance(OneMinute);
        var ended = Assert.Single(await host.TickAsync((busy.Id, 100)));

        Assert.Equal(idle.Id, ended.Id);
        Assert.Equal(SessionEndReason.Idle, ended.EndReason);
        Assert.True((await host.GetSessionAsync(busy.Id)).IsActive);
    }

    [Fact]
    public async Task Default_idle_timeout_comes_from_settings()
    {
        await using var host = await TestHost.CreateAsync(); // default setting: 10 minutes
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(quota: 600));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;

        host.Advance(TimeSpan.FromMinutes(9));
        Assert.Empty(await host.TickAsync());
        host.Advance(OneMinute);
        Assert.Equal(session.Id, Assert.Single(await host.TickAsync()).Id);
    }

    [Fact]
    public async Task Max_devices_is_enforced_and_same_device_reuses_its_session()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(maxDevices: 2));

        var phone = await host.LoginAsync(TestHost.PhoneMac, voucher.Code);
        var laptop = await host.LoginAsync(TestHost.LaptopMac, voucher.Code);
        var tablet = await host.LoginAsync(TestHost.TabletMac, voucher.Code);
        var phoneAgain = await host.LoginAsync(TestHost.PhoneMac, voucher.Code);

        Assert.True(phone.Succeeded);
        Assert.True(laptop.Succeeded);
        Assert.Equal(LoginStatus.TooManyDevices, tablet.Status);
        Assert.Equal(phone.Session!.Id, phoneAgain.Session!.Id);

        await host.Run<SessionService, bool>(s => s.EndAsync(laptop.Session!.Id, SessionEndReason.Logout));
        Assert.True((await host.LoginAsync(TestHost.TabletMac, voucher.Code)).Succeeded);
    }

    [Fact]
    public async Task Shared_voucher_time_is_taken_once_not_per_device()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(quota: 60, maxDevices: 3));
        var a = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;
        var b = (await host.LoginAsync(TestHost.LaptopMac, voucher.Code)).Session!;

        host.Advance(OneMinute);
        await host.TickAsync((a.Id, 10), (b.Id, 10));

        Assert.Equal(59 * 60, (await host.GetVoucherAsync(voucher.Id)).Allowance.RemainingSeconds);
    }

    [Fact]
    public async Task Wrong_codes_lock_the_device_for_five_minutes()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync());

        for (var i = 0; i < LoginAttemptLimiter.MaxAttempts - 1; i++)
            Assert.Equal(LoginStatus.InvalidCode, (await host.LoginAsync(TestHost.PhoneMac, "WRONG" + i)).Status);

        var fifth = await host.LoginAsync(TestHost.PhoneMac, "WRONGXX");
        Assert.Equal(LoginStatus.LockedOut, fifth.Status);
        Assert.Equal(host.Now.AddMinutes(5), fifth.LockedUntil);

        // Even the right code is refused during the lockout; other devices are not affected.
        Assert.Equal(LoginStatus.LockedOut, (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Status);
        Assert.Equal(LoginStatus.InvalidCode, (await host.LoginAsync(TestHost.LaptopMac, "WRONG")).Status);

        host.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Succeeded);
    }

    [Fact]
    public async Task Failed_attempts_outside_the_window_do_not_add_up()
    {
        await using var host = await TestHost.CreateAsync();
        for (var i = 0; i < 4; i++)
            await host.LoginAsync(TestHost.PhoneMac, "WRONG");

        host.Advance(LoginAttemptLimiter.Window + OneMinute);
        Assert.Equal(LoginStatus.InvalidCode, (await host.LoginAsync(TestHost.PhoneMac, "WRONG")).Status);
    }

    [Fact]
    public async Task Blocked_device_cannot_log_in_and_is_kicked()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync();
        await host.LoginAsync(TestHost.PhoneMac, (await host.CreateVoucherAsync(tariff)).Code);

        await host.Run<DeviceService>(s => s.SetBlockedAsync(TestHost.PhoneMac, true, null));
        host.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SessionEndReason.KickedByStaff, Assert.Single(await host.TickAsync()).EndReason);

        Assert.Equal(LoginStatus.DeviceBlocked, (await host.LoginAsync(TestHost.PhoneMac, (await host.CreateVoucherAsync(tariff)).Code)).Status);
    }

    [Fact]
    public async Task Staff_kick_is_audited()
    {
        await using var host = await TestHost.CreateAsync();
        var session = (await host.LoginAsync(TestHost.PhoneMac, (await host.CreateVoucherAsync(await host.CreateTariffAsync())).Code)).Session!;
        var admin = await host.AdminAsync();

        Assert.True(await host.Run<SessionService, bool>(s => s.EndAsync(session.Id, SessionEndReason.KickedByStaff, admin)));

        Assert.Equal(SessionEndReason.KickedByStaff, (await host.GetSessionAsync(session.Id)).EndReason);
        Assert.Equal("admin", (await host.Query(db => db.AuditLogs.SingleAsync(a => a.Action == "Session.Kicked"))).Actor);
    }

    [Fact]
    public async Task New_code_on_same_device_logs_out_the_previous_session()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync();
        var first = (await host.LoginAsync(TestHost.PhoneMac, (await host.CreateVoucherAsync(tariff)).Code)).Session!;
        var second = (await host.LoginAsync(TestHost.PhoneMac, (await host.CreateVoucherAsync(tariff)).Code)).Session!;

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(SessionEndReason.Logout, (await host.GetSessionAsync(first.Id)).EndReason);
        Assert.Single(await host.Run<SessionService, List<Session>>(s => s.GetActiveSessionsAsync()));
    }

    [Fact]
    public async Task Restart_restores_sessions_without_charging_downtime()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(quota: 60, idleMinutes: 10));
        var session = (await host.LoginAsync(TestHost.PhoneMac, voucher.Code)).Session!;
        host.Advance(OneMinute);
        await host.TickAsync((session.Id, 10));

        host.Advance(TimeSpan.FromHours(1)); // service (or PC) was off
        var restored = await host.Run<SessionService, IReadOnlyList<Session>>(s => s.ResumeAfterRestartAsync());
        host.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(await host.TickAsync((session.Id, 10)));

        Assert.Equal(session.Id, Assert.Single(restored).Id);
        Assert.Equal(59 * 60 - 5, (await host.GetVoucherAsync(voucher.Id)).Allowance.RemainingSeconds);
    }

    [Fact]
    public async Task Device_identity_is_the_mac_and_ip_changes_are_followed()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync());
        await host.Run<SessionService, LoginResult>(s => s.LoginWithVoucherAsync("aa-bb-cc-00-00-01", "192.168.137.10", voucher.Code));
        var again = await host.Run<SessionService, LoginResult>(s => s.LoginWithVoucherAsync(TestHost.PhoneMac, "192.168.137.55", voucher.Code));

        Assert.Equal("192.168.137.55", again.Session!.Ip);
        var device = await host.Query(db => db.Devices.SingleAsync());
        Assert.Equal(TestHost.PhoneMac, device.Mac);
        Assert.Equal("192.168.137.55", device.LastIp);
    }
}

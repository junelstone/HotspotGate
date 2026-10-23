using System.Globalization;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Tests.Infrastructure;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Tests;

public class PortalUnitTests
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    [Theory]
    [InlineData(35, "35 s", "35 s")]
    [InlineData(249, "4 min 09 s", "4 min 09 s")]
    [InlineData(3900, "1 h 05 min", "1 h 05 min")]
    [InlineData(2 * 86400 + 3 * 3600 + 59, "2 j 3 h", "2 d 3 h")]
    [InlineData(86400, "1 j", "1 d")]
    [InlineData(-5, "0 s", "0 s")]
    public void Durations(long seconds, string french, string english)
    {
        Assert.Equal(french, Units.Duration(seconds, Fr));
        Assert.Equal(english, Units.Duration(seconds, En));
    }

    [Theory]
    [InlineData(500, "500 o", "500 B")]
    [InlineData(870_400, "850 Ko", "850 KB")]
    [InlineData(13_107_200, "12,5 Mo", "12.5 MB")]
    [InlineData(1_288_490_189, "1,2 Go", "1.2 GB")]
    public void Sizes(long bytes, string french, string english)
    {
        Assert.Equal(french, Units.Bytes(bytes, Fr));
        Assert.Equal(english, Units.Bytes(bytes, En));
    }

    [Theory]
    [InlineData(750_000, "750 kbit/s", "750 kbit/s")]
    [InlineData(1_500_000, "1,5 Mbit/s", "1.5 Mbit/s")]
    [InlineData(25_000_000, "25 Mbit/s", "25 Mbit/s")]
    public void Speeds(double bps, string french, string english)
    {
        Assert.Equal(french, Units.Speed(bps, Fr));
        Assert.Equal(english, Units.Speed(bps, En));
    }

    [Theory]
    [InlineData(null, "http://www.example.com/news", "http://www.example.com/news")]
    [InlineData("https://shop.example.cg/welcome", "http://www.example.com/", "https://shop.example.cg/welcome")]
    [InlineData(null, "http://connectivitycheck.gstatic.com/generate_204", null)]
    [InlineData(null, "http://captive.apple.com/hotspot-detect.html", null)]
    [InlineData(null, "http://www.msftconnecttest.com/connecttest.txt", null)]
    [InlineData(null, "javascript:alert(1)", null)]
    [InlineData("javascript:alert(1)", "http://www.example.com/", "http://www.example.com/")]
    [InlineData(null, "/relative", null)]
    [InlineData(null, null, null)]
    public void Destination_after_login(string? welcome, string? original, string? expected) =>
        Assert.Equal(expected, PortalTarget.Choose(welcome, original));

    [Fact]
    public void Speed_monitor_smooths_and_forgets_ended_sessions()
    {
        var monitor = new LiveTrafficMonitor();
        monitor.Record(new Dictionary<long, (long, long)> { [1] = (12_500, 125_000) }, [1, 2], TimeSpan.FromSeconds(1));
        Assert.Equal((1_000_000, 100_000), monitor.For(1));           // bits per second
        Assert.Equal((0, 0), monitor.For(2));

        monitor.Record(new Dictionary<long, (long, long)>(), [1], TimeSpan.FromSeconds(1));
        Assert.Equal(500_000, monitor.For(1).DownBps);                 // halves toward 0
        monitor.Record(new Dictionary<long, (long, long)>(), [], TimeSpan.FromSeconds(1));
        Assert.Equal((0, 0), monitor.For(1));
    }

    [Fact]
    public async Task Refresh_signal_wakes_the_engine_once()
    {
        var signal = new AccessRefreshSignal();
        signal.Request();
        signal.Request(); // collapses into one
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None));
    }

    // ---- live session status (Core) ----

    [Fact]
    public async Task Status_counts_down_between_engine_ticks()
    {
        await using var host = await TestHost.CreateAsync();
        var voucher = await host.CreateVoucherAsync(await host.CreateTariffAsync(quota: 30, validityHours: 24));
        await host.LoginAsync(TestHost.PhoneMac, voucher.Code);

        host.Advance(TimeSpan.FromSeconds(10)); // no tick yet: the status still counts these seconds
        var status = await host.Run<SessionService, SessionStatus?>(s => s.GetStatusForDeviceAsync(TestHost.PhoneMac));

        Assert.Equal(30 * 60 - 10, status!.SecondsLeft);
        Assert.Equal(voucher.Code, status.VoucherCode);
        Assert.Equal(host.Now.AddSeconds(-10).AddHours(24), status.EndsAt);
        Assert.Null(status.BytesLeft);
    }

    [Fact]
    public async Task Status_of_a_day_pass_counts_down_to_its_end_and_data_shows_bytes()
    {
        await using var host = await TestHost.CreateAsync();
        var pass = await host.CreateVoucherAsync(await host.CreateTariffAsync(TariffType.Unlimited, validityHours: 24, idleMinutes: 600));
        await host.LoginAsync(TestHost.PhoneMac, pass.Code);
        var data = await host.CreateVoucherAsync(await host.CreateTariffAsync(TariffType.DataBased, quota: 100));
        await host.LoginAsync(TestHost.LaptopMac, data.Code);
        host.Advance(TimeSpan.FromHours(1));

        var day = await host.Run<SessionService, SessionStatus?>(s => s.GetStatusForDeviceAsync(TestHost.PhoneMac));
        var mb = await host.Run<SessionService, SessionStatus?>(s => s.GetStatusForDeviceAsync(TestHost.LaptopMac));

        Assert.Equal(23 * 3600, day!.SecondsLeft);
        Assert.Null(mb!.SecondsLeft);
        Assert.Equal(100 * AllowanceRules.BytesPerMb, mb.BytesLeft);
        Assert.Null(await host.Run<SessionService, SessionStatus?>(s => s.GetStatusForDeviceAsync(TestHost.TabletMac)));
    }

    [Fact]
    public async Task Recently_ended_session_is_found_only_for_a_while()
    {
        await using var host = await TestHost.CreateAsync();
        var session = (await host.LoginAsync(TestHost.PhoneMac, (await host.CreateVoucherAsync(await host.CreateTariffAsync())).Code)).Session!;
        Assert.Null(await host.Run<SessionService, Session?>(s => s.GetRecentlyEndedAsync(TestHost.PhoneMac, TimeSpan.FromMinutes(15))));

        await host.Run<SessionService, bool>(s => s.EndAsync(session.Id, SessionEndReason.Idle));
        Assert.Equal(SessionEndReason.Idle,
            (await host.Run<SessionService, Session?>(s => s.GetRecentlyEndedAsync(TestHost.PhoneMac, TimeSpan.FromMinutes(15))))?.EndReason);

        host.Advance(TimeSpan.FromMinutes(16));
        Assert.Null(await host.Run<SessionService, Session?>(s => s.GetRecentlyEndedAsync(TestHost.PhoneMac, TimeSpan.FromMinutes(15))));
    }
}

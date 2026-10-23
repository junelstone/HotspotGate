using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Gate.Packets;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;
using Xunit.Abstractions;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

/// <summary>
/// 50 devices on the hotspot at once, through the real service (portal pages, SQLite file database, engine, packet
/// processor). The limits are generous — they catch a design problem (locks, N+1 queries), not a slow build agent;
/// the measured numbers are printed in the test output.
/// </summary>
public sealed class LoadTests(LoadTests.LoadFactory factory, ITestOutputHelper output) : IClassFixture<LoadTests.LoadFactory>
{
    public sealed class LoadFactory : ServiceSmokeTests.Factory;

    private const int Devices = 50;

    private sealed record Phone(string Ip, string Mac, HttpClient Http);

    private HotspotEngineService Engine => factory.Services.GetServices<IHostedService>().OfType<HotspotEngineService>().Single();

    [Fact]
    public async Task Fifty_devices_log_in_together_and_their_traffic_is_counted_every_second()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().ApplyAsync(FakeAdapters.EthernetId, FakeAdapters.LanId, null)).Succeeded);
        await Engine.TickAsync(CancellationToken.None);

        List<string> codes;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tariff = (await scope.ServiceProvider.GetRequiredService<TariffService>().ListAsync()).Single(t => t.Name == "1 heure");
            codes = (await scope.ServiceProvider.GetRequiredService<VoucherService>().GenerateBatchAsync(tariff.Id, Devices, "load", false, null))
                .Select(v => v.Code).ToList();
        }
        var phones = Enumerable.Range(0, Devices).Select(n =>
        {
            var phone = new Phone($"192.168.137.{100 + n}", $"DA:A1:50:00:00:{n:X2}", null!);
            factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(phone.Ip), phone.Mac, NeighborState.Reachable));
            return phone with { Http = factory.CreateDefaultClient(new CookieContainerHandler(), new RemoteIpHandler(phone.Ip)) };
        }).ToList();

        // 1. Everybody opens the portal and types a code at the same moment.
        var watch = Stopwatch.StartNew();
        var logins = await Task.WhenAll(phones.Select(async (phone, i) =>
        {
            var page = await phone.Http.GetStringAsync("/portal");
            var token = StaffSignIn.TokenRegex.Match(page).Groups[1].Value;
            var started = Stopwatch.StartNew();
            var response = await phone.Http.PostAsync("/portal?handler=Voucher", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Code"] = codes[i], ["__RequestVerificationToken"] = token,
            }));
            return (response.StatusCode, started.Elapsed);
        }));
        var loginTime = watch.Elapsed;
        output.WriteLine($"{Devices} simultaneous portal logins: {loginTime.TotalMilliseconds:F0} ms in total, slowest {logins.Max(l => l.Elapsed).TotalMilliseconds:F0} ms");

        Assert.All(logins, l => Assert.Equal(HttpStatusCode.Redirect, l.StatusCode));
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(Devices, await scope.ServiceProvider.GetRequiredService<HotspotDbContext>().Sessions.CountAsync(s => s.EndedAt == null && s.Voucher!.BatchName == "load"));
        Assert.True(loginTime < TimeSpan.FromSeconds(30), $"logins took {loginTime}");

        // 2. Ten seconds of browsing: each second every phone sends 20 small packets and receives 60 full ones. They
        //    arrive here within a few milliseconds instead of spread over the second, so the speed limit delays or
        //    drops most of each burst: that is the shaper working, the point is the processing cost per packet.
        await Engine.TickAsync(CancellationToken.None);
        var processor = factory.Services.GetRequiredService<IPacketGate>().Processor;
        var up = phones.Select(p => Tcp(p.Ip, 50000, "93.184.216.34", 443, TcpFlags.Ack, payload: new byte[60])).ToList();
        var down = phones.Select(p => Tcp("93.184.216.34", 443, p.Ip, 50000, TcpFlags.Ack, payload: new byte[1400])).ToList();
        var verdicts = new Dictionary<GateVerdict, int>();
        var packets = 0;
        var processing = TimeSpan.Zero;
        var ticks = new List<TimeSpan>();
        for (var second = 0; second < 10; second++)
        {
            var round = Stopwatch.StartNew();
            for (var i = 0; i < Devices; i++)
            {
                for (var k = 0; k < 20; k++)
                    Count(processor.Process((byte[])up[i].Clone(), PacketSource.Forward, false));
                for (var k = 0; k < 60; k++)
                    Count(processor.Process((byte[])down[i].Clone(), PacketSource.Forward, false));
            }
            processing += round.Elapsed;
            var tick = Stopwatch.StartNew();
            await Engine.TickAsync(CancellationToken.None);
            ticks.Add(tick.Elapsed);
        }

        var perSecond = packets / processing.TotalSeconds;
        output.WriteLine($"Packet processing: {packets} packets in {processing.TotalMilliseconds:F0} ms = {perSecond:F0} packets/s ({string.Join(", ", verdicts.Select(v => $"{v.Key} {v.Value}"))})");
        output.WriteLine($"Engine tick with {Devices} sessions: average {ticks.Average(t => t.TotalMilliseconds):F0} ms, slowest {ticks.Max().TotalMilliseconds:F0} ms");

        // 50 phones at full speed (1 Mbit/s each in this plan) are about 4 500 packets/s: keep a wide margin.
        Assert.True(perSecond > 20_000, $"only {perSecond:F0} packets/s");
        Assert.True(ticks.Max() < TimeSpan.FromSeconds(1), $"a tick took {ticks.Max()} (the engine ticks every second)");
        Assert.Equal(0, verdicts.GetValueOrDefault(GateVerdict.Reset));   // nobody was thrown back to the portal

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var used = await scope.ServiceProvider.GetRequiredService<HotspotDbContext>().Sessions
                .Where(s => s.Voucher!.BatchName == "load").Select(s => s.BytesDown).ToListAsync();
            Assert.All(used, bytes => Assert.True(bytes > 0));
        }

        // 3. Everybody refreshes the status page together.
        watch.Restart();
        var statuses = await Task.WhenAll(phones.Select(p => p.Http.GetAsync("/portal/status")));
        output.WriteLine($"{Devices} simultaneous status pages: {watch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s.StatusCode));

        void Count(GateVerdict verdict)
        {
            packets++;
            verdicts[verdict] = verdicts.GetValueOrDefault(verdict) + 1;
        }
    }
}

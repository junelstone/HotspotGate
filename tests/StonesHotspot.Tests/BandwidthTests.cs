using System.Collections.Frozen;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Bandwidth;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Gate.Packets;
using StonesHotspot.Network.Neighbors;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

public sealed class BandwidthTests
{
    private const string Phone = "192.168.137.23";
    private const string Web = "93.184.216.34";
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-03-02T08:00:00Z"));

    private PacketProcessor Gate(int? downKbps = null, int? upKbps = null)
    {
        var gate = new PacketProcessor(_clock);
        gate.Update(new GateSnapshot(Ipv4Subnet.Parse("192.168.137.1/24"), Ip("192.168.137.1"), 80,
            new Dictionary<uint, AccessEntry> { [Ip(Phone)] = new("DA:A1:19:00:00:01", 7, downKbps, upKbps) }.ToFrozenDictionary(),
            FilterRules.Empty));
        return gate;
    }

    /// <summary>
    /// Offers <paramref name="offeredKbps"/> of 1500-byte packets for <paramref name="seconds"/> simulated seconds
    /// through a <paramref name="limitKbps"/> shaper; returns the delivered rate (kbit/s), drops and the longest delay.
    /// </summary>
    private (double Kbps, int Drops, TimeSpan MaxDelay) Offer(int limitKbps, int offeredKbps, int seconds)
    {
        var gate = new PacketProcessor(_clock);
        var ip = Ip(Phone);
        const int size = 1500;
        var interval = TimeSpan.FromTicks(size * 8L * TimeSpan.TicksPerSecond / (offeredKbps * 1000L));
        var end = _clock.GetUtcNow() + TimeSpan.FromSeconds(seconds);
        long delivered = 0;
        var drops = 0;
        var maxDelay = TimeSpan.Zero;
        while (_clock.GetUtcNow() < end)
        {
            var verdict = gate.Shape(ip, upload: false, size, limitKbps, out var delay);
            if (verdict == GateVerdict.Drop)
                drops++;
            else if (_clock.GetUtcNow() + delay < end)
                delivered += size; // left the queue before the measurement ended (as a speed test sees it)
            if (delay > maxDelay)
                maxDelay = delay;
            _clock.Advance(interval);
        }
        return (delivered * 8.0 / 1000 / seconds, drops, maxDelay);
    }

    [Theory]
    [InlineData(1024, 10_000)]   // 1 Mbit/s plan, phone pulls 10 Mbit/s
    [InlineData(512, 2_000)]
    [InlineData(4096, 50_000)]
    public void Shaper_delivers_the_plan_speed_within_two_percent(int limit, int offered)
    {
        var (kbps, drops, maxDelay) = Offer(limit, offered, seconds: 10);

        Assert.InRange(kbps, limit * 0.98, limit * 1.02 + 20); // + the 20 ms burst allowance
        Assert.True(drops > 0);
        Assert.True(maxDelay <= PacketProcessor.MaxQueueDelay);
    }

    [Fact]
    public void Traffic_under_the_limit_is_never_delayed()
    {
        var (kbps, drops, maxDelay) = Offer(limitKbps: 2048, offeredKbps: 1000, seconds: 5);
        Assert.InRange(kbps, 990, 1010);
        Assert.Equal(0, drops);
        Assert.Equal(TimeSpan.Zero, maxDelay);
    }

    [Fact]
    public void Download_is_shaped_by_the_download_limit_and_upload_separately()
    {
        var gate = Gate(downKbps: 512, upKbps: null);
        var verdicts = new List<GateVerdict>();
        for (var i = 0; i < 20; i++)
            verdicts.Add(gate.Process(Tcp(Web, 443, Phone, 50000, TcpFlags.Ack, payload: new byte[1400]), PacketSource.Forward, false, out _));

        Assert.Contains(GateVerdict.Delay, verdicts);
        Assert.True(gate.Counters.ShapedPackets > 0);
        // Upload has no limit: never delayed.
        for (var i = 0; i < 20; i++)
            Assert.Equal(GateVerdict.Pass, gate.Process(Tcp(Phone, 50000, Web, 443, TcpFlags.Ack, payload: new byte[1400]), PacketSource.Forward, false, out _));
    }

    [Fact]
    public void Dropped_packets_are_not_counted_as_used_data()
    {
        var gate = Gate(downKbps: 64);
        long sent = 0;
        for (var i = 0; i < 200; i++)
        {
            var packet = Tcp(Web, 443, Phone, 50000, TcpFlags.Ack, payload: new byte[1400]);
            if (gate.Process(packet, PacketSource.Forward, false, out _) != GateVerdict.Drop)
                sent += packet.Length;
        }
        Assert.True(gate.Counters.ShapedDrops > 0);
        Assert.Equal(sent, gate.TakeByteCounts()[Ip(Phone)].Down);
    }

    [Fact]
    public void Recently_active_devices_are_counted_for_fair_share()
    {
        var gate = Gate();
        Assert.Empty(gate.RecentlyActive(TimeSpan.FromSeconds(10)));
        gate.Process(Tcp(Phone, 50000, Web, 443, TcpFlags.Syn), PacketSource.Forward, false);
        Assert.Single(gate.RecentlyActive(TimeSpan.FromSeconds(10)));
        _clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Empty(gate.RecentlyActive(TimeSpan.FromSeconds(10)));
    }

    // ---- Fair share ----

    private static FrozenDictionary<uint, AccessEntry> Table(params (uint Ip, int? Down, int? Up)[] entries) =>
        entries.ToDictionary(e => e.Ip, e => new AccessEntry("00:00:00:00:00:01", 1, e.Down, e.Up)).ToFrozenDictionary();

    [Fact]
    public void Fair_share_splits_the_line_between_active_devices_within_min_and_max()
    {
        var table = Table((1, null, null), (2, 1024, null), (3, null, null), (4, null, null));
        var settings = new FairShare(true, TotalDownKbps: 8000, TotalUpKbps: 2000, MinKbps: 256, MaxKbps: 3000);

        var shared = BandwidthPolicy.Apply(table, settings, activeIps: [1, 2, 3, 4]);

        Assert.Equal((2000, 500), (shared[1].DownKbps!.Value, shared[1].UpKbps!.Value));
        Assert.Equal(1024, shared[2].DownKbps);                       // the plan is lower than the share

        var alone = BandwidthPolicy.Apply(table, settings, activeIps: [1]);
        Assert.Equal(3000, alone[1].DownKbps);                        // capped at the maximum
        var crowd = BandwidthPolicy.Apply(table, settings with { TotalDownKbps = 800 }, activeIps: [1, 2, 3, 4]);
        Assert.Equal(256, crowd[1].DownKbps);                         // never below the minimum
    }

    [Fact]
    public void Fair_share_off_keeps_plan_limits_and_missing_totals_mean_no_share()
    {
        var table = Table((1, 1024, 256));
        Assert.Same(table, BandwidthPolicy.Apply(table, FairShare.Off, [1]));

        var downOnly = BandwidthPolicy.Apply(Table((1, null, 256)), new FairShare(true, 4000, 0, 256, 0), [1]);
        Assert.Equal((4000, 256), (downOnly[1].DownKbps!.Value, downOnly[1].UpKbps!.Value));
    }

    [Fact]
    public void Plan_limits_reach_the_access_table_and_survive_whitelisting()
    {
        var table = AccessTableBuilder.Build(new AccessInputs(
            [new AccessSession(7, "DA:A1:19:00:00:01", Phone, 1024, 512)], ["DA:A1:19:00:00:01"], [],
            [new NeighborEntry(7, IPAddress.Parse(Phone), "DA:A1:19:00:00:01", NeighborState.Reachable)], true));

        Assert.Equal(new AccessEntry("DA:A1:19:00:00:01", 7, 1024, 512), table[Ip(Phone)]);
    }

    // ---- Delay line ----

    [Fact]
    public async Task Delay_line_releases_in_time_order_and_refuses_when_full()
    {
        var released = new List<string>();
        var done = new TaskCompletionSource();
        using (var line = new PacketDelayLine<string>(item =>
               {
                   lock (released)
                   {
                       released.Add(item);
                       if (released.Count == 3)
                           done.TrySetResult();
                   }
               }, TimeProvider.System, "test", capacity: 3))
        {
            Assert.True(line.Enqueue("third", TimeSpan.FromMilliseconds(90)));
            Assert.True(line.Enqueue("first", TimeSpan.FromMilliseconds(10)));
            Assert.True(line.Enqueue("second", TimeSpan.FromMilliseconds(50)));
            Assert.False(line.Enqueue("too many", TimeSpan.FromSeconds(5)));

            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(["first", "second", "third"], released);
    }

    // ---- WAN speed test ----

    private sealed class FakeLine(int bytesPerResponse, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Uploads { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(20, ct);
            if (request.Method == HttpMethod.Post)
                Uploads++;
            return new HttpResponseMessage(status) { Content = new ByteArrayContent(new byte[request.Method == HttpMethod.Get ? bytesPerResponse : 0]) };
        }
    }

    private static WanSpeedTest SpeedTest(HttpMessageHandler handler) => new(new HttpClient(handler),
        new SpeedTestOptions { DownloadDuration = TimeSpan.FromMilliseconds(400), UploadDuration = TimeSpan.FromMilliseconds(300), Streams = 2,
            TestDownloadUrl = "http://line.test/down", TestUploadUrl = "http://line.test/up" },
        TimeProvider.System, NullLogger<WanSpeedTest>.Instance);

    [Fact]
    public async Task Speed_test_measures_both_directions()
    {
        var line = new FakeLine(256 * 1024);
        var test = SpeedTest(line);

        var result = await test.RunAsync(CancellationToken.None);

        Assert.Null(result.Error);
        Assert.True(result.DownKbps > 0);
        Assert.True(result.UpKbps > 0);
        Assert.True(line.Uploads > 0);
        Assert.Same(result, test.Last);
    }

    [Fact]
    public async Task Speed_test_reports_a_failing_server()
    {
        var result = await SpeedTest(new FakeLine(0, HttpStatusCode.ServiceUnavailable)).RunAsync(CancellationToken.None);
        Assert.NotNull(result.Error);
        Assert.Null(result.DownKbps);
    }
}

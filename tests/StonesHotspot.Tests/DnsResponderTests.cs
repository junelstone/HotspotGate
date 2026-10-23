using System.Collections.Frozen;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Dns;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Gate.Packets;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

public sealed class DnsResponderTests
{
    private sealed class FakeUpstream : IDnsUpstream
    {
        public Func<byte[], byte[]?> Answer { get; set; } = _ => null;
        public int Calls { get; private set; }
        public bool? LastFamily { get; private set; }

        public Task<byte[]?> QueryAsync(byte[] query, bool family, CancellationToken ct)
        {
            Calls++;
            LastFamily = family;
            return Task.FromResult(Answer(query));
        }
    }

    private readonly PacketProcessor _gate = new(new FakeTimeProvider(DateTimeOffset.UnixEpoch));
    private readonly FakeUpstream _upstream = new();
    private readonly DnsResponder _dns;

    public DnsResponderTests()
    {
        _gate.Update(new GateSnapshot(Ipv4Subnet.Parse("192.168.137.1/24"), Ip("192.168.137.1"), 80,
            FrozenDictionary<uint, AccessEntry>.Empty,
            FilterRules.Empty with { BlockedDomains = new DomainSet(["facebook.com"]), FreeDomains = new DomainSet(["mtn.cg"]) }));
        _dns = new DnsResponder(_gate, _upstream, NullLogger<DnsResponder>.Instance);
    }

    private static byte[] QueryPacket(string name, string server = "192.168.137.1", ushort type = DnsType.A) =>
        Udp("192.168.137.23", 40000, server, 53, DnsQuery(name, type, 0x4242));

    private static byte[] Payload(byte[] packet)
    {
        Assert.True(Ipv4Packet.TryParse(packet, out var p));
        return packet.AsSpan(p.PayloadOffset, p.PayloadLength).ToArray();
    }

    [Fact]
    public async Task Blocked_domain_points_to_the_gateway_without_asking_upstream()
    {
        var reply = await _dns.AnswerAsync(QueryPacket("www.facebook.com"), CancellationToken.None);

        Assert.Equal(new DnsARecord(Ip("192.168.137.1"), DnsResponder.BlockedTtlSeconds), Assert.Single(DnsMessage.ReadARecords(Payload(reply!))));
        Assert.Equal(0, _upstream.Calls);
        Assert.Equal(1, _gate.Counters.DnsBlocked);
    }

    [Fact]
    public async Task Other_names_are_resolved_upstream_and_returned_from_the_server_the_device_asked()
    {
        _upstream.Answer = q => DnsResponse(q, "edge.example.net", ("93.184.216.34", 300));

        var reply = await _dns.AnswerAsync(QueryPacket("example.com", server: "8.8.8.8"), CancellationToken.None);

        Assert.True(Ipv4Packet.TryParse(reply, out var p));
        Assert.Equal((Ip("8.8.8.8"), (ushort)53, Ip("192.168.137.23"), (ushort)40000), (p.Source, p.SourcePort, p.Destination, p.DestinationPort));
        var payload = Payload(reply!);
        Assert.Equal(0x4242, payload[0] << 8 | payload[1]);
        Assert.Equal(Ip("93.184.216.34"), Assert.Single(DnsMessage.ReadARecords(payload)).Address);
        Assert.Equal(0, _gate.LearnedFreeCount); // not a free-access domain
    }

    [Fact]
    public async Task Free_access_domain_answers_open_their_addresses()
    {
        _upstream.Answer = q => DnsResponse(q, "lb.mtn.cg", ("41.202.1.1", 60), ("41.202.1.2", 60));

        await _dns.AnswerAsync(QueryPacket("momo.mtn.cg"), CancellationToken.None);

        Assert.Equal(2, _gate.LearnedFreeCount);
        Assert.Equal(GateVerdict.Pass, _gate.Process(Tcp("192.168.137.23", 50000, "41.202.1.2", 443, TcpFlags.Syn), PacketSource.Forward, false));
    }

    [Fact]
    public async Task Family_dns_option_is_passed_to_the_resolver()
    {
        _upstream.Answer = q => DnsResponse(q, "x.example.net", ("93.184.216.34", 300));
        await _dns.AnswerAsync(QueryPacket("example.com"), CancellationToken.None);
        Assert.False(_upstream.LastFamily);

        _gate.Update(_gate.Snapshot! with { Rules = _gate.Snapshot.Rules with { FamilyDns = true } });
        await _dns.AnswerAsync(QueryPacket("example.com"), CancellationToken.None);
        Assert.True(_upstream.LastFamily);
    }

    [Fact]
    public async Task Upstream_failure_gives_servfail_and_garbage_gives_nothing()
    {
        var reply = await _dns.AnswerAsync(QueryPacket("example.com"), CancellationToken.None);
        Assert.Equal(DnsRcode.ServFail, Payload(reply!)[3] & 0x0F);

        Assert.Null(await _dns.AnswerAsync(Udp("192.168.137.23", 40000, "192.168.137.1", 53, [1, 2, 3]), CancellationToken.None));
    }
}

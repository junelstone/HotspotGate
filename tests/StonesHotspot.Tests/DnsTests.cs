using System.Buffers.Binary;
using StonesHotspot.Network.Dns;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

public class DnsTests
{
    [Fact]
    public void Parses_a_query_with_edns()
    {
        Assert.True(DnsMessage.TryParseQuery(DnsQuery("ConnectivityCheck.GStatic.com", DnsType.A, 0x1234), out var q));
        Assert.Equal((0x1234, "connectivitycheck.gstatic.com", DnsType.A, (ushort)1), (q.Id, q.Name, q.Type, q.Class));
    }

    [Fact]
    public void Rejects_responses_and_garbage()
    {
        var response = DnsQuery("example.com");
        response[2] |= 0x80;
        Assert.False(DnsMessage.TryParseQuery(response, out _));
        Assert.False(DnsMessage.TryParseQuery([1, 2, 3], out _));
        var loop = DnsQuery("example.com");
        loop[12] = 0xC0; // compression pointer in a query: refused
        Assert.False(DnsMessage.TryParseQuery(loop, out _));
    }

    [Fact]
    public void Builds_an_a_answer_pointing_to_the_gateway()
    {
        var query = DnsQuery("facebook.com", DnsType.A, 0x0102);
        DnsMessage.TryParseQuery(query, out var q);

        var answer = DnsMessage.BuildAddressAnswer(query, q, Ip("192.168.137.1"), 10);

        Assert.Equal(0x0102, BinaryPrimitives.ReadUInt16BigEndian(answer));
        Assert.Equal(0x8180, BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(2)) & 0xFF8F); // QR, RD, RA, NOERROR
        var records = DnsMessage.ReadARecords(answer);
        Assert.Equal(new DnsARecord(Ip("192.168.137.1"), 10), Assert.Single(records));
    }

    [Fact]
    public void Non_a_questions_get_an_empty_answer()
    {
        var query = DnsQuery("facebook.com", DnsType.Aaaa);
        DnsMessage.TryParseQuery(query, out var q);

        var answer = DnsMessage.BuildAddressAnswer(query, q, Ip("192.168.137.1"), 10);

        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(6)));
        Assert.Empty(DnsMessage.ReadARecords(answer));
    }

    [Fact]
    public void Server_failure_answer_has_the_rcode()
    {
        var query = DnsQuery("example.com");
        DnsMessage.TryParseQuery(query, out var q);
        Assert.Equal(DnsRcode.ServFail, DnsMessage.BuildError(query, q, DnsRcode.ServFail)[3] & 0x0F);
    }

    [Fact]
    public void Reads_a_records_after_a_compressed_cname()
    {
        var response = DnsResponse(DnsQuery("www.wikipedia.org"), "dyna.wikimedia.org", ("185.15.58.224", 600), ("185.15.58.225", 30));

        Assert.Equal([new DnsARecord(Ip("185.15.58.224"), 600), new DnsARecord(Ip("185.15.58.225"), 30)], DnsMessage.ReadARecords(response));
    }

    [Theory]
    [InlineData("facebook.com", true)]
    [InlineData("m.facebook.com", true)]
    [InlineData("FACEBOOK.COM.", true)]
    [InlineData("notfacebook.com", false)]
    [InlineData("facebook.com.evil.net", false)]
    public void Domain_set_matches_subdomains_only(string name, bool expected) =>
        Assert.Equal(expected, new DomainSet(["*.facebook.com"]).Matches(name));
}

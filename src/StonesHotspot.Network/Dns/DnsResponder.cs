using Microsoft.Extensions.Logging;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Gate.Packets;

namespace StonesHotspot.Network.Dns;

/// <summary>Sends a packet built by the gate to a customer device.</summary>
public interface IPacketInjector
{
    void InjectToClient(byte[] packet);
}

/// <summary>
/// Answers the customers' DNS queries taken out of the packet flow: blocked domains point to the gateway (the
/// "site blocked" page), everything else is resolved upstream; answers for free-access domains open their IPs in
/// the gate.
/// </summary>
public sealed class DnsResponder(PacketProcessor processor, IDnsUpstream upstream, ILogger<DnsResponder> logger)
{
    public const int MaxConcurrentQueries = 128;
    public const uint BlockedTtlSeconds = 10;

    private readonly SemaphoreSlim _slots = new(MaxConcurrentQueries);

    /// <summary>Starts answering <paramref name="queryPacket"/> (a full IPv4/UDP packet) in the background.</summary>
    public void Enqueue(byte[] queryPacket, IPacketInjector injector, CancellationToken ct)
    {
        if (!_slots.Wait(0))
            return; // overloaded: drop, the device will retry
        _ = Task.Run(async () =>
        {
            try
            {
                if (await AnswerAsync(queryPacket, ct) is { } reply)
                    injector.InjectToClient(reply);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "DNS query failed");
            }
            finally
            {
                _slots.Release();
            }
        }, CancellationToken.None);
    }

    /// <summary>Builds the reply packet for a query packet, or null to stay silent (not a valid query).</summary>
    public async Task<byte[]?> AnswerAsync(byte[] queryPacket, CancellationToken ct)
    {
        if (!Ipv4Packet.TryParse(queryPacket, out var p) || !p.IsUdp)
            return null;
        var query = queryPacket.AsSpan(p.PayloadOffset, p.PayloadLength).ToArray();
        if (!DnsMessage.TryParseQuery(query, out var question))
            return null;

        var snapshot = processor.Snapshot;
        var rules = snapshot?.Rules ?? FilterRules.Empty;

        byte[] answer;
        if (snapshot is not null && rules.BlockedDomains.Matches(question.Name))
        {
            processor.Counters.DnsBlock();
            answer = DnsMessage.BuildAddressAnswer(query, question, snapshot.Gateway, BlockedTtlSeconds);
        }
        else
        {
            var response = await upstream.QueryAsync(query, ct);
            if (response is null)
            {
                answer = DnsMessage.BuildError(query, question, DnsRcode.ServFail);
            }
            else
            {
                DnsMessage.SetId(response, question.Id);
                answer = response;
                if (rules.FreeDomains.Matches(question.Name))
                {
                    foreach (var record in DnsMessage.ReadARecords(response))
                        processor.LearnFreeAddress(record.Address, TimeSpan.FromSeconds(record.Ttl));
                }
            }
        }

        return Ipv4Packet.BuildUdpReply(queryPacket, answer);
    }
}

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace StonesHotspot.Network.Dns;

public interface IDnsUpstream
{
    /// <summary>Sends a raw DNS query to a real resolver; null when no server answered in time.</summary>
    Task<byte[]?> QueryAsync(byte[] query, CancellationToken ct);
}

/// <summary>
/// Plain UDP resolver: the internet adapter's DNS servers first, then public fallbacks. Queries leave from the PC
/// itself, so the gate (which only captures the customer subnet) does not see them.
/// </summary>
public sealed class UdpDnsUpstream(Func<IReadOnlyList<IPAddress>> serverProvider, ILogger<UdpDnsUpstream> logger) : IDnsUpstream
{
    public static readonly IPAddress[] Fallbacks = [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("8.8.8.8")];
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public async Task<byte[]?> QueryAsync(byte[] query, CancellationToken ct)
    {
        var servers = serverProvider()
            .Where(s => !IPAddress.IsLoopback(s) && !s.Equals(IPAddress.Any))
            .Concat(Fallbacks)
            .Distinct()
            .Take(3);

        foreach (var server in servers)
        {
            try
            {
                using var udp = new UdpClient(AddressFamily.InterNetwork);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(Timeout);
                await udp.SendAsync(query, new IPEndPoint(server, 53), timeout.Token);
                while (true)
                {
                    var result = await udp.ReceiveAsync(timeout.Token);
                    // Ignore stray datagrams: same transaction ID and from the server we asked.
                    if (result.RemoteEndPoint.Address.Equals(server) && result.Buffer.Length >= 12
                        && result.Buffer[0] == query[0] && result.Buffer[1] == query[1])
                        return result.Buffer;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogDebug("DNS server {Server} did not answer in time", server);
            }
            catch (SocketException ex)
            {
                logger.LogDebug(ex, "DNS server {Server} unreachable", server);
            }
        }
        return null;
    }
}

using System.Net.Sockets;

namespace StonesHotspot.Core.Printing;

/// <summary>Sends a finished print job (raw ESC/POS bytes) to the printer.</summary>
public interface IPrinterTransport
{
    Task SendAsync(byte[] job, CancellationToken ct);
}

/// <summary>Makes the transport for a connection; Windows printers and COM ports are implemented in the Network project.</summary>
public interface IPrinterTransportFactory
{
    IPrinterTransport Create(PrinterConnection connection);

    /// <summary>Names of the printers installed in Windows (empty elsewhere).</summary>
    IReadOnlyList<string> InstalledPrinters();
}

/// <summary>Could not reach the printer or send the job; the message is shown to staff.</summary>
public sealed class PrinterException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Network printers: raw TCP, usually port 9100 ("JetDirect").</summary>
public sealed class TcpPrinterTransport(string host, int port, TimeSpan? timeout = null) : IPrinterTransport
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);

    public async Task SendAsync(byte[] job, CancellationToken ct)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(_timeout);
        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(host, port, timeoutSource.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(job, timeoutSource.Token);
            await stream.FlushAsync(timeoutSource.Token);
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PrinterException($"No answer from {host}:{port} within {_timeout.TotalSeconds:0} s.");
        }
        catch (SocketException ex)
        {
            throw new PrinterException($"Cannot reach {host}:{port}: {ex.SocketErrorCode}.", ex);
        }
    }
}

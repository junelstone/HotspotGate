using System.IO.Ports;
using StonesHotspot.Core.Printing;

namespace StonesHotspot.Network.Printing;

/// <summary>Serial printers, and Bluetooth printers paired in Windows (they appear as an outgoing COM port).</summary>
public sealed class SerialPrinterTransport(string portName, int baudRate) : IPrinterTransport
{
    public Task SendAsync(byte[] job, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                WriteTimeout = 10_000,
            };
            port.Open();
            // Small chunks: cheap printers without flow control lose bytes when their buffer overflows.
            for (var offset = 0; offset < job.Length; offset += 256)
            {
                ct.ThrowIfCancellationRequested();
                port.Write(job, offset, Math.Min(256, job.Length - offset));
            }
            port.BaseStream.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or ArgumentException or InvalidOperationException)
        {
            throw new PrinterException($"Cannot print on {portName}: {ex.Message}", ex);
        }
    }, ct);
}

using System.IO.Ports;
using StonesHotspot.Core.Sms;

namespace StonesHotspot.Network.Sms;

public sealed class SmsTransportFactory(HttpClient http) : ISmsTransportFactory
{
    public ISmsTransport Create(SmsSettings settings) => settings.Kind switch
    {
        SmsTransportKind.Modem => new ModemSmsTransport(settings.ModemPort, settings.ModemBaud),
        SmsTransportKind.Http => new HttpSmsTransport(http, settings.HttpUrl ?? "", settings.HttpBody, settings.HttpSuccessText),
        _ => throw new SmsException("No SMS modem or provider is configured."),
    };

    public IReadOnlyList<string> SerialPorts()
    {
        try
        {
            return SerialPort.GetPortNames().Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return [];
        }
    }
}

/// <summary>A GSM/USB modem on a COM port. One SMS at a time per port (the modem cannot do two conversations).</summary>
public sealed class ModemSmsTransport(string portName, int baudRate) : ISmsTransport
{
    private static readonly Dictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(60);

    public async Task SendAsync(string phone, string text, CancellationToken ct)
    {
        SemaphoreSlim gate;
        lock (Locks)
        {
            if (!Locks.TryGetValue(portName, out gate!))
                Locks[portName] = gate = new SemaphoreSlim(1, 1);
        }

        await gate.WaitAsync(ct);
        try
        {
            using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None, DtrEnable = true, RtsEnable = true, ReadTimeout = 5000, WriteTimeout = 5000,
            };
            try
            {
                port.Open();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                throw new SmsException($"Cannot open {portName}: {ex.Message} (wrong port, or another program such as the modem's own software is using it).", ex);
            }
            await AtModem.SendSmsAsync(port.BaseStream, phone, text, SendTimeout, ct);
        }
        finally
        {
            gate.Release();
        }
    }
}

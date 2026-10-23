using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Core.Printing;

public enum PrinterConnectionKind { None, WindowsPrinter, Network, Serial }

/// <summary>
/// Where the receipt printer is, as typed in the settings: <c>192.168.1.50:9100</c> (network), <c>COM3:9600</c>
/// (serial or Bluetooth), anything else is the name of a printer installed in Windows. Empty = no receipt printer.
/// </summary>
public sealed partial record PrinterConnection(PrinterConnectionKind Kind, string Target, int Port)
{
    public const int DefaultNetworkPort = 9100;
    public const int DefaultBaudRate = 9600;

    public static PrinterConnection None { get; } = new(PrinterConnectionKind.None, "", 0);

    public static PrinterConnection Parse(string? text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0)
            return None;

        var serial = SerialPattern().Match(text);
        if (serial.Success)
        {
            var baud = serial.Groups[2].Success ? int.Parse(serial.Groups[2].Value, CultureInfo.InvariantCulture) : DefaultBaudRate;
            return new(PrinterConnectionKind.Serial, serial.Groups[1].Value.ToUpperInvariant(), baud);
        }

        var network = NetworkPattern().Match(text);
        if (network.Success && IPAddress.TryParse(network.Groups[1].Value, out _))
        {
            var port = network.Groups[2].Success ? int.Parse(network.Groups[2].Value, CultureInfo.InvariantCulture) : DefaultNetworkPort;
            if (port is > 0 and <= 65535)
                return new(PrinterConnectionKind.Network, network.Groups[1].Value, port);
        }

        return new(PrinterConnectionKind.WindowsPrinter, text, 0);
    }

    public override string ToString() => Kind switch
    {
        PrinterConnectionKind.Network => string.Create(CultureInfo.InvariantCulture, $"{Target}:{Port}"),
        PrinterConnectionKind.Serial => string.Create(CultureInfo.InvariantCulture, $"{Target}:{Port}"),
        _ => Target,
    };

    [GeneratedRegex(@"^(COM\d{1,3})(?::(\d{3,6}))?$", RegexOptions.IgnoreCase)]
    private static partial Regex SerialPattern();

    [GeneratedRegex(@"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})(?::(\d{1,5}))?$")]
    private static partial Regex NetworkPattern();
}

/// <summary>Receipt printer settings (stored with the other settings).</summary>
public sealed record PrinterSettings(
    PrinterConnection Connection,
    int PaperWidthMm,
    PrinterCharset Charset,
    bool PrintQr,
    bool AutoPrintSale,
    bool AutoPrintShift,
    string Header,
    string Footer)
{
    /// <summary>Characters per line of the normal font.</summary>
    public int Columns => PaperWidthMm == 58 ? 32 : 48;

    public bool HasPrinter => Connection.Kind != PrinterConnectionKind.None;

    public static async Task<PrinterSettings> LoadAsync(SettingsService settings, CancellationToken ct = default) => new(
        PrinterConnection.Parse(await settings.GetAsync(SettingKeys.PrinterConnection, ct)),
        await settings.GetIntAsync(SettingKeys.PrinterPaperWidth, 80, ct) == 58 ? 58 : 80,
        await settings.GetAsync(SettingKeys.PrinterCharset, ct) == "ascii" ? PrinterCharset.Ascii : PrinterCharset.Pc850,
        await settings.GetBoolAsync(SettingKeys.PrinterQr, ct),
        await settings.GetBoolAsync(SettingKeys.PrinterAutoSale, ct),
        await settings.GetBoolAsync(SettingKeys.PrinterAutoShift, ct),
        await settings.GetAsync(SettingKeys.ReceiptHeader, ct) ?? "",
        await settings.GetAsync(SettingKeys.ReceiptFooter, ct) ?? "");
}

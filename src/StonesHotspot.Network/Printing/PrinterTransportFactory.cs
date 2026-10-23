using StonesHotspot.Core.Printing;

namespace StonesHotspot.Network.Printing;

public sealed class PrinterTransportFactory : IPrinterTransportFactory
{
    public IPrinterTransport Create(PrinterConnection connection) => connection.Kind switch
    {
        PrinterConnectionKind.Network => new TcpPrinterTransport(connection.Target, connection.Port),
        PrinterConnectionKind.Serial => new SerialPrinterTransport(connection.Target, connection.Port),
        PrinterConnectionKind.WindowsPrinter when OperatingSystem.IsWindows() => new WindowsSpoolerTransport(connection.Target),
        PrinterConnectionKind.WindowsPrinter => throw new PrinterException("Windows printers can only be used on Windows."),
        _ => throw new PrinterException("No receipt printer is configured."),
    };

    public IReadOnlyList<string> InstalledPrinters() =>
        OperatingSystem.IsWindows() ? WindowsSpoolerTransport.InstalledPrinters() : [];
}

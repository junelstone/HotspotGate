using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StonesHotspot.Core.Printing;

namespace StonesHotspot.Network.Printing;

/// <summary>
/// Printers installed in Windows (USB receipt printers, with their own driver or "Generic / Text Only"): the job is
/// sent as RAW data through the spooler, so the ESC/POS commands reach the printer unchanged.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsSpoolerTransport(string printerName) : IPrinterTransport
{
    public Task SendAsync(byte[] job, CancellationToken ct) => Task.Run(() => Send(job), ct);

    private void Send(byte[] job)
    {
        if (!OpenPrinterW(printerName, out var printer, IntPtr.Zero))
            throw Error($"Printer '{printerName}' not found");
        try
        {
            var info = new DocInfo1 { DocName = "Stone's Hotspot", OutputFile = null, DataType = "RAW" };
            if (StartDocPrinterW(printer, 1, ref info) == 0)
                throw Error("Cannot start the print job");
            try
            {
                if (!StartPagePrinter(printer))
                    throw Error("Cannot start the page");
                if (!WritePrinter(printer, job, job.Length, out var written) || written != job.Length)
                    throw Error("Cannot send the data");
                EndPagePrinter(printer);
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    /// <summary>Local and connected printers.</summary>
    public static IReadOnlyList<string> InstalledPrinters()
    {
        const int local = 0x2, connections = 0x4;
        EnumPrintersW(local | connections, null, 4, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0)
            return [];
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumPrintersW(local | connections, null, 4, buffer, needed, out _, out var count))
                return [];
            var names = new List<string>(count);
            var size = Marshal.SizeOf<PrinterInfo4>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buffer + i * size);
                if (Marshal.PtrToStringUni(info.PrinterName) is { Length: > 0 } name)
                    names.Add(name);
            }
            return names.Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static PrinterException Error(string what) =>
        new($"{what}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo1
    {
        public string DocName;
        public string? OutputFile;
        public string DataType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr PrinterName;
        public IntPtr ServerName;
        public int Attributes;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinterW(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocPrinterW(IntPtr printer, int level, ref DocInfo1 docInfo);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartPagePrinter(IntPtr printer);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WritePrinter(IntPtr printer, byte[] bytes, int count, out int written);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndPagePrinter(IntPtr printer);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndDocPrinter(IntPtr printer);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrintersW(int flags, string? name, int level, IntPtr buffer, int size, out int needed, out int returned);
}

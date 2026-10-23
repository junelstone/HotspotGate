using System.Globalization;
using Microsoft.Extensions.Logging;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Printing;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Documents;

namespace StonesHotspot.Web.Admin;

/// <summary>Prints receipts, shift reports and the test page on the configured thermal printer.</summary>
public sealed class ReceiptPrinter(
    SettingsService settings, SalesService sales, CashService cash, PortalLocation portal,
    IPrinterTransportFactory transports, ILogger<ReceiptPrinter> logger)
{
    public Task<PrinterSettings> SettingsAsync(CancellationToken ct = default) => PrinterSettings.LoadAsync(settings, ct);

    public async Task<ReceiptBranding> BrandingAsync(CancellationToken ct = default) => new(
        await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "",
        await settings.GetAsync(SettingKeys.WifiName, ct),
        await settings.GetAsync(SettingKeys.ReceiptHeader, ct) ?? "",
        await settings.GetAsync(SettingKeys.ReceiptFooter, ct) ?? "",
        PortalLinks.Address(portal),
        code => PortalLinks.ForCode(portal, code));

    /// <returns>Null when printed, otherwise the reason it failed (shown to staff).</returns>
    public async Task<string?> PrintReceiptAsync(string number, CancellationToken ct = default)
    {
        var receipt = await sales.GetReceiptAsync(number, ct);
        if (receipt is null)
            return Strings.Receipt_NotFound;
        return await SendAsync($"receipt {number}",
            (brand, printer) => ReceiptLayouts.ReceiptEscPos(receipt, brand, printer, CultureInfo.CurrentUICulture), ct);
    }

    public async Task<string?> PrintShiftAsync(int shiftId, CancellationToken ct = default)
    {
        var report = await cash.GetShiftReportAsync(shiftId, ct);
        var shift = await cash.FindShiftAsync(shiftId, ct);
        report = report with { Shift = shift ?? report.Shift };
        var transactions = await cash.ListShiftTransactionsAsync(shiftId, ct);
        return await SendAsync($"shift {shiftId}",
            (brand, printer) => ReceiptLayouts.ShiftEscPos(report, transactions, brand, printer, CultureInfo.CurrentUICulture), ct);
    }

    public Task<string?> PrintTestPageAsync(CancellationToken ct = default) =>
        SendAsync("test page", (brand, printer) => ReceiptLayouts.TestPageEscPos(brand, printer, CultureInfo.CurrentUICulture), ct);

    private async Task<string?> SendAsync(string what, Func<ReceiptBranding, PrinterSettings, byte[]> layout, CancellationToken ct)
    {
        var printer = await SettingsAsync(ct);
        if (!printer.HasPrinter)
            return Strings.Printer_NotConfigured;
        try
        {
            var job = layout(await BrandingAsync(ct), printer);
            await transports.Create(printer.Connection).SendAsync(job, ct);
            logger.LogInformation("Printed {What} on {Printer} ({Bytes} bytes)", what, printer.Connection, job.Length);
            return null;
        }
        catch (PrinterException ex)
        {
            logger.LogWarning("Could not print {What} on {Printer}: {Reason}", what, printer.Connection, ex.Message);
            return string.Format(CultureInfo.CurrentUICulture, Strings.Printer_Failed, ex.Message);
        }
    }
}

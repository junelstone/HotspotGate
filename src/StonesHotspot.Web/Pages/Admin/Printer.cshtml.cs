using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Printing;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Receipt printer and receipt texts (admins).</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class PrinterModel(SettingsService settings, ReceiptPrinter printer, IPrinterTransportFactory transports, CurrentStaff current) : PageModel
{
    public const int MaxTextLength = 300;

    [BindProperty] public string? Connection { get; set; }
    [BindProperty] public int PaperWidth { get; set; } = 80;
    [BindProperty] public string Charset { get; set; } = "pc850";
    [BindProperty] public bool Qr { get; set; } = true;
    [BindProperty] public bool AutoSale { get; set; }
    [BindProperty] public bool AutoShift { get; set; }
    [BindProperty] public string? Header { get; set; }
    [BindProperty] public string? Footer { get; set; }

    public IReadOnlyList<string> InstalledPrinters { get; private set; } = [];
    public PrinterConnection Parsed { get; private set; } = PrinterConnection.None;

    public async Task OnGetAsync()
    {
        var s = await printer.SettingsAsync(HttpContext.RequestAborted);
        Connection = s.Connection.ToString();
        PaperWidth = s.PaperWidthMm;
        Charset = s.Charset == PrinterCharset.Ascii ? "ascii" : "pc850";
        Qr = s.PrintQr;
        AutoSale = s.AutoPrintSale;
        AutoShift = s.AutoPrintShift;
        Header = s.Header;
        Footer = s.Footer;
        Load();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ct = HttpContext.RequestAborted;
        var actor = await current.GetAsync(ct);
        var connection = PrinterConnection.Parse(Connection);

        async Task SetAsync(string key, string value)
        {
            if (await settings.GetAsync(key, ct) != value)
                await settings.SetAsync(key, value, actor, ct);
        }

        await SetAsync(SettingKeys.PrinterConnection, connection.ToString());
        await SetAsync(SettingKeys.PrinterPaperWidth, PaperWidth == 58 ? "58" : "80");
        await SetAsync(SettingKeys.PrinterCharset, Charset == "ascii" ? "ascii" : "pc850");
        await SetAsync(SettingKeys.PrinterQr, Qr ? "true" : "false");
        await SetAsync(SettingKeys.PrinterAutoSale, AutoSale ? "true" : "false");
        await SetAsync(SettingKeys.PrinterAutoShift, AutoShift ? "true" : "false");
        await SetAsync(SettingKeys.ReceiptHeader, Clip(Header));
        await SetAsync(SettingKeys.ReceiptFooter, Clip(Footer));
        TempData.Success(Strings.Common_Saved);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        var error = await printer.PrintTestPageAsync(HttpContext.RequestAborted);
        if (error is null)
            TempData.Success(Strings.Printer_Sent);
        else
            TempData.Error(error);
        return RedirectToPage();
    }

    public static string Describe(PrinterConnection c) => c.Kind switch
    {
        PrinterConnectionKind.Network => string.Format(CultureInfo.CurrentUICulture, Strings.Printer_KindNetwork, c.Target, c.Port),
        PrinterConnectionKind.Serial => string.Format(CultureInfo.CurrentUICulture, Strings.Printer_KindSerial, c.Target, c.Port),
        PrinterConnectionKind.WindowsPrinter => string.Format(CultureInfo.CurrentUICulture, Strings.Printer_KindWindows, c.Target),
        _ => Strings.Printer_KindNone,
    };

    private void Load()
    {
        Parsed = PrinterConnection.Parse(Connection);
        InstalledPrinters = transports.InstalledPrinters();
    }

    private static string Clip(string? text)
    {
        text = (text ?? "").Replace("\r", "").Trim();
        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }
}

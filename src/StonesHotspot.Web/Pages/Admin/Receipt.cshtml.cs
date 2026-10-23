using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Documents;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>One receipt (Pro): on screen, as PDF, or on the thermal printer.</summary>
public sealed class ReceiptModel(SalesService sales, ReceiptPrinter printer, ILicenseService licence) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? N { get; set; }

    public Receipt Receipt { get; private set; } = null!;
    public ReceiptBranding Brand { get; private set; } = null!;
    public bool HasPrinter { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!licence.IsAllowed(Feature.Receipts))
            return Redirect($"/admin/print?receipt={Uri.EscapeDataString(N ?? "")}");
        if (!await LoadAsync())
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnGetPdfAsync()
    {
        if (!licence.IsAllowed(Feature.Receipts) || !await LoadAsync())
            return NotFound();
        var pdf = ReceiptLayouts.ReceiptPdf(Receipt, Brand, CultureInfo.CurrentUICulture);
        return File(pdf, "application/pdf", $"recu-{Receipt.Number}.pdf");
    }

    public async Task<IActionResult> OnPostThermalAsync()
    {
        if (!licence.IsAllowed(Feature.Receipts) || string.IsNullOrEmpty(N))
            return NotFound();
        var error = await printer.PrintReceiptAsync(N, HttpContext.RequestAborted);
        if (error is null)
            TempData.Success(Strings.Printer_Sent);
        else
            TempData.Error(error);
        return Redirect($"/admin/receipt?n={Uri.EscapeDataString(N)}");
    }

    private async Task<bool> LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (string.IsNullOrEmpty(N) || await sales.GetReceiptAsync(N, ct) is not { } receipt)
            return false;
        Receipt = receipt;
        Brand = await printer.BrandingAsync(ct);
        HasPrinter = (await printer.SettingsAsync(ct)).HasPrinter;
        return true;
    }
}

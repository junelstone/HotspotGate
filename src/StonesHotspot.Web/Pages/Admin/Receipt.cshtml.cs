using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Documents;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>One receipt (Pro): on screen (prints on A4) or as an A4 PDF.</summary>
public sealed class ReceiptModel(SalesService sales, ReceiptBrandingProvider branding, ILicenseService licence) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? N { get; set; }

    public Receipt Receipt { get; private set; } = null!;
    public ReceiptBranding Brand { get; private set; } = null!;

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

    private async Task<bool> LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (string.IsNullOrEmpty(N) || await sales.GetReceiptAsync(N, ct) is not { } receipt)
            return false;
        Receipt = receipt;
        Brand = await branding.GetAsync(ct);
        return true;
    }
}

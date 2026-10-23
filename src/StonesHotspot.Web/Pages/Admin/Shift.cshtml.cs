using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Documents;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;
using StonesHotspot.Web.Documents;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>The cash drawer of the signed-in staff member (Pro). Admins can look at any shift.</summary>
public sealed class ShiftModel(CurrentStaff current, CashService cash, ReceiptBrandingProvider branding, ILicenseService licence, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public string? Amount { get; set; }
    [BindProperty] public string? Note { get; set; }

    public bool Enabled { get; private set; }
    public Staff Me { get; private set; } = null!;
    public ShiftReport? Report { get; private set; }
    public IReadOnlyList<Transaction> Transactions { get; private set; } = [];
    public IReadOnlyList<Shift> History { get; private set; } = [];
    public bool IsMine => Report?.Shift.StaffId == Me.Id;
    public string? Error { get; private set; }

    /// <summary>Receipt links go to the full receipt (Pro) or to the codes ticket.</summary>
    public string ReceiptLink(string number) => licence.IsAllowed(Feature.Receipts)
        ? $"/admin/receipt?n={Uri.EscapeDataString(number)}"
        : $"/admin/print?receipt={Uri.EscapeDataString(number)}";

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Report is null && Id is not null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostOpenAsync()
    {
        await LoadAsync();
        if (!Enabled)
            return Page();
        if (!Xaf.TryParse(Amount, out var opening))
        {
            Error = Strings.Common_InvalidAmount;
            return Page();
        }
        try
        {
            await cash.OpenShiftAsync(Me, opening, HttpContext.RequestAborted);
            TempData.Success(Strings.Shift_Opened);
            return RedirectToPage();
        }
        catch (BusinessRuleException)
        {
            Error = Strings.Shift_AlreadyOpen;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCloseAsync(int shiftId)
    {
        await LoadAsync();
        if (!Enabled)
            return Page();
        if (!Xaf.TryParse(Amount, out var counted))
        {
            Error = Strings.Common_InvalidAmount;
            return Page();
        }
        try
        {
            var report = await cash.CloseShiftAsync(shiftId, counted, Me, Note, HttpContext.RequestAborted);
            TempData.Success(string.Format(Strings.Shift_Closed, report.Difference));
            return RedirectToPage(new { id = shiftId });
        }
        catch (BusinessRuleException)
        {
            Error = Strings.Denied_Text;
            return Page();
        }
    }

    /// <summary>The shift report as an A4 PDF.</summary>
    public async Task<IActionResult> OnGetPdfAsync()
    {
        await LoadAsync();
        if (Report is null)
            return NotFound();
        var culture = CultureInfo.CurrentUICulture;
        var now = TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, clock.LocalTimeZone);
        var footer = string.Format(culture, Strings.Report_Footer, now.ToString("g", culture), Me.DisplayName ?? Me.Username);
        var document = ReceiptLayouts.ShiftReportDocument(Report, Transactions, await branding.GetAsync(HttpContext.RequestAborted), footer, culture);
        return File(ReportExport.Pdf(document), "application/pdf",
            string.Create(CultureInfo.InvariantCulture, $"caisse-{Report.Shift.Id}-{Report.Shift.OpenedAt.ToLocalTime():yyyy-MM-dd}.pdf"));
    }

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        Me = await current.GetAsync(ct);
        Enabled = licence.IsAllowed(Feature.Shifts);
        if (!Enabled)
            return;

        var admin = Me.Role == StaffRole.Admin;
        Shift? shift = Id is { } id ? await cash.FindShiftAsync(id, ct) : await cash.GetOpenShiftAsync(Me.Id, ct);
        if (shift is not null && (admin || shift.StaffId == Me.Id))
        {
            Report = await cash.GetShiftReportAsync(shift.Id, ct);
            Transactions = await cash.ListShiftTransactionsAsync(shift.Id, ct);
        }
        History = await cash.ListShiftsAsync(admin ? null : Me.Id, 20, ct);
    }
}

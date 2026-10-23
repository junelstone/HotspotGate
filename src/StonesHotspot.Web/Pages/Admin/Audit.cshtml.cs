using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Documents;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Who did what and when (admins).</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class AuditModel(AuditService audit, ReportService reports, StaffService staff) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int? StaffId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public IReadOnlyList<AuditLog> Entries { get; private set; } = [];
    public IReadOnlyList<Staff> StaffMembers { get; private set; } = [];

    public string Query => string.Join("&", Request.Query.Where(q => q.Key != "handler")
        .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}"));

    public async Task OnGetAsync()
    {
        await LoadAsync();
        StaffMembers = await staff.ListAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnGetCsvAsync()
    {
        await LoadAsync();
        var table = new ReportTable(Strings.Nav_Audit, new ReportColumn(Strings.Col_Time), new ReportColumn(Strings.Col_Staff),
            new ReportColumn(Strings.Audit_Action), new ReportColumn(Strings.Audit_Details));
        foreach (var e in Entries)
            table.Add(ReportCell.Date(e.Timestamp.ToLocalTime(), CultureInfo.CurrentUICulture), e.Actor, e.Action, e.Details ?? "");
        return File(ReportExport.Csv([table]), "text/csv; charset=utf-8",
            string.Create(CultureInfo.InvariantCulture, $"journal-{From:yyyy-MM-dd}-{To:yyyy-MM-dd}.csv"));
    }

    private async Task LoadAsync()
    {
        To ??= reports.Today;
        From ??= To.Value.AddDays(-6);
        if (From > To)
            (From, To) = (To, From);
        var (from, to) = reports.ToUtc(new DateRange(From.Value, To.Value));
        Entries = await audit.QueryAsync(from, to, StaffId, Q, HttpContext.RequestAborted);
    }
}

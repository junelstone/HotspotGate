using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

/// <summary>Business reports over dates, on screen and as CSV / PDF. Sales by plan and sessions are Basic, the others Pro.</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class ReportsModel(
    ReportService reports, StaffService staff, SettingsService settings, CurrentStaff current, ILicenseService licence, TimeProvider clock) : PageModel
{
    public static readonly string[] Kinds = ["plans", "sessions", "shifts", "transactions", "turnover"];

    [BindProperty(SupportsGet = true)] public string R { get; set; } = "plans";
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public TransactionType? Type { get; set; }
    [BindProperty(SupportsGet = true)] public PaymentMethod? Method { get; set; }
    [BindProperty(SupportsGet = true)] public int? StaffId { get; set; }
    [BindProperty(SupportsGet = true)] public bool Monthly { get; set; }

    public bool Allowed { get; private set; }
    public ReportDocument? Report { get; private set; }
    public IReadOnlyList<Staff> StaffMembers { get; private set; } = [];

    public static bool IsPro(string kind) => kind is "shifts" or "transactions" or "turnover";

    public static string Title(string kind) => kind switch
    {
        "sessions" => Strings.Report_Sessions,
        "shifts" => Strings.Report_Shifts,
        "transactions" => Strings.Report_Transactions,
        "turnover" => Strings.Report_Turnover,
        _ => Strings.Report_Plans,
    };

    /// <summary>The current query string without the handler, for the CSV / PDF links.</summary>
    public string Query => string.Join("&", Request.Query.Where(q => q.Key != "handler")
        .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}"));

    public async Task OnGetAsync()
    {
        await BuildAsync();
        StaffMembers = await staff.ListAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnGetCsvAsync()
    {
        await BuildAsync();
        return Report is null ? NotFound() : File(ReportExport.Csv(Report.Tables), "text/csv; charset=utf-8", FileName("csv"));
    }

    public async Task<IActionResult> OnGetPdfAsync()
    {
        await BuildAsync();
        return Report is null ? NotFound() : File(ReportExport.Pdf(Report), "application/pdf", FileName("pdf"));
    }

    private string FileName(string extension) =>
        string.Create(CultureInfo.InvariantCulture, $"rapport-{R}-{From:yyyy-MM-dd}-{To:yyyy-MM-dd}.{extension}");

    private async Task BuildAsync()
    {
        var ct = HttpContext.RequestAborted;
        var culture = CultureInfo.CurrentUICulture;
        if (!Kinds.Contains(R))
            R = "plans";
        To ??= reports.Today;
        From ??= To.Value.AddDays(1 - To.Value.Day);   // first day of the month
        if (From > To)
            (From, To) = (To, From);
        Allowed = licence.IsAllowed(IsPro(R) ? Feature.FullReports : Feature.BasicReports);
        if (!Allowed)
            return;

        var range = new DateRange(From.Value, To.Value);
        var tables = R switch
        {
            "sessions" => await SessionsAsync(range, culture, ct),
            "shifts" => await ShiftsAsync(range, culture, ct),
            "transactions" => await TransactionsAsync(range, culture, ct),
            "turnover" => await TurnoverAsync(range, culture, ct),
            _ => await PlansAsync(range, culture, ct),
        };

        var me = await current.GetAsync(ct);
        var now = TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, clock.LocalTimeZone);
        var subtitle = range.From == range.To
            ? range.From.ToString("D", culture)
            : string.Format(culture, Strings.Report_FromTo, range.From.ToString("d", culture), range.To.ToString("d", culture));
        Report = new ReportDocument(Title(R), subtitle, await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "",
            string.Format(culture, Strings.Report_Footer, now.ToString("g", culture), me.DisplayName ?? me.Username), tables, Strings.Report_Continued);
    }

    private async Task<List<ReportTable>> PlansAsync(DateRange range, CultureInfo culture, CancellationToken ct)
    {
        var rows = await reports.VoucherSalesByPlanAsync(range, ct);
        var table = new ReportTable(Strings.Report_Plans,
            new ReportColumn(Strings.Col_Tariff, Weight: 3), new ReportColumn(Strings.Report_Count, Numeric: true), new ReportColumn(Strings.Col_Amount, Numeric: true, Weight: 1.5f));
        foreach (var r in rows)
            table.Add(r.Plan, ReportCell.Number(r.Count, culture), ReportCell.Money(r.Amount));
        table.Totals = [Strings.Print_Total, ReportCell.Number(rows.Sum(r => r.Count), culture), ReportCell.Money(Xaf.Sum(rows.Select(r => r.Amount)))];
        return [table];
    }

    private async Task<List<ReportTable>> SessionsAsync(DateRange range, CultureInfo culture, CancellationToken ct)
    {
        var r = await reports.SessionsAsync(range, ct);
        var summary = new ReportTable(Strings.ShiftReport_Summary, new ReportColumn("", Weight: 2), new ReportColumn("", Numeric: true));
        summary.Add(Strings.Report_SessionCount, ReportCell.Number(r.Sessions, culture))
            .Add(Strings.Report_DeviceCount, ReportCell.Number(r.Devices, culture))
            .Add(Strings.Report_DataDown, ReportCell.Formatted(Units.Bytes(r.BytesDown, culture), r.BytesDown))
            .Add(Strings.Report_DataUp, ReportCell.Formatted(Units.Bytes(r.BytesUp, culture), r.BytesUp))
            .Add(Strings.Report_AverageDuration, ReportCell.Formatted(Units.Duration((long)r.AverageDuration.TotalSeconds, culture), (long)r.AverageDuration.TotalSeconds));

        var methods = new ReportTable(Strings.Report_ByMethod, new ReportColumn(Strings.Report_Method, Weight: 2), new ReportColumn(Strings.Report_Count, Numeric: true));
        foreach (var (method, count) in r.ByMethod.OrderByDescending(m => m.Value))
            methods.Add(AdminText.Method(method), ReportCell.Number(count, culture));

        var peak = r.PeakHours.Max();
        var hours = new ReportTable(Strings.Report_PeakHours,
            new ReportColumn(Strings.Report_Hour), new ReportColumn(Strings.Report_ActiveSessions, Numeric: true), new ReportColumn("", Weight: 2));
        for (var h = 0; h < 24; h++)
        {
            var bar = peak > 0 ? new string('|', (int)Math.Round(r.PeakHours[h] / peak * 30)) : "";
            hours.Add(string.Create(CultureInfo.InvariantCulture, $"{h:00} h – {(h + 1) % 24:00} h"), ReportCell.Decimal(r.PeakHours[h], 1, culture), new ReportCell(bar, ""));
        }
        return [summary, methods, hours];
    }

    private async Task<List<ReportTable>> ShiftsAsync(DateRange range, CultureInfo culture, CancellationToken ct)
    {
        var shifts = new ReportTable(Strings.Report_Shifts,
            new ReportColumn(Strings.Shift_OpenedAt, Weight: 1.4f), new ReportColumn(Strings.Col_Staff, Weight: 1.3f), new ReportColumn(Strings.Shift_OpeningCash, Numeric: true),
            new ReportColumn(Strings.Shift_CashIn, Numeric: true), new ReportColumn(Strings.Shift_Expected, Numeric: true), new ReportColumn(Strings.Shift_Counted, Numeric: true),
            new ReportColumn(Strings.Shift_Difference, Numeric: true), new ReportColumn(Strings.Report_AllMethods, Numeric: true));
        var rows = await reports.ShiftsAsync(range, ct);
        foreach (var s in rows)
        {
            shifts.Add(ReportCell.Date(s.Shift.OpenedAt.ToLocalTime(), culture), s.Staff, ReportCell.Money(s.OpeningCash), ReportCell.Money(s.CashIn),
                ReportCell.Money(s.ExpectedCash), s.CountedCash is null ? Strings.Shift_Open : ReportCell.Money(s.CountedCash), ReportCell.Money(s.Difference), ReportCell.Money(s.Total));
        }
        shifts.Totals = [Strings.Print_Total, "", "", ReportCell.Money(Xaf.Sum(rows.Select(r => r.CashIn))), "", "",
            ReportCell.Money(Xaf.Sum(rows.Where(r => r.Difference is not null).Select(r => r.Difference!.Value))), ReportCell.Money(Xaf.Sum(rows.Select(r => r.Total)))];

        var byStaff = new ReportTable(Strings.Report_ByStaff,
            [new ReportColumn(Strings.Col_Staff, Weight: 1.5f), new ReportColumn(Strings.Report_Receipts, Numeric: true),
             .. AdminText.CounterMethods.Select(m => new ReportColumn(AdminText.Payment(m), Numeric: true)), new ReportColumn(Strings.Print_Total, Numeric: true)]);
        foreach (var s in await reports.SalesByStaffAsync(range, ct))
        {
            byStaff.Add([s.Staff.Length == 0 ? Strings.Report_Online : s.Staff, ReportCell.Number(s.Receipts, culture),
                .. AdminText.CounterMethods.Select(m => ReportCell.Money(s.ByMethod.GetValueOrDefault(m))), ReportCell.Money(s.Total)]);
        }
        return [shifts, byStaff];
    }

    private async Task<List<ReportTable>> TransactionsAsync(DateRange range, CultureInfo culture, CancellationToken ct)
    {
        var rows = await reports.TransactionsAsync(range, new TransactionFilter(Type, Method, StaffId), ct);
        var table = new ReportTable(Strings.Report_Transactions,
            new ReportColumn(Strings.Col_Time, Weight: 1.3f), new ReportColumn(Strings.Col_Receipt, Weight: 1.1f), new ReportColumn(Strings.Col_Type, Weight: 1.3f),
            new ReportColumn(Strings.Col_Description, Weight: 1.8f), new ReportColumn(Strings.Sell_Payment), new ReportColumn(Strings.Col_Staff),
            new ReportColumn(Strings.Col_Amount, Numeric: true));
        foreach (var t in rows)
        {
            table.Add(ReportCell.Date(t.Timestamp.ToLocalTime(), culture), t.ReceiptNumber, AdminText.TransactionType(t.Type), ReceiptLayouts.TransactionText(t),
                AdminText.Payment(t.PaymentMethod), t.Staff?.DisplayName ?? t.Staff?.Username ?? "", ReportCell.Money(t.Amount));
        }
        table.Totals = [Strings.Print_Total, "", "", ReportCell.Number(rows.Count, culture), "", "", ReportCell.Money(Xaf.Sum(rows.Select(t => t.Amount)))];
        return [table];
    }

    private async Task<List<ReportTable>> TurnoverAsync(DateRange range, CultureInfo culture, CancellationToken ct)
    {
        TransactionType[] types = [TransactionType.VoucherSale, TransactionType.ProductSale, TransactionType.AccountDeposit, TransactionType.MobileMoneyPayment, TransactionType.Refund];
        var rows = await reports.TurnoverAsync(range, Monthly, ct);
        var table = new ReportTable(Strings.Report_Turnover,
            [new ReportColumn(Monthly ? Strings.Report_Month : Strings.Report_Day, Weight: 1.3f),
             .. types.Select(t => new ReportColumn(AdminText.TransactionType(t), Numeric: true)), new ReportColumn(Strings.Print_Total, Numeric: true, Weight: 1.2f)]);
        foreach (var r in rows)
        {
            var period = Monthly ? r.Period.ToString("MMMM yyyy", culture) : r.Period.ToString("ddd d MMM yyyy", culture);
            table.Add([new ReportCell(period, r.Period.ToString(Monthly ? "yyyy-MM" : "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                .. types.Select(t => ReportCell.Money(r.ByType.GetValueOrDefault(t))), ReportCell.Money(r.Total)]);
        }
        table.Totals = [Strings.Print_Total, .. types.Select(t => ReportCell.Money(Xaf.Sum(rows.Select(r => r.ByType.GetValueOrDefault(t))))),
            ReportCell.Money(Xaf.Sum(rows.Select(r => r.Total)))];
        return [table];
    }
}

using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

[Authorize(Policy = StaffPolicies.Admin)]
public sealed class BatchModel(VoucherService vouchers, CurrentStaff current) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Name { get; set; } = "";

    public IReadOnlyList<Voucher> Vouchers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        Vouchers = await vouchers.ListBatchAsync(Name, HttpContext.RequestAborted);
        return Vouchers.Count == 0 ? NotFound() : Page();
    }

    /// <summary>Spreadsheet of the batch (semicolon-separated, UTF-8 with BOM so Excel in French opens it correctly).</summary>
    public async Task<IActionResult> OnGetCsvAsync()
    {
        var list = await vouchers.ListBatchAsync(Name, HttpContext.RequestAborted);
        var csv = new StringBuilder("code;plan;price_xaf;status;created_utc;sold_utc;first_used_utc\n");
        foreach (var v in list)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{VoucherCodeGenerator.Format(v.Code)};{Escape(v.Tariff.Name)};{v.Tariff.Price.Amount};{v.Status};{Iso(v.CreatedAt)};{Iso(v.SoldAt)};{Iso(v.FirstUsedAt)}\n");
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"vouchers-{Safe(Name)}.csv");
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var count = await vouchers.DisableUnusedInBatchAsync(Name, await current.GetAsync(), HttpContext.RequestAborted);
        TempData.Success(string.Format(Strings.Vouchers_Disabled, count));
        return Redirect($"/admin/batch?name={Uri.EscapeDataString(Name)}");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var ids = (await vouchers.ListBatchAsync(Name, HttpContext.RequestAborted)).Select(v => v.Id).ToList();
        var count = await vouchers.DeleteUnusedAsync(ids, await current.GetAsync(), HttpContext.RequestAborted);
        TempData.Success(string.Format(Strings.Vouchers_Deleted, count));
        return count == ids.Count ? Redirect("/admin/vouchers") : Redirect($"/admin/batch?name={Uri.EscapeDataString(Name)}");
    }

    private static string Iso(DateTime? utc) => utc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
    private static string Escape(string text) => text.Contains(';') || text.Contains('"') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    private static string Safe(string name) => new(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
}

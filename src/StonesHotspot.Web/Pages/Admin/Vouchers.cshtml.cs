using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

[Authorize(Policy = StaffPolicies.Admin)]
public sealed class VouchersModel(VoucherService vouchers, TariffService tariffs, CurrentStaff current) : PageModel
{
    public IReadOnlyList<BatchSummary> Batches { get; private set; } = [];
    public IReadOnlyList<Tariff> Tariffs { get; private set; } = [];

    [BindProperty] public int TariffId { get; set; }
    [BindProperty] public int Count { get; set; } = 21;
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public bool SingleUse { get; set; }

    public async Task OnGetAsync()
    {
        Batches = await vouchers.ListBatchesAsync(HttpContext.RequestAborted);
        Tariffs = await tariffs.ListAsync(ct: HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        try
        {
            var created = await vouchers.GenerateBatchAsync(TariffId, Count, Name ?? "", SingleUse, await current.GetAsync(), HttpContext.RequestAborted);
            TempData.Success(string.Format(Strings.Vouchers_Created, created.Count));
            return Redirect($"/admin/batch?name={Uri.EscapeDataString(created[0].BatchName)}");
        }
        catch (BusinessRuleException)
        {
            TempData.Error(string.Format(Strings.Vouchers_InvalidCount, VoucherService.MaxBatchSize));
            return RedirectToPage();
        }
    }
}

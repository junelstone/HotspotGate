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
public sealed class TariffsModel(TariffService tariffs, CurrentStaff current) : PageModel
{
    public IReadOnlyList<Tariff> Tariffs { get; private set; } = [];

    public async Task OnGetAsync() => Tariffs = await tariffs.ListAsync(includeInactive: true, HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        try
        {
            await tariffs.DeleteAsync(id, await current.GetAsync(), HttpContext.RequestAborted);
            TempData.Success(Strings.Tariff_Deleted);
        }
        catch (BusinessRuleException)
        {
            TempData.Error(Strings.Tariff_InUse);
        }
        return RedirectToPage();
    }
}

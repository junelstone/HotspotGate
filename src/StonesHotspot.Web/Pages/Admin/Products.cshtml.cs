using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Products sold at the till, their groups and stock (Pro).</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class ProductsModel(ProductService products, CurrentStaff current, ILicenseService licence) : PageModel
{
    public bool Enabled => licence.IsAllowed(Feature.Products);
    public IReadOnlyList<Product> Products { get; private set; } = [];
    public IReadOnlyList<ProductGroup> Groups { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (!Enabled)
            return;
        Products = await products.ListAsync(includeInactive: true, HttpContext.RequestAborted);
        Groups = await products.ListGroupsAsync(HttpContext.RequestAborted);
    }

    public Task<IActionResult> OnPostAddStockAsync(int id, int quantity) =>
        RunAsync(async actor =>
        {
            var p = await products.AddStockAsync(id, quantity, actor, HttpContext.RequestAborted);
            TempData.Success(string.Format(Strings.Products_StockNow, p.Name, p.Stock));
        });

    public Task<IActionResult> OnPostCountStockAsync(int id, int counted) =>
        RunAsync(async actor =>
        {
            var p = await products.CountStockAsync(id, counted, actor, HttpContext.RequestAborted);
            TempData.Success(string.Format(Strings.Products_StockNow, p.Name, p.Stock));
        });

    public Task<IActionResult> OnPostAddGroupAsync(string? name) =>
        RunAsync(async actor =>
        {
            await products.CreateGroupAsync(name ?? "", actor, HttpContext.RequestAborted);
            TempData.Success(Strings.Common_Saved);
        });

    public Task<IActionResult> OnPostRenameGroupAsync(int id, string? name) =>
        RunAsync(async actor =>
        {
            await products.RenameGroupAsync(id, name ?? "", actor, HttpContext.RequestAborted);
            TempData.Success(Strings.Common_Saved);
        });

    public Task<IActionResult> OnPostDeleteGroupAsync(int id) =>
        RunAsync(async actor =>
        {
            await products.DeleteGroupAsync(id, actor, HttpContext.RequestAborted);
            TempData.Success(Strings.Products_GroupDeleted);
        });

    private async Task<IActionResult> RunAsync(Func<Staff, Task> action)
    {
        if (!Enabled)
            return RedirectToPage();
        try
        {
            await action(await current.GetAsync(HttpContext.RequestAborted));
        }
        catch (BusinessRuleException ex)
        {
            TempData.Error(ex.Code == ErrorCodes.DuplicateName ? Strings.Products_DuplicateName : Strings.Common_InvalidValue);
        }
        return RedirectToPage();
    }
}

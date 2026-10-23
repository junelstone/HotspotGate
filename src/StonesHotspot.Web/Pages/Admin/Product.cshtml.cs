using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Form values of a product as typed (price "1 000", VAT "18,9").</summary>
public sealed class ProductInput
{
    public string Name { get; set; } = "";
    public int? GroupId { get; set; }
    public string Price { get; set; } = "";
    public string? Vat { get; set; }
    public int Stock { get; set; }
    public bool TrackStock { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public static ProductInput From(Product p) => new()
    {
        Name = p.Name, GroupId = p.GroupId, Price = p.Price.ToNumberString(XafStyle.Ascii),
        Vat = p.VatRate == 0 ? "" : (p.VatRate * 100).ToString("0.##", CultureInfo.CurrentCulture),
        Stock = p.Stock, TrackStock = p.TrackStock, IsActive = p.IsActive,
    };

    /// <summary>"18,9", "18.9" or "18,9 %" → 0.189; empty → 0.</summary>
    public static bool TryParseVat(string? text, out decimal rate)
    {
        rate = 0;
        text = text?.Replace("%", "").Trim().Replace(',', '.');
        if (string.IsNullOrEmpty(text))
            return true;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent) || percent >= 100)
            return false;
        rate = Math.Round(percent / 100, 4);
        return true;
    }
}

[Authorize(Policy = StaffPolicies.Admin)]
public sealed class ProductModel(ProductService products, CurrentStaff current, ILicenseService licence) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public ProductInput Input { get; set; } = new();
    public IReadOnlyList<ProductGroup> Groups { get; private set; } = [];
    public bool Enabled => licence.IsAllowed(Feature.Products);
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Groups = await products.ListGroupsAsync(HttpContext.RequestAborted);
        if (Id is { } id)
        {
            var product = await products.FindAsync(id, HttpContext.RequestAborted);
            if (product is null)
                return NotFound();
            Input = ProductInput.From(product);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ct = HttpContext.RequestAborted;
        Groups = await products.ListGroupsAsync(ct);
        if (!Enabled)
            return Page();
        if (!Xaf.TryParse(Input.Price, out var price))
        {
            Error = Strings.Common_InvalidAmount;
            return Page();
        }
        if (!ProductInput.TryParseVat(Input.Vat, out var vat))
        {
            Error = Strings.Products_InvalidVat;
            return Page();
        }
        var product = new Product
        {
            Name = Input.Name ?? "", GroupId = Input.GroupId, Price = price, VatRate = vat, Stock = Input.Stock,
            TrackStock = Input.TrackStock, IsActive = Input.IsActive,
        };
        try
        {
            var actor = await current.GetAsync(ct);
            if (Id is { } id)
                await products.UpdateAsync(id, product, actor, ct);
            else
                await products.CreateAsync(product, actor, ct);
            TempData.Success(Strings.Common_Saved);
            return Redirect("/admin/products");
        }
        catch (BusinessRuleException ex)
        {
            Error = ex.Code == ErrorCodes.DuplicateName ? Strings.Products_DuplicateName : Strings.Common_InvalidValue;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is not { } id || !Enabled)
            return Redirect("/admin/products");
        try
        {
            await products.DeleteAsync(id, await current.GetAsync(HttpContext.RequestAborted), HttpContext.RequestAborted);
            TempData.Success(Strings.Products_Deleted);
        }
        catch (BusinessRuleException)
        {
            TempData.Error(Strings.Products_InUse);
        }
        return Redirect("/admin/products");
    }
}

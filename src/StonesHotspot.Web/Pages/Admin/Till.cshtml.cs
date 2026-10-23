using System.Globalization;
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

/// <summary>The till (Pro): Wi-Fi plans and products in one cart, one receipt.</summary>
public sealed class TillModel(
    CurrentStaff current, TariffService tariffs, ProductService products, SalesService sales, CashService cash,
    ILicenseService licence) : PageModel
{
    public bool Enabled => licence.IsAllowed(Feature.PointOfSale);
    public IReadOnlyList<Tariff> Plans { get; private set; } = [];
    public IReadOnlyList<Product> Products { get; private set; } = [];
    public bool NeedsShift { get; private set; }

    /// <summary>The cart as the page's script sends it: "t12x2,p3x1" (t = plan, p = product, id, quantity).</summary>
    [BindProperty] public string? Cart { get; set; }
    [BindProperty] public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    [BindProperty] public string? Received { get; set; }

    public string? Error { get; private set; }

    public Task OnGetAsync() => LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!Enabled)
            return Page();
        var lines = ParseCart(Cart);
        if (lines is null || lines.Count == 0)
        {
            Error = Strings.Till_EmptyCart;
            return Page();
        }

        var ct = HttpContext.RequestAborted;
        string receipt;
        try
        {
            receipt = await sales.CheckoutAsync(lines, Method, await current.GetAsync(ct), licence.IsAllowed(Feature.Shifts), ct);
        }
        catch (BusinessRuleException ex)
        {
            Error = ex.Code switch
            {
                ErrorCodes.OutOfStock => string.Format(CultureInfo.CurrentUICulture, Strings.Till_OutOfStock, ex.Subject),
                ErrorCodes.ShiftRequired => Strings.Sell_OpenShiftFirst,
                _ => Strings.Till_Error,
            };
            await LoadAsync();
            return Page();
        }

        var total = (await sales.GetReceiptAsync(receipt, ct))?.Total ?? Xaf.Zero;
        var message = string.Format(CultureInfo.CurrentUICulture, Strings.Till_Done, receipt, total);
        if (Method == PaymentMethod.Cash && Xaf.TryParse(Received, out var received) && received >= total)
            message += " " + string.Format(CultureInfo.CurrentUICulture, Strings.Till_Change, received - total);
        TempData.Success(message);
        return Redirect($"/admin/receipt?n={Uri.EscapeDataString(receipt)}");
    }

    /// <summary>Parses "t12x2,p3x1"; null when malformed.</summary>
    public static List<CartLine>? ParseCart(string? text)
    {
        var lines = new List<CartLine>();
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kind = part[0] switch { 't' => CartItemKind.Plan, 'p' => CartItemKind.Product, _ => (CartItemKind?)null };
            var pieces = part[1..].Split('x');
            if (kind is null || pieces.Length != 2
                || !int.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || !int.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out var quantity))
                return null;
            lines.Add(new CartLine(kind.Value, id, quantity));
        }
        return lines;
    }

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (!Enabled)
            return;
        var me = await current.GetAsync(ct);
        Plans = await tariffs.ListAsync(ct: ct);
        Products = await products.ListAsync(ct: ct);
        NeedsShift = me.Role == StaffRole.Cashier && licence.IsAllowed(Feature.Shifts) && await cash.GetOpenShiftAsync(me.Id, ct) is null;
    }
}

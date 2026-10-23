using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Counter sale: new codes for a plan (printed on a ticket), or a pre-printed card.</summary>
public sealed class SellModel(CurrentStaff current, TariffService tariffs, VoucherService vouchers, CashService cash, ILicenseService licence) : PageModel
{
    public IReadOnlyList<Tariff> Tariffs { get; private set; } = [];

    /// <summary>Cashiers must open their shift (cash drawer) before selling when shifts are enabled (Pro).</summary>
    public bool NeedsShift { get; private set; }

    [BindProperty] public int TariffId { get; set; }
    [BindProperty] public int Quantity { get; set; } = 1;
    [BindProperty] public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    [BindProperty] public string? Code { get; set; }

    public string? Error { get; private set; }

    public Task OnGetAsync() => LoadAsync();

    public async Task<IActionResult> OnPostNewAsync()
    {
        await LoadAsync();
        if (NeedsShift)
            return Page();
        try
        {
            var (receipt, _) = await vouchers.SellNewAsync(TariffId, Quantity, Method, await current.GetAsync(), HttpContext.RequestAborted);
            return Redirect($"/admin/print?receipt={Uri.EscapeDataString(receipt)}&format=80");
        }
        catch (BusinessRuleException)
        {
            Error = Strings.Sell_Error;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCardAsync()
    {
        await LoadAsync();
        if (NeedsShift)
            return Page();
        try
        {
            var receipt = await vouchers.SellCodeAsync(Code ?? "", Method, await current.GetAsync(), HttpContext.RequestAborted);
            TempData.Success(string.Format(Strings.Sell_CardSold, VoucherCodeGenerator.Format(VoucherCodeGenerator.Normalize(Code)), receipt));
            return RedirectToPage();
        }
        catch (BusinessRuleException ex)
        {
            Error = ex.Code == ErrorCodes.VoucherNotSellable ? Strings.Sell_CardNotNew : Strings.Login_InvalidCode;
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        var me = await current.GetAsync(HttpContext.RequestAborted);
        Tariffs = await tariffs.ListAsync(ct: HttpContext.RequestAborted);
        if (TariffId == 0 && Tariffs.Count > 0)
            TariffId = Tariffs[0].Id;
        NeedsShift = me.Role == StaffRole.Cashier && licence.IsAllowed(Feature.Shifts)
                     && await cash.GetOpenShiftAsync(me.Id, HttpContext.RequestAborted) is null;
    }
}

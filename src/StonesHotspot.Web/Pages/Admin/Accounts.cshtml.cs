using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Customer accounts: everyone can search and top up; admins create them.</summary>
public sealed class AccountsModel(AccountService accounts, TariffService tariffs, CurrentStaff current) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    public IReadOnlyList<UserAccount> Accounts { get; private set; } = [];
    public IReadOnlyList<Tariff> Tariffs { get; private set; } = [];

    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public string? FirstName { get; set; }
    [BindProperty] public string? LastName { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int TariffId { get; set; }
    [BindProperty] public string? Deposit { get; set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!User.IsAdmin())
            return Forbid();
        var password = Password ?? "";
        Password = null;
        var initial = Xaf.Zero;
        if (!string.IsNullOrWhiteSpace(Deposit) && (!Xaf.TryParse(Deposit, out initial) || initial.IsNegative))
        {
            Error = Strings.Common_InvalidAmount;
            await LoadAsync();
            return Page();
        }

        try
        {
            var me = await current.GetAsync(HttpContext.RequestAborted);
            var account = await accounts.CreateAsync(new UserAccount
            {
                Username = Username ?? "", FirstName = Blank(FirstName), LastName = Blank(LastName), Phone = Blank(Phone), TariffId = TariffId,
            }, password, me, HttpContext.RequestAborted);
            if (initial > Xaf.Zero)
                await accounts.DepositAsync(account.Id, initial, PaymentMethod.Cash, me, HttpContext.RequestAborted);
            TempData.Success(Strings.Common_Saved);
            return Redirect($"/admin/account?id={account.Id}");
        }
        catch (BusinessRuleException ex)
        {
            Error = ex.Code switch
            {
                ErrorCodes.DuplicateName => Strings.Account_UsernameTaken,
                ErrorCodes.WeakPassword => string.Format(Strings.Password_TooWeak, AccountService.MinPasswordLength),
                _ => ex.Message,
            };
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        Accounts = await accounts.ListAsync(Search, 200, HttpContext.RequestAborted);
        Tariffs = await tariffs.ListAsync(ct: HttpContext.RequestAborted);
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

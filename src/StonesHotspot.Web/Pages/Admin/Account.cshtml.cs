using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

public sealed class AccountModel(
    AccountService accounts, TariffService tariffs, CashService cash, HotspotDbContext db, CurrentStaff current, ILicenseService licence) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public UserAccount Account { get; private set; } = null!;
    public IReadOnlyList<Tariff> Tariffs { get; private set; } = [];
    public IReadOnlyList<Transaction> History { get; private set; } = [];

    [BindProperty] public string? Amount { get; set; }
    [BindProperty] public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    [BindProperty] public string? FirstName { get; set; }
    [BindProperty] public string? LastName { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int TariffId { get; set; }
    [BindProperty] public DateOnly? ExpiresOn { get; set; }
    [BindProperty] public string? Overdraft { get; set; }
    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public string? NewPassword { get; set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostDepositAsync()
    {
        if (!await LoadAsync())
            return NotFound();
        var me = await current.GetAsync(HttpContext.RequestAborted);
        if (me.Role == StaffRole.Cashier && licence.IsAllowed(Feature.Shifts) && await cash.GetOpenShiftAsync(me.Id) is null)
        {
            Error = Strings.Sell_OpenShiftFirst;
            return Page();
        }
        if (!Xaf.TryParse(Amount, out var amount) || amount <= Xaf.Zero || Method == PaymentMethod.AccountBalance)
        {
            Error = Strings.Common_InvalidAmount;
            return Page();
        }
        var receipt = await accounts.DepositAsync(Id, amount, Method, me, HttpContext.RequestAborted);
        TempData.Success(string.Format(Strings.Account_Deposited, amount, receipt));
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!User.IsAdmin())
            return Forbid();
        if (!await LoadAsync())
            return NotFound();
        var overdraft = Xaf.Zero;
        if (!string.IsNullOrWhiteSpace(Overdraft) && !Xaf.TryParse(Overdraft, out overdraft))
        {
            Error = Strings.Common_InvalidAmount;
            return Page();
        }
        try
        {
            await accounts.UpdateAsync(Id, new UserAccount
            {
                FirstName = Blank(FirstName), LastName = Blank(LastName), Phone = Blank(Phone), TariffId = TariffId,
                ExpiresAt = ExpiresOn is { } d ? d.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Local).ToUniversalTime() : null,
                AllowedOverdraft = overdraft, Enabled = Enabled,
            }, await current.GetAsync(), HttpContext.RequestAborted);
            if (!string.IsNullOrEmpty(NewPassword))
                await accounts.SetPasswordAsync(Id, NewPassword, await current.GetAsync(), HttpContext.RequestAborted);
            NewPassword = null;
            TempData.Success(Strings.Common_Saved);
            return RedirectToPage(new { id = Id });
        }
        catch (BusinessRuleException ex)
        {
            NewPassword = null;
            Error = ex.Code == ErrorCodes.WeakPassword ? string.Format(Strings.Password_TooWeak, AccountService.MinPasswordLength) : ex.Message;
            return Page();
        }
    }

    private async Task<bool> LoadAsync()
    {
        var account = await accounts.FindAsync(Id, HttpContext.RequestAborted);
        if (account is null)
            return false;
        Account = account;
        Tariffs = await tariffs.ListAsync(includeInactive: true, HttpContext.RequestAborted);
        History = await db.Transactions.AsNoTracking().Where(t => t.UserAccountId == Id)
            .OrderByDescending(t => t.Timestamp).Take(50).ToListAsync(HttpContext.RequestAborted);
        if (HttpContext.Request.Method == "GET")
        {
            (FirstName, LastName, Phone, TariffId, Enabled) = (account.FirstName, account.LastName, account.Phone, account.TariffId, account.Enabled);
            ExpiresOn = account.ExpiresAt is { } e ? DateOnly.FromDateTime(e.ToLocalTime()) : null;
            Overdraft = account.AllowedOverdraft.ToNumberString(XafStyle.Ascii);
        }
        return true;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

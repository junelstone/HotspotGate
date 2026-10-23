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

[Authorize(Policy = StaffPolicies.Admin)]
public sealed class StaffModel(StaffService staff, CurrentStaff current, ILicenseService licence) : PageModel
{
    public IReadOnlyList<Staff> Members { get; private set; } = [];
    public bool CanAdd { get; private set; }

    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? DisplayName { get; set; }
    [BindProperty] public StaffRole Role { get; set; } = StaffRole.Cashier;
    [BindProperty] public string? Password { get; set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await LoadAsync();
        var password = Password ?? "";
        Password = null;
        if (!CanAdd)
            return Page();
        try
        {
            await staff.CreateAsync(Username ?? "", password, Role, string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
                await current.GetAsync(), HttpContext.RequestAborted);
            TempData.Success(string.Format(Strings.Staff_Created, Username));
            return RedirectToPage();
        }
        catch (BusinessRuleException ex)
        {
            Error = Message(ex);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostEnableAsync(int id, bool enabled)
    {
        try
        {
            await staff.SetEnabledAsync(id, enabled, await current.GetAsync(), HttpContext.RequestAborted);
            TempData.Success(Strings.Common_Saved);
        }
        catch (BusinessRuleException ex)
        {
            TempData.Error(Message(ex));
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync(int id, string? temporaryPassword)
    {
        try
        {
            await staff.ResetPasswordAsync(id, temporaryPassword ?? "", await current.GetAsync(), HttpContext.RequestAborted);
            TempData.Success(Strings.Staff_PasswordReset);
        }
        catch (BusinessRuleException ex)
        {
            TempData.Error(Message(ex));
        }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Members = await staff.ListAsync(HttpContext.RequestAborted);
        // Basic edition: one staff account (the owner). More accounts are a Pro feature.
        CanAdd = licence.IsAllowed(Feature.MultipleStaffAccounts);
    }

    private static string Message(BusinessRuleException ex) => ex.Code switch
    {
        ErrorCodes.DuplicateName => Strings.Account_UsernameTaken,
        ErrorCodes.WeakPassword => string.Format(Strings.Password_TooWeak, StaffService.MinPasswordLength),
        ErrorCodes.NotAllowed => Strings.Staff_CannotDisable,
        _ => ex.Message,
    };
}

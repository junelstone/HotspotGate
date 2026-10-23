using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

public sealed class PasswordModel(StaffService staff, CurrentStaff current, AppPaths paths) : PageModel
{
    /// <summary>Written by the service on first start (see Service/InitialAdminPasswordFile).</summary>
    public const string InitialPasswordFile = "initial-admin-password.txt";

    [BindProperty] public string? Current { get; set; }
    [BindProperty] public string? New { get; set; }
    [BindProperty] public string? Confirm { get; set; }

    public bool Forced => User.FindFirst(StaffClaims.MustChangePassword)?.Value == "1";
    public string? Error { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var (currentPassword, newPassword, confirm) = (Current ?? "", New ?? "", Confirm ?? "");
        Current = New = Confirm = null;

        if (newPassword != confirm)
        {
            Error = Strings.Password_Mismatch;
            return Page();
        }

        var me = await current.GetAsync(HttpContext.RequestAborted);
        try
        {
            await staff.ChangePasswordAsync(me.Id, currentPassword, newPassword, HttpContext.RequestAborted);
        }
        catch (BusinessRuleException ex)
        {
            Error = ex.Code switch
            {
                ErrorCodes.WrongPassword => Strings.Password_WrongCurrent,
                _ => string.Format(Strings.Password_TooWeak, StaffService.MinPasswordLength),
            };
            return Page();
        }

        // The first admin password was written to a file on first start: no longer needed.
        if (me.Username == DatabaseInitializer.AdminUsername)
            System.IO.File.Delete(Path.Combine(paths.Root, InitialPasswordFile));

        var updated = await staff.FindAsync(me.Id, HttpContext.RequestAborted);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffClaims.Create(updated!));
        TempData.Success(Strings.Password_Changed);
        return Redirect("/admin");
    }
}

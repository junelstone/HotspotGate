using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

public sealed class LoginModel(StaffService staff, LoginThrottle throttle, TimeProvider clock) : PageModel
{
    [BindProperty]
    public string? Username { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Redirect("/admin") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var key = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var password = Password ?? "";
        Password = null;

        if (throttle.LockedUntil(key) is { } until)
        {
            Error = string.Format(Strings.Login_LockedOut, Math.Max(1, (int)Math.Ceiling((until - clock.GetUtcNow()).TotalMinutes)));
            return Page();
        }

        var member = await staff.AuthenticateAsync(Username ?? "", password, HttpContext.RequestAborted);
        if (member is null)
        {
            throttle.Failure(key);
            Error = Strings.Login_InvalidCredentials;
            return Page();
        }

        throttle.Success(key);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffClaims.Create(member));
        if (member.MustChangePassword)
            return RedirectToPage("/Admin/Password");
        return Url.IsLocalUrl(ReturnUrl) && ReturnUrl!.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            ? LocalRedirect(ReturnUrl)
            : Redirect("/admin");
    }
}

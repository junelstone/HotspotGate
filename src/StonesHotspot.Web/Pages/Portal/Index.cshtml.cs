using Microsoft.AspNetCore.Mvc;
using StonesHotspot.Core;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web.Pages.Portal;

/// <summary>The login page customers land on: access code, or account username/password.</summary>
public sealed class IndexModel(
    SettingsService settings,
    AppPaths paths,
    SessionService sessions,
    PortalClient client,
    AccessRefreshSignal refresh,
    ILicenseService licence,
    TimeProvider clock) : PortalPageModel(settings, paths)
{
    public static readonly TimeSpan ExplainEndedWithin = TimeSpan.FromMinutes(15);

    [BindProperty(SupportsGet = true)]
    public string? Code { get; set; }

    [BindProperty]
    public string? Username { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    /// <summary>The page the customer originally asked for (set by the redirect).</summary>
    [BindProperty(SupportsGet = true, Name = "url")]
    public string? OriginalUrl { get; set; }

    [BindProperty(SupportsGet = true, Name = "mode")]
    public string? Mode { get; set; }

    public bool Unavailable { get; private set; }
    public string? Error { get; private set; }
    public string? Info { get; private set; }
    public bool AccountOpen => Mode == "account";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!licence.GetStatus().HotspotAllowed)
        {
            Unavailable = true;
            return Page();
        }

        var device = client.Identify(HttpContext);
        if (device.Mac is not null)
        {
            if (await sessions.GetStatusForDeviceAsync(device.Mac, HttpContext.RequestAborted) is not null)
                return RedirectToPage("/Portal/Status", new { url = OriginalUrl });
            if (await sessions.GetRecentlyEndedAsync(device.Mac, ExplainEndedWithin, HttpContext.RequestAborted) is { EndReason: { } reason })
                Info = PortalTexts.Ended(reason);
        }
        return Page();
    }

    public Task<IActionResult> OnPostVoucherAsync() =>
        LoginAsync(device => sessions.LoginWithVoucherAsync(device.Mac!, device.Ip?.ToString(), Code ?? "", HttpContext.RequestAborted));

    public Task<IActionResult> OnPostAccountAsync()
    {
        Mode = "account";
        var password = Password ?? "";
        return LoginAsync(device => sessions.LoginWithAccountAsync(device.Mac!, device.Ip?.ToString(), Username ?? "", password, HttpContext.RequestAborted));
    }

    private async Task<IActionResult> LoginAsync(Func<PortalDevice, Task<LoginResult>> login)
    {
        Password = null; // never echoed back into the page (the account handler kept its own copy)
        if (!licence.GetStatus().HotspotAllowed)
        {
            Unavailable = true;
            return Page();
        }

        var device = client.Identify(HttpContext);
        if (device.Mac is null)
        {
            Error = Strings.Portal_DeviceUnknown;
            return Page();
        }

        var result = await login(device);
        if (!result.Succeeded)
        {
            Error = PortalTexts.LoginError(result, clock.GetUtcNow().UtcDateTime);
            return Page();
        }

        refresh.Request(); // let the device through now, not at the next tick
        var welcome = await Settings.GetAsync(SettingKeys.PortalWelcomeUrl, HttpContext.RequestAborted);
        return RedirectToPage("/Portal/Status", new { next = PortalTarget.Choose(welcome, OriginalUrl), welcome = true });
    }
}

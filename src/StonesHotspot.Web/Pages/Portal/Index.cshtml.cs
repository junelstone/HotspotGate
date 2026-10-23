using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using StonesHotspot.Core;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web.Pages.Portal;

/// <summary>
/// The login page customers land on: access code, account, and (when switched on) free access, the password of the
/// day and SMS code.
/// </summary>
public sealed class IndexModel(
    SettingsService settings,
    AppPaths paths,
    SessionService sessions,
    FreeAccessService free,
    SmsLoginService smsLogin,
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

    [BindProperty]
    public bool AcceptTerms { get; set; }

    [BindProperty]
    public string? SharedPassword { get; set; }

    [BindProperty]
    public string? Phone { get; set; }

    [BindProperty]
    public string? SmsCode { get; set; }

    /// <summary>Which free modes the page offers (on, with a plan, and in the licence).</summary>
    public bool OffersFree { get; private set; }
    public bool OffersPassword { get; private set; }
    public bool OffersSms { get; private set; }

    /// <summary>The code was sent: the page asks for it (Phone holds the normalized number).</summary>
    public bool SmsCodeStep { get; private set; }

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
        await LoadModesAsync();

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

    public async Task<IActionResult> OnPostFreeAsync()
    {
        Mode = "free";
        await LoadModesAsync();
        if (!OffersFree)
            return Page();
        if (!AcceptTerms)
        {
            Error = Strings.Free_AcceptRequired;
            return Page();
        }
        return await LoginAsync(device => free.LoginWithTermsAsync(device.Mac!, device.Ip?.ToString(), HttpContext.RequestAborted));
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        Mode = "password";
        await LoadModesAsync();
        if (!OffersPassword)
            return Page();
        var password = SharedPassword ?? "";
        SharedPassword = null;
        ModelState.Remove(nameof(SharedPassword));
        return await LoginAsync(device => free.LoginWithPasswordAsync(device.Mac!, device.Ip?.ToString(), password, HttpContext.RequestAborted));
    }

    public async Task<IActionResult> OnPostSmsSendAsync()
    {
        Mode = "sms";
        await LoadModesAsync();
        if (!OffersSms)
            return Page();
        var device = client.Identify(HttpContext);
        if (device.Mac is null)
        {
            Error = Strings.Portal_DeviceUnknown;
            return Page();
        }

        var result = await smsLogin.RequestCodeAsync(device.Mac, device.Ip?.ToString(), Phone, HttpContext.RequestAborted);
        if (result.Status == SmsCodeStatus.Sent)
        {
            Phone = result.Phone;
            ModelState.Remove(nameof(Phone));   // show the normalized number, not what was typed
            SmsCodeStep = true;
            Info = string.Format(CultureInfo.CurrentUICulture, Strings.Sms_Sent, Core.Rules.PhoneNumber.Format(result.Phone!));
        }
        else
        {
            Error = PortalTexts.SmsError(result);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSmsVerifyAsync()
    {
        Mode = "sms";
        await LoadModesAsync();
        if (!OffersSms)
            return Page();
        SmsCodeStep = true;
        var code = SmsCode;
        SmsCode = null;
        ModelState.Remove(nameof(SmsCode));
        return await LoginAsync(device => smsLogin.VerifyAsync(device.Mac!, device.Ip?.ToString(), Phone, code, HttpContext.RequestAborted));
    }

    private async Task LoadModesAsync()
    {
        var modes = await free.GetSettingsAsync(HttpContext.RequestAborted);
        var freeModes = licence.IsAllowed(Feature.FreeAccessModes);
        OffersFree = freeModes && modes.Terms.Usable;
        OffersPassword = freeModes && modes.Password.Usable;
        OffersSms = licence.IsAllowed(Feature.SmsLogin) && modes.Sms.Usable;
    }

    private async Task<IActionResult> LoginAsync(Func<PortalDevice, Task<LoginResult>> login)
    {
        Password = null; // never echoed back into the page (the account handler kept its own copy)
        if (!licence.GetStatus().HotspotAllowed)
        {
            Unavailable = true;
            return Page();
        }
        await LoadModesAsync();

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

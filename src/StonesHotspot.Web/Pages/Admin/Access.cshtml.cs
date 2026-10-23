using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Core.Sms;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Free access, shared password and SMS login settings, SMS test and log (admins, Pro).</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class AccessModel(
    SettingsService settings, FreeAccessService free, SmsService sms, TariffService tariffs, ISmsTransportFactory transports,
    CurrentStaff current, ILicenseService licence) : PageModel
{
    public const int MaxPerDay = 100;

    [BindProperty] public bool FreeEnabled { get; set; }
    [BindProperty] public int? FreeTariffId { get; set; }
    [BindProperty] public int FreePerDay { get; set; } = 1;
    [BindProperty] public bool PasswordEnabled { get; set; }
    [BindProperty] public int? PasswordTariffId { get; set; }
    [BindProperty] public int PasswordPerDay { get; set; } = 3;
    [BindProperty] public string? PasswordValue { get; set; }
    [BindProperty] public bool PasswordDaily { get; set; }
    [BindProperty] public bool SmsEnabled { get; set; }
    [BindProperty] public int? SmsTariffId { get; set; }
    [BindProperty] public int SmsPerDay { get; set; } = 1;
    [BindProperty] public int SmsPerNumber { get; set; } = 3;
    [BindProperty] public int SmsPerDevice { get; set; } = 3;
    [BindProperty] public int SmsTotal { get; set; } = 200;
    [BindProperty] public string? SmsPrefixes { get; set; }
    [BindProperty] public string Transport { get; set; } = "none";
    [BindProperty] public string? ModemPort { get; set; }
    [BindProperty] public int ModemBaud { get; set; } = 115200;

    /// <summary>Typed only to change it: the saved address (with its API key) is never sent back to the browser.</summary>
    [BindProperty] public string? HttpUrl { get; set; }
    [BindProperty] public string? HttpBody { get; set; }
    [BindProperty] public bool ClearProvider { get; set; }
    [BindProperty] public string? HttpSuccessText { get; set; }
    [BindProperty] public string? TestPhone { get; set; }

    public bool FreeAllowed => licence.IsAllowed(Feature.FreeAccessModes);
    public bool SmsAllowed => licence.IsAllowed(Feature.SmsLogin);
    public IReadOnlyList<Tariff> Tariffs { get; private set; } = [];
    public IReadOnlyList<string> Ports { get; private set; } = [];
    public IReadOnlyList<SmsMessage> Log { get; private set; } = [];
    public string? ProviderHost { get; private set; }
    public bool ProviderHasBody { get; private set; }
    public string? PasswordToday { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var modes = await free.GetSettingsAsync(ct);
        (FreeEnabled, FreeTariffId, FreePerDay) = (modes.Terms.Enabled, modes.Terms.TariffId, modes.Terms.PerDevicePerDay);
        (PasswordEnabled, PasswordTariffId, PasswordPerDay) = (modes.Password.Enabled, modes.Password.TariffId, modes.Password.PerDevicePerDay);
        PasswordDaily = modes.PasswordDaily;
        PasswordValue = await settings.GetAsync(SettingKeys.PasswordValue, ct);
        (SmsEnabled, SmsTariffId, SmsPerDay) = (modes.Sms.Enabled, modes.Sms.TariffId, modes.Sms.PerDevicePerDay);
        SmsPerNumber = await settings.GetIntAsync(SettingKeys.SmsPerNumberPerDay, 3, ct);
        SmsPerDevice = await settings.GetIntAsync(SettingKeys.SmsPerDevicePerDay, 3, ct);
        SmsTotal = await settings.GetIntAsync(SettingKeys.SmsTotalPerDay, 200, ct);
        SmsPrefixes = await settings.GetAsync(SettingKeys.SmsPrefixes, ct);
        var transport = await sms.GetSettingsAsync(ct);
        Transport = transport.Kind.ToString().ToLowerInvariant();
        ModemPort = transport.ModemPort;
        ModemBaud = transport.ModemBaud;
        HttpSuccessText = transport.HttpSuccessText;
        await LoadAsync(transport);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (new[] { FreePerDay, PasswordPerDay, SmsPerDay, SmsPerNumber, SmsPerDevice }.Any(n => n is < 1 or > MaxPerDay) || SmsTotal is < 1 or > 100_000
            || Transport is not ("none" or "modem" or "http") || ModemBaud is < 1200 or > 921_600)
            Error = Strings.Common_InvalidValue;
        else if (PasswordEnabled && !PasswordDaily && string.IsNullOrWhiteSpace(PasswordValue))
            Error = Strings.Access_PasswordRequired;
        else if ((FreeEnabled && FreeTariffId is null) || (PasswordEnabled && PasswordTariffId is null) || (SmsEnabled && SmsTariffId is null))
            Error = Strings.Access_TariffRequired;
        else if (!string.IsNullOrWhiteSpace(HttpUrl) && !(Uri.TryCreate(HttpUrl.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            Error = Strings.Access_InvalidUrl;
        if (Error is not null)
        {
            await LoadAsync(await sms.GetSettingsAsync(ct));
            return Page();
        }

        var me = await current.GetAsync(ct);
        async Task Save(string key, string? value)
        {
            var text = value?.Trim() ?? "";
            if ((await settings.GetAsync(key, ct) ?? "") != text)
                await settings.SetAsync(key, text, me, ct);
        }
        string Bool(bool b) => b ? "true" : "false";
        string Int(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "";

        if (FreeAllowed)
        {
            await Save(SettingKeys.FreeEnabled, Bool(FreeEnabled));
            await Save(SettingKeys.FreeTariffId, Int(FreeTariffId));
            await Save(SettingKeys.FreePerDay, Int(FreePerDay));
            await Save(SettingKeys.PasswordEnabled, Bool(PasswordEnabled));
            await Save(SettingKeys.PasswordTariffId, Int(PasswordTariffId));
            await Save(SettingKeys.PasswordPerDay, Int(PasswordPerDay));
            await Save(SettingKeys.PasswordValue, PasswordValue);
            await Save(SettingKeys.PasswordDaily, Bool(PasswordDaily));
        }
        if (SmsAllowed)
        {
            await Save(SettingKeys.SmsEnabled, Bool(SmsEnabled));
            await Save(SettingKeys.SmsTariffId, Int(SmsTariffId));
            await Save(SettingKeys.SmsPerDay, Int(SmsPerDay));
            await Save(SettingKeys.SmsPerNumberPerDay, Int(SmsPerNumber));
            await Save(SettingKeys.SmsPerDevicePerDay, Int(SmsPerDevice));
            await Save(SettingKeys.SmsTotalPerDay, Int(SmsTotal));
            await Save(SettingKeys.SmsPrefixes, string.Join(", ", PhoneNumber.ParsePrefixes(SmsPrefixes)));
            await Save(SettingKeys.SmsTransport, Transport);
            await Save(SettingKeys.SmsModemPort, ModemPort?.ToUpperInvariant());
            await Save(SettingKeys.SmsModemBaud, Int(ModemBaud));
            await Save(SettingKeys.SmsHttpSuccessText, HttpSuccessText);
            if (ClearProvider)
                await sms.SaveProviderAsync("", "", me, ct);
            else if (!string.IsNullOrWhiteSpace(HttpUrl))
                await sms.SaveProviderAsync(HttpUrl, HttpBody, me, ct);
        }
        TempData.Success(Strings.Common_Saved);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        var ct = HttpContext.RequestAborted;
        var phone = PhoneNumber.Normalize(TestPhone);
        if (!SmsAllowed || phone is null)
        {
            TempData.Error(Strings.Sms_InvalidNumber);
            return RedirectToPage();
        }
        var business = await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "";
        var error = await sms.SendAsync(phone, string.Format(CultureInfo.CurrentUICulture, Strings.Sms_TestText, business), SmsPurpose.Test, null, ct);
        if (error is null)
            TempData.Success(string.Format(CultureInfo.CurrentUICulture, Strings.Access_TestSent, PhoneNumber.Format(phone)));
        else
            TempData.Error(string.Format(CultureInfo.CurrentUICulture, Strings.Access_TestFailed, error));
        return RedirectToPage();
    }

    private async Task LoadAsync(SmsSettings transport)
    {
        var ct = HttpContext.RequestAborted;
        Tariffs = await tariffs.ListAsync(includeInactive: false, ct);
        Ports = transports.SerialPorts();
        Log = await sms.RecentAsync(50, ct);
        ProviderHost = Uri.TryCreate(transport.HttpUrl ?? "", UriKind.Absolute, out var uri) ? uri.Host : null;
        ProviderHasBody = !string.IsNullOrWhiteSpace(transport.HttpBody);
        PasswordToday = FreeAllowed ? await free.PasswordOfTheDayAsync(ct) : null;
    }
}

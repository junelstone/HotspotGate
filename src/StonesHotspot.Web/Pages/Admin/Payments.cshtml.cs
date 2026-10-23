using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Payments;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>MTN MoMo and Airtel Money: keys, plans sold online, connection test, recent payments (admins, Pro).</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class PaymentsModel(
    SettingsService settings, ISecretProtector secrets, MobileMoneyService payments, IPaymentProviderFactory providers,
    TariffService tariffs, CurrentStaff current, ILicenseService licence) : PageModel
{
    [BindProperty] public bool MtnEnabled { get; set; }
    [BindProperty] public string? MtnBaseUrl { get; set; }
    [BindProperty] public string? MtnTarget { get; set; }
    [BindProperty] public string? MtnCurrency { get; set; }
    [BindProperty] public string? MtnApiUser { get; set; }
    [BindProperty] public string? MtnSubscriptionKey { get; set; }
    [BindProperty] public string? MtnApiKey { get; set; }
    [BindProperty] public string? MtnPrefixes { get; set; }
    [BindProperty] public bool AirtelEnabled { get; set; }
    [BindProperty] public string? AirtelBaseUrl { get; set; }
    [BindProperty] public string? AirtelClientId { get; set; }
    [BindProperty] public string? AirtelClientSecret { get; set; }
    [BindProperty] public string? AirtelCountry { get; set; }
    [BindProperty] public string? AirtelCurrency { get; set; }
    [BindProperty] public string? AirtelPrefixes { get; set; }
    [BindProperty] public List<int> PlanIds { get; set; } = [];
    [BindProperty] public string? CallbackUrl { get; set; }
    [BindProperty] public int TimeoutSeconds { get; set; } = 180;

    public bool Enabled => licence.IsAllowed(Feature.MobileMoneyPayments);
    public PaymentSettings Current { get; private set; } = null!;
    public IReadOnlyList<Tariff> Tariffs { get; private set; } = [];
    public IReadOnlyList<MobilePayment> Recent { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
        var c = Current;
        (MtnEnabled, MtnBaseUrl, MtnTarget, MtnCurrency, MtnApiUser, MtnPrefixes) =
            (c.Mtn.Enabled, c.Mtn.BaseUrl, c.Mtn.TargetEnvironment, c.Mtn.Currency, c.Mtn.ApiUser, string.Join(", ", c.Mtn.Prefixes));
        (AirtelEnabled, AirtelBaseUrl, AirtelClientId, AirtelCountry, AirtelCurrency, AirtelPrefixes) =
            (c.Airtel.Enabled, c.Airtel.BaseUrl, c.Airtel.ClientId, c.Airtel.Country, c.Airtel.Currency, string.Join(", ", c.Airtel.Prefixes));
        PlanIds = c.TariffIds.ToList();
        CallbackUrl = c.CallbackBaseUrl;
        TimeoutSeconds = (int)c.Timeout.TotalSeconds;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (!Enabled)
            return RedirectToPage();
        static bool Url(string? u, bool httpsOnly) => string.IsNullOrWhiteSpace(u)
            || Uri.TryCreate(u.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == "https" || !httpsOnly && uri.Scheme == "http");
        if (!Url(MtnBaseUrl, false) || !Url(AirtelBaseUrl, false) || !Url(CallbackUrl, true))
            Error = Strings.Payments_InvalidUrl;
        else if (TimeoutSeconds is < 60 or > 900)
            Error = Strings.Common_InvalidValue;
        if (Error is not null)
        {
            await LoadAsync();
            return Page();
        }

        var me = await current.GetAsync(ct);
        async Task Save(string key, string? value)
        {
            var text = value?.Trim() ?? "";
            if ((await settings.GetAsync(key, ct) ?? "") != text)
                await settings.SetAsync(key, text, me, ct);
        }
        async Task SaveSecret(string key, string? value)
        {
            // Empty = keep the saved key: keys are never sent back to the browser.
            if (!string.IsNullOrWhiteSpace(value))
                await settings.SetAsync(key, secrets.Protect(value.Trim()), me, secret: true, ct);
        }

        await Save(SettingKeys.MtnEnabled, MtnEnabled ? "true" : "false");
        await Save(SettingKeys.MtnBaseUrl, MtnBaseUrl);
        await Save(SettingKeys.MtnTargetEnvironment, MtnTarget);
        await Save(SettingKeys.MtnCurrency, MtnCurrency?.ToUpperInvariant());
        await Save(SettingKeys.MtnApiUser, MtnApiUser);
        await Save(SettingKeys.MtnPrefixes, string.Join(", ", PhoneNumber.ParsePrefixes(MtnPrefixes)));
        await SaveSecret(SettingKeys.MtnSubscriptionKey, MtnSubscriptionKey);
        await SaveSecret(SettingKeys.MtnApiKey, MtnApiKey);
        await Save(SettingKeys.AirtelEnabled, AirtelEnabled ? "true" : "false");
        await Save(SettingKeys.AirtelBaseUrl, AirtelBaseUrl);
        await Save(SettingKeys.AirtelClientId, AirtelClientId);
        await Save(SettingKeys.AirtelCountry, AirtelCountry?.ToUpperInvariant());
        await Save(SettingKeys.AirtelCurrency, AirtelCurrency?.ToUpperInvariant());
        await Save(SettingKeys.AirtelPrefixes, string.Join(", ", PhoneNumber.ParsePrefixes(AirtelPrefixes)));
        await SaveSecret(SettingKeys.AirtelClientSecret, AirtelClientSecret);
        await Save(SettingKeys.PayTariffs, string.Join(",", PlanIds.Distinct().Order()));
        await Save(SettingKeys.PayCallbackUrl, CallbackUrl?.TrimEnd('/'));
        await Save(SettingKeys.PayTimeoutSeconds, TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        TempData.Success(Strings.Common_Saved);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(PaymentMethod method)
    {
        var config = await payments.GetSettingsAsync(HttpContext.RequestAborted);
        var provider = providers.Create(method, config);
        if (provider is null)
        {
            TempData.Error(string.Format(CultureInfo.CurrentUICulture, Strings.Payments_NotConfigured, AdminText.Payment(method)));
            return RedirectToPage();
        }
        try
        {
            await provider.TestConnectionAsync(HttpContext.RequestAborted);
            TempData.Success(string.Format(CultureInfo.CurrentUICulture, Strings.Payments_TestOk, AdminText.Payment(method)));
        }
        catch (PaymentException ex)
        {
            TempData.Error(string.Format(CultureInfo.CurrentUICulture, Strings.Payments_TestFailed, AdminText.Payment(method), ex.Message));
        }
        return RedirectToPage();
    }

    /// <summary>Asks the operator again (a payment that timed out may have been approved late).</summary>
    public async Task<IActionResult> OnPostCheckAsync(string reference)
    {
        var payment = await payments.CheckAsync(reference, force: true, HttpContext.RequestAborted);
        if (payment is not null)
            TempData.Success(string.Format(CultureInfo.CurrentUICulture, Strings.Payments_Checked, reference, Status(payment.Status)));
        return RedirectToPage();
    }

    public static string Status(MobilePaymentStatus status) => status switch
    {
        MobilePaymentStatus.Pending or MobilePaymentStatus.Processing => Strings.Payments_StatusPending,
        MobilePaymentStatus.Successful => Strings.Payments_StatusPaid,
        MobilePaymentStatus.TimedOut => Strings.Payments_StatusTimedOut,
        _ => Strings.Payments_StatusFailed,
    };

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        Current = await payments.GetSettingsAsync(ct);
        Tariffs = (await tariffs.ListAsync(ct: ct)).Where(t => t.Price.Amount > 0).ToList();
        Recent = Enabled ? await payments.ListAsync(null, null, 100, ct) : [];
    }
}

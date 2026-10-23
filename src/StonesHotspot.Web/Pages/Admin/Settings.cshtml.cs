using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web.Pages.Admin;

[Authorize(Policy = StaffPolicies.Admin)]
public sealed partial class SettingsModel(SettingsService settings, AppPaths paths, CurrentStaff current) : PageModel
{
    public const long MaxLogoBytes = 512 * 1024;
    public const int MaxReceiptText = 300;
    private static readonly string[] LogoExtensions = [".png", ".jpg", ".jpeg", ".svg", ".webp"];

    [BindProperty] public string? BusinessName { get; set; }
    [BindProperty] public string? WifiName { get; set; }
    [BindProperty] public string? AccentColor { get; set; }
    [BindProperty] public string? WelcomeUrl { get; set; }
    [BindProperty] public string? TermsFr { get; set; }
    [BindProperty] public string? TermsEn { get; set; }
    [BindProperty] public string DefaultLanguage { get; set; } = Languages.French;
    [BindProperty] public int IdleTimeout { get; set; } = 10;
    [BindProperty] public string? ReceiptHeader { get; set; }
    [BindProperty] public string? ReceiptFooter { get; set; }
    [BindProperty] public IFormFile? Logo { get; set; }

    public bool HasLogo { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        BusinessName = await settings.GetAsync(SettingKeys.BusinessName, ct);
        WifiName = await settings.GetAsync(SettingKeys.WifiName, ct);
        AccentColor = await settings.GetAsync(SettingKeys.PortalAccentColor, ct) ?? PortalBranding.DefaultAccent;
        WelcomeUrl = await settings.GetAsync(SettingKeys.PortalWelcomeUrl, ct);
        TermsFr = await settings.GetAsync(SettingKeys.PortalTermsFr, ct);
        TermsEn = await settings.GetAsync(SettingKeys.PortalTermsEn, ct);
        DefaultLanguage = await settings.GetAsync(SettingKeys.DefaultLanguage, ct) ?? Languages.French;
        IdleTimeout = await settings.GetIntAsync(SettingKeys.DefaultIdleTimeoutMinutes, 10, ct);
        ReceiptHeader = await settings.GetAsync(SettingKeys.ReceiptHeader, ct);
        ReceiptFooter = await settings.GetAsync(SettingKeys.ReceiptFooter, ct);
        HasLogo = PortalBranding.FindLogo(paths) is not null;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        HasLogo = PortalBranding.FindLogo(paths) is not null;
        if (string.IsNullOrWhiteSpace(BusinessName) || BusinessName.Length > 100)
            Error = Strings.Settings_NameRequired;
        else if (AccentColor is null || !ColorRegex().IsMatch(AccentColor))
            Error = Strings.Settings_InvalidColor;
        else if (!string.IsNullOrWhiteSpace(WelcomeUrl) && !PortalTarget.IsSafe(WelcomeUrl))
            Error = Strings.Settings_InvalidUrl;
        else if (IdleTimeout is < 1 or > 1440)
            Error = Strings.Settings_InvalidIdle;
        else if (DefaultLanguage is not (Languages.French or Languages.English))
            Error = Strings.Settings_InvalidLanguage;
        else if (ReceiptHeader?.Length > MaxReceiptText || ReceiptFooter?.Length > MaxReceiptText)
            Error = Strings.Common_InvalidValue;
        else if (Logo is not null && !LogoExtensions.Contains(Path.GetExtension(Logo.FileName).ToLowerInvariant()))
            Error = Strings.Settings_InvalidLogo;
        else if (Logo is { Length: > MaxLogoBytes })
            Error = Strings.Settings_LogoTooBig;
        if (Error is not null)
            return Page();

        var ct = HttpContext.RequestAborted;
        var me = await current.GetAsync(ct);
        async Task Save(string key, string? value)
        {
            var text = value?.Trim() ?? "";
            if ((await settings.GetAsync(key, ct) ?? "") != text)
                await settings.SetAsync(key, text, me, ct);
        }

        await Save(SettingKeys.BusinessName, BusinessName);
        await Save(SettingKeys.WifiName, WifiName);
        await Save(SettingKeys.PortalAccentColor, AccentColor);
        await Save(SettingKeys.PortalWelcomeUrl, WelcomeUrl);
        await Save(SettingKeys.PortalTermsFr, TermsFr);
        await Save(SettingKeys.PortalTermsEn, TermsEn);
        await Save(SettingKeys.DefaultLanguage, DefaultLanguage);
        await Save(SettingKeys.ReceiptHeader, ReceiptHeader?.Replace("\r", ""));
        await Save(SettingKeys.ReceiptFooter, ReceiptFooter?.Replace("\r", ""));
        await Save(SettingKeys.DefaultIdleTimeoutMinutes, IdleTimeout.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (Logo is not null)
        {
            DeleteLogo();
            var target = Path.Combine(paths.Branding, "logo" + Path.GetExtension(Logo.FileName).ToLowerInvariant().Replace(".jpeg", ".jpg"));
            await using var file = System.IO.File.Create(target);
            await Logo.CopyToAsync(file, ct);
        }

        TempData.Success(Strings.Common_Saved);
        return RedirectToPage();
    }

    public IActionResult OnPostRemoveLogo()
    {
        DeleteLogo();
        TempData.Success(Strings.Common_Saved);
        return RedirectToPage();
    }

    private void DeleteLogo()
    {
        while (PortalBranding.FindLogo(paths) is { } existing)
            System.IO.File.Delete(existing);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorRegex();
}

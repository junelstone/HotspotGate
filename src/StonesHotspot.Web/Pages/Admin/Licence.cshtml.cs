using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Licence state, Machine ID to send to Stone's IT Consulting, and activation of a code (admins).</summary>
public sealed class LicenceModel(ILicenseService licence, SettingsService settings, IConfiguration configuration, TimeProvider clock, IServiceProvider services) : PageModel
{
    public const int MaxFileBytes = 10_000;

    [BindProperty] public string? Code { get; set; }
    [BindProperty] public IFormFile? LicenceFile { get; set; }

    public LicenseStatus Status { get; private set; } = null!;
    public string WhatsAppLink { get; private set; } = "";
    public string? Error { get; private set; }
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!User.IsAdmin())
            return Forbid();
        var code = Code;
        if (LicenceFile is { Length: > 0 and <= MaxFileBytes })
        {
            using var reader = new StreamReader(LicenceFile.OpenReadStream());
            code = await reader.ReadToEndAsync(HttpContext.RequestAborted);
        }
        var result = string.IsNullOrWhiteSpace(code) ? ActivationResult.Invalid : licence.Activate(code);
        if (result == ActivationResult.Activated)
        {
            (services.GetService(typeof(LicenceSignals)) as LicenceSignals)?.RaiseActivated();
            var status = licence.GetStatus();
            TempData.Success(string.Format(CultureInfo.CurrentUICulture, Strings.Licence_Activated, status.LicensedEdition, status.ExpiresAt?.AddDays(-1).ToString("d", CultureInfo.CurrentUICulture)));
            return RedirectToPage();
        }
        Error = result switch
        {
            ActivationResult.OtherMachine => Strings.Licence_OtherMachine,
            ActivationResult.Expired => Strings.Licence_ExpiredCode,
            ActivationResult.Older => Strings.Licence_OlderCode,
            _ => Strings.Licence_InvalidCode,
        };
        await LoadAsync();
        return Page();
    }

    public static string EditionName(Edition edition) => edition switch
    {
        Edition.Trial => Strings.Licence_EditionTrial,
        Edition.Basic => "Basic",
        Edition.Pro => "Pro",
        _ => Strings.Licence_EditionNone,
    };

    private async Task LoadAsync()
    {
        Status = licence.GetStatus();
        var business = await settings.GetAsync(SettingKeys.BusinessName, HttpContext.RequestAborted) ?? "";
        var text = string.Format(CultureInfo.CurrentUICulture, Strings.Licence_WhatsAppText, business, licence.MachineId);
        var number = new string((configuration["Licensing:OwnerWhatsApp"] ?? "").Where(char.IsAsciiDigit).ToArray());
        WhatsAppLink = $"https://wa.me/{number}?text={Uri.EscapeDataString(text)}";
    }
}

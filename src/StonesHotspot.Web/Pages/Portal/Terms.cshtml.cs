using System.Globalization;
using StonesHotspot.Core;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web.Pages.Portal;

public sealed class TermsModel(SettingsService settings, AppPaths paths) : PortalPageModel(settings, paths)
{
    public IReadOnlyList<string> Paragraphs { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Custom terms from the settings (phase 5 dashboard), in the customer's language; otherwise the default text.
        var key = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Languages.English ? SettingKeys.PortalTermsEn : SettingKeys.PortalTermsFr;
        var text = await Settings.GetAsync(key, HttpContext.RequestAborted);
        if (string.IsNullOrWhiteSpace(text))
            text = Strings.Terms_Default;
        Paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

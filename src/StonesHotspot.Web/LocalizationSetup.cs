using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Web;

public static class LocalizationSetup
{
    /// <summary>
    /// Picks the UI language from <c>?lang=</c>, then the language cookie, then the browser; French otherwise.
    /// </summary>
    public static IApplicationBuilder UseHotspotLocalization(this IApplicationBuilder app)
    {
        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(Languages.Default),
            SupportedCultures = Languages.Supported.ToList(),
            SupportedUICultures = Languages.Supported.ToList(),
            FallBackToParentCultures = true,
            FallBackToParentUICultures = true,
        };
        options.RequestCultureProviders.Insert(0, new QueryStringRequestCultureProvider
        {
            QueryStringKey = "lang",
            UIQueryStringKey = "lang",
        });
        return app.UseRequestLocalization(options);
    }
}

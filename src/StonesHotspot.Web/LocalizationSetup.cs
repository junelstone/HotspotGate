using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Web;

public static class LocalizationSetup
{
    /// <summary>
    /// Picks the UI language from <c>?lang=</c>, then the language cookie, then the browser; French otherwise.
    /// A <c>?lang=</c> choice is remembered in the cookie for the next pages.
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
        app.UseRequestLocalization(options);
        return app.Use(async (context, next) =>
        {
            if (context.Request.Query.TryGetValue("lang", out var lang)
                && Languages.Supported.Any(c => c.Name == lang.ToString()))
            {
                context.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(lang.ToString())),
                    new CookieOptions { MaxAge = TimeSpan.FromDays(365), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
            }
            await next();
        });
    }
}

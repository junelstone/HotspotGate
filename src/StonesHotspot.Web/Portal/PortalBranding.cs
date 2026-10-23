using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Web.Portal;

/// <summary>Business name, accent colour and logo shown on every portal page.</summary>
public sealed partial record PortalBranding(string BusinessName, string AccentColor, bool HasLogo)
{
    public const string DefaultAccent = "#0b6e4f";
    public const string ViewDataKey = "PortalBranding";

    private static readonly string[] LogoNames = ["logo.png", "logo.jpg", "logo.jpeg", "logo.svg", "logo.webp"];

    public static async Task<PortalBranding> LoadAsync(SettingsService settings, AppPaths paths, CancellationToken ct)
    {
        var name = await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "";
        var accent = await settings.GetAsync(SettingKeys.PortalAccentColor, ct);
        return new PortalBranding(name, accent is not null && ColorRegex().IsMatch(accent) ? accent : DefaultAccent, FindLogo(paths) is not null);
    }

    /// <summary>The logo file in the branding folder, if one was uploaded.</summary>
    public static string? FindLogo(AppPaths paths) =>
        LogoNames.Select(n => Path.Combine(paths.Branding, n)).FirstOrDefault(File.Exists);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorRegex();
}

/// <summary>Base of the portal pages: loads the branding for the layout.</summary>
public abstract class PortalPageModel(SettingsService settings, AppPaths paths) : PageModel
{
    protected SettingsService Settings => settings;

    public PortalBranding Branding { get; private set; } = new("", PortalBranding.DefaultAccent, false);

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        Branding = await PortalBranding.LoadAsync(settings, paths, HttpContext.RequestAborted);
        ViewData[PortalBranding.ViewDataKey] = Branding;
        await next();
    }
}

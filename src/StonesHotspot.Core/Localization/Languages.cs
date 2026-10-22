using System.Globalization;

namespace StonesHotspot.Core.Localization;

/// <summary>Languages offered in the portal, dashboard and tray. French is the default.</summary>
public static class Languages
{
    public const string French = "fr";
    public const string English = "en";

    public static CultureInfo Default { get; } = CultureInfo.GetCultureInfo(French);

    public static IReadOnlyList<CultureInfo> Supported { get; } =
        [CultureInfo.GetCultureInfo(French), CultureInfo.GetCultureInfo(English)];

    /// <summary>Maps any tag ("en-US", "FR", null…) to a supported culture, falling back to French.</summary>
    public static CultureInfo Resolve(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
            return Default;

        var tag = languageTag.Trim();
        var dash = tag.IndexOfAny(['-', '_']);
        if (dash > 0)
            tag = tag[..dash];

        return Supported.FirstOrDefault(c => string.Equals(c.TwoLetterISOLanguageName, tag, StringComparison.OrdinalIgnoreCase))
               ?? Default;
    }
}

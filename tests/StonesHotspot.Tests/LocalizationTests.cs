using System.Collections;
using System.Globalization;
using System.Resources;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Tests;

public class LocalizationTests
{
    private static HashSet<string> Keys(CultureInfo culture)
    {
        var set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                  ?? throw new InvalidOperationException($"No resources for {culture.Name}");
        return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
    }

    [Fact]
    public void French_and_English_have_the_same_keys()
    {
        var french = Keys(CultureInfo.InvariantCulture); // neutral resources = French
        var english = Keys(CultureInfo.GetCultureInfo("en"));

        Assert.Empty(french.Except(english));
        Assert.Empty(english.Except(french));
    }

    [Fact]
    public void Default_language_is_French()
    {
        var attribute = (NeutralResourcesLanguageAttribute?)Attribute.GetCustomAttribute(
            typeof(Strings).Assembly, typeof(NeutralResourcesLanguageAttribute));
        Assert.Equal("fr", attribute?.CultureName);
        Assert.Equal("Démarrer le hotspot", Strings.ResourceManager.GetString(nameof(Strings.Tray_StartService), CultureInfo.GetCultureInfo("fr")));
        Assert.Equal("Start hotspot", Strings.ResourceManager.GetString(nameof(Strings.Tray_StartService), CultureInfo.GetCultureInfo("en-GB")));
    }

    [Theory]
    [InlineData(null, "fr")]
    [InlineData("", "fr")]
    [InlineData("en-US", "en")]
    [InlineData("EN", "en")]
    [InlineData("fr_CG", "fr")]
    [InlineData("de-DE", "fr")]
    public void Resolve_falls_back_to_French(string? tag, string expected) =>
        Assert.Equal(expected, Languages.Resolve(tag).Name);
}

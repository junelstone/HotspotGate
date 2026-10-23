using StonesHotspot.Licensing;

namespace StonesHotspot.Tests;

public class LicensingTests
{
    [Fact]
    public void Every_feature_is_in_the_edition_table()
    {
        foreach (var feature in Enum.GetValues<Feature>())
            Assert.True(EditionFeatures.MinimumEdition.ContainsKey(feature), $"{feature} missing");
    }

    [Theory]
    [InlineData(Feature.Vouchers, Edition.Basic, true)]
    [InlineData(Feature.MobileMoneyPayments, Edition.Basic, false)]
    [InlineData(Feature.MobileMoneyPayments, Edition.Pro, true)]
    [InlineData(Feature.PointOfSale, Edition.Trial, true)]
    [InlineData(Feature.CaptivePortal, Edition.None, false)]
    public void Edition_includes_feature(Feature feature, Edition edition, bool expected) =>
        Assert.Equal(expected, EditionFeatures.Includes(edition, feature));

    [Fact]
    public void Embedded_public_keys_load_and_are_marked_as_development()
    {
        var options = LicensingOptions.WithEmbeddedKeys("x");
        Assert.Equal(32, options.LicencePublicKey.Length);
        Assert.Equal(32, options.ServerPublicKey.Length);
        Assert.NotEqual(options.LicencePublicKey, options.ServerPublicKey);
        // Until the owner's keys are put in src/StonesHotspot.Licensing/Keys, this build must not be released (Phase 11 checks it).
        Assert.True(LicensingOptions.UsesDevelopmentKeys);
    }
}

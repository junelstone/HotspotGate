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
    public void Stub_reports_a_30_day_pro_trial()
    {
        var licence = new TrialStubLicenseService(TimeProvider.System);
        var status = licence.GetStatus();

        Assert.Equal(Edition.Trial, status.Edition);
        Assert.Equal(30, status.DaysLeft(DateTimeOffset.UtcNow));
        Assert.True(licence.IsAllowed(Feature.SmsLogin));
    }
}

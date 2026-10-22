namespace StonesHotspot.Licensing;

/// <summary>
/// The single table that says which edition includes which feature. To move a feature between Basic and
/// Pro, move its line here — no feature code changes. Trial always gets the Pro set.
/// </summary>
public static class EditionFeatures
{
    public static IReadOnlyDictionary<Feature, Edition> MinimumEdition { get; } = new Dictionary<Feature, Edition>
    {
        [Feature.CaptivePortal] = Edition.Basic,
        [Feature.Vouchers] = Edition.Basic,
        [Feature.UserAccounts] = Edition.Basic,
        [Feature.Tariffs] = Edition.Basic,
        [Feature.AdminDashboard] = Edition.Basic,
        [Feature.BandwidthControl] = Edition.Basic,
        [Feature.WebFilter] = Edition.Basic,
        [Feature.BasicReports] = Edition.Basic,

        [Feature.MobileMoneyPayments] = Edition.Pro,
        [Feature.SmsLogin] = Edition.Pro,
        [Feature.FreeAccessModes] = Edition.Pro,
        [Feature.Shifts] = Edition.Pro,
        [Feature.PointOfSale] = Edition.Pro,
        [Feature.Products] = Edition.Pro,
        [Feature.Receipts] = Edition.Pro,
        [Feature.FullReports] = Edition.Pro,
        [Feature.MultipleStaffAccounts] = Edition.Pro,
    };

    public static bool Includes(Edition edition, Feature feature)
    {
        if (!MinimumEdition.TryGetValue(feature, out var minimum))
            throw new InvalidOperationException($"Feature {feature} is missing from the edition table.");

        return edition switch
        {
            Edition.Pro or Edition.Trial => true,
            Edition.Basic => minimum == Edition.Basic,
            _ => false,
        };
    }
}

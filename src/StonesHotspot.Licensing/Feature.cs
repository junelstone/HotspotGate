namespace StonesHotspot.Licensing;

/// <summary>
/// Every feature that depends on the edition. Feature code never checks the edition directly; it asks
/// <see cref="ILicenseService.IsAllowed"/>. Which edition includes what is decided in <see cref="EditionFeatures"/>.
/// </summary>
public enum Feature
{
    // Basic
    CaptivePortal,
    Vouchers,
    UserAccounts,
    Tariffs,
    AdminDashboard,
    BandwidthControl,
    WebFilter,
    BasicReports,

    // Pro
    MobileMoneyPayments,
    SmsLogin,
    FreeAccessModes,
    Shifts,
    PointOfSale,
    Products,
    Receipts,
    FullReports,
    MultipleStaffAccounts,
}

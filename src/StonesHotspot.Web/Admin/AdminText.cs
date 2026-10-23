using System.Globalization;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Web.Admin;

/// <summary>Texts shared by dashboard pages and printed tickets.</summary>
public static class AdminText
{
    /// <summary>"30 min", "500 Mo", "Illimité — 24 h", with the validity when there is one.</summary>
    public static string Describe(Tariff t, CultureInfo culture)
    {
        var what = t.Type switch
        {
            TariffType.TimeBased => Units.Period(t.Quota * 60, culture),
            TariffType.DataBased => Units.Bytes(t.Quota * 1024 * 1024, culture),
            _ => Strings.Status_Unlimited,
        };
        if (t.ValidityHours is { } hours)
            what += " · " + string.Format(Strings.Tariff_ValidFor, Units.Period(hours * 3600L, culture));
        return what;
    }

    public static string Payment(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => Strings.Payment_Cash,
        PaymentMethod.MtnMoMo => "MTN MoMo",
        PaymentMethod.AirtelMoney => "Airtel Money",
        _ => Strings.Payment_Account,
    };

    public static string TransactionType(TransactionType type) => type switch
    {
        Core.Data.Entities.TransactionType.VoucherSale => Strings.Tx_VoucherSale,
        Core.Data.Entities.TransactionType.AccountDeposit => Strings.Tx_AccountDeposit,
        Core.Data.Entities.TransactionType.AccountCharge => Strings.Tx_AccountCharge,
        Core.Data.Entities.TransactionType.ProductSale => Strings.Tx_ProductSale,
        Core.Data.Entities.TransactionType.MobileMoneyPayment => Strings.Tx_MobileMoney,
        _ => Strings.Tx_Refund,
    };

    public static string Role(StaffRole role) => role == StaffRole.Admin ? Strings.Role_Admin : Strings.Role_Cashier;

    public static string Method(LoginMethod method) => method switch
    {
        LoginMethod.Voucher => Strings.Method_Voucher,
        LoginMethod.Account => Strings.Method_Account,
        LoginMethod.Free => Strings.Method_Free,
        LoginMethod.Sms => "SMS",
        LoginMethod.MobileMoney => "Mobile Money",
        _ => Strings.Gate_Access_Whitelist,
    };

    public static string Date(DateTime utc, CultureInfo culture) => utc.ToLocalTime().ToString("g", culture);

    /// <summary>Payment methods offered at the counter.</summary>
    public static readonly PaymentMethod[] CounterMethods = [PaymentMethod.Cash, PaymentMethod.MtnMoMo, PaymentMethod.AirtelMoney];
}

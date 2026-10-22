namespace StonesHotspot.Core.Data.Entities;

public enum TariffType
{
    /// <summary>Quota in minutes.</summary>
    TimeBased,

    /// <summary>Quota in MB (1 MB = 1 048 576 bytes, upload + download).</summary>
    DataBased,

    /// <summary>No quota; only the validity window and idle timeout apply.</summary>
    Unlimited,
}

public enum CountdownMode
{
    /// <summary>Time keeps running from first login, even when logged out.</summary>
    WallClock,

    /// <summary>Time runs only while at least one device is logged in.</summary>
    UsageOnly,
}

public enum VoucherStatus { New, Sold, Active, Expired, Disabled }

public enum LoginMethod { Voucher, Account, Free, Sms, MobileMoney, Whitelist }

public enum SessionEndReason { Expired, QuotaUsed, Idle, Logout, KickedByStaff }

public enum StaffRole { Admin, Cashier }

public enum TransactionType { VoucherSale, AccountDeposit, AccountCharge, ProductSale, MobileMoneyPayment, Refund }

public enum PaymentMethod { Cash, MtnMoMo, AirtelMoney, AccountBalance }

public enum WebFilterKind { Block, FreeAccess }

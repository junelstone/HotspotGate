using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Web.Portal;

/// <summary>Customer-facing messages for login results and ended sessions.</summary>
public static class PortalTexts
{
    public static string LoginError(LoginResult result, DateTime now) => result.Status switch
    {
        LoginStatus.InvalidCode => Strings.Login_InvalidCode,
        LoginStatus.InvalidCredentials => Strings.Login_InvalidCredentials,
        LoginStatus.VoucherDisabled => Strings.Login_VoucherDisabled,
        LoginStatus.VoucherExpired => Strings.Login_VoucherExpired,
        LoginStatus.QuotaUsed => Strings.Login_QuotaUsed,
        LoginStatus.TooManyDevices => Strings.Login_TooManyDevices,
        LoginStatus.LockedOut => string.Format(Strings.Login_LockedOut,
            Math.Max(1, (int)Math.Ceiling(((result.LockedUntil ?? now) - now).TotalMinutes))),
        LoginStatus.DeviceBlocked => Strings.Login_DeviceBlocked,
        LoginStatus.AccountDisabled => Strings.Login_AccountDisabled,
        LoginStatus.AccountExpired => Strings.Login_AccountExpired,
        LoginStatus.InsufficientBalance => Strings.Login_InsufficientBalance,
        _ => Strings.Login_InvalidCode,
    };

    public static string Ended(SessionEndReason reason) => reason switch
    {
        SessionEndReason.QuotaUsed => Strings.Ended_QuotaUsed,
        SessionEndReason.Expired => Strings.Ended_Expired,
        SessionEndReason.Idle => Strings.Ended_Idle,
        SessionEndReason.KickedByStaff => Strings.Ended_KickedByStaff,
        _ => Strings.Ended_Logout,
    };
}

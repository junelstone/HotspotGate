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
        LoginStatus.ModeDisabled => Strings.Login_ModeDisabled,
        LoginStatus.DailyLimitReached => Strings.Login_DailyLimit,
        LoginStatus.InvalidPassword => Strings.Login_InvalidPassword,
        LoginStatus.CodeExpired => Strings.Login_CodeExpired,
        _ => Strings.Login_InvalidCode,
    };

    public static string SmsError(SmsCodeResult result) => result.Status switch
    {
        SmsCodeStatus.InvalidNumber => Strings.Sms_InvalidNumber,
        SmsCodeStatus.PrefixNotAllowed => Strings.Sms_PrefixNotAllowed,
        SmsCodeStatus.TooSoon => string.Format(Strings.Sms_TooSoon, result.WaitSeconds ?? 60),
        SmsCodeStatus.NumberLimit or SmsCodeStatus.DeviceLimit => Strings.Sms_DailyLimit,
        SmsCodeStatus.TotalLimit or SmsCodeStatus.SendFailed => Strings.Sms_Unavailable,
        SmsCodeStatus.DailyLoginLimit => Strings.Login_DailyLimit,
        SmsCodeStatus.DeviceBlocked => Strings.Login_DeviceBlocked,
        SmsCodeStatus.LockedOut => string.Format(Strings.Login_LockedOut, 5),
        _ => Strings.Login_ModeDisabled,
    };

    public static string PayError(StonesHotspot.Core.Payments.PayStartResult result, PaymentMethod method) => result.Status switch
    {
        StonesHotspot.Core.Payments.PayStartStatus.InvalidNumber => Strings.Sms_InvalidNumber,
        StonesHotspot.Core.Payments.PayStartStatus.WrongOperator => string.Format(Strings.Pay_WrongOperator, Admin.AdminText.Payment(method)),
        StonesHotspot.Core.Payments.PayStartStatus.TooMany => Strings.Pay_TooMany,
        StonesHotspot.Core.Payments.PayStartStatus.DeviceBlocked => Strings.Login_DeviceBlocked,
        StonesHotspot.Core.Payments.PayStartStatus.PlanNotSold => Strings.Pay_PlanNotSold,
        StonesHotspot.Core.Payments.PayStartStatus.OperatorError => Strings.Pay_OperatorError,
        _ => Strings.Pay_Unavailable,
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

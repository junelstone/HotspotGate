namespace StonesHotspot.Core;

/// <summary>
/// A request broke a business rule (e.g. "shift already open"). <see cref="Code"/> is stable and is mapped to
/// a French/English message by the UI; <see cref="Exception.Message"/> is for logs.
/// </summary>
public sealed class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class ErrorCodes
{
    public const string NotFound = "NotFound";
    public const string Invalid = "Invalid";
    public const string DuplicateName = "DuplicateName";
    public const string TariffInUse = "TariffInUse";
    public const string VoucherNotSellable = "VoucherNotSellable";
    public const string ShiftAlreadyOpen = "ShiftAlreadyOpen";
    public const string ShiftClosed = "ShiftClosed";
    public const string NotAllowed = "NotAllowed";
    public const string WrongPassword = "WrongPassword";
    public const string WeakPassword = "WeakPassword";
}

namespace StonesHotspot.Core;

/// <summary>
/// A request broke a business rule (e.g. "shift already open"). <see cref="Code"/> is stable and is mapped to
/// a French/English message by the UI; <see cref="Exception.Message"/> is for logs.
/// </summary>
public sealed class BusinessRuleException(string code, string message, string? subject = null) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>What the rule is about, for the UI message (e.g. the product that is out of stock).</summary>
    public string? Subject { get; } = subject;
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
    public const string OutOfStock = "OutOfStock";
    public const string ShiftRequired = "ShiftRequired";
    public const string EmptyCart = "EmptyCart";
    public const string InUse = "InUse";
}

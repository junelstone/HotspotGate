using System.Globalization;
using System.Text;

namespace StonesHotspot.Core.Money;

/// <summary>How the thousands separator of an amount is written.</summary>
public enum XafStyle
{
    /// <summary>No-break space (U+00A0): screens and PDFs, the amount never wraps across lines.</summary>
    Display,

    /// <summary>Plain ASCII space: thermal receipt printers, CSV exports, SMS.</summary>
    Ascii,
}

/// <summary>
/// An amount in Central African CFA francs (FCFA / XAF). Whole francs only — the currency has no
/// subunit in practice. Formats as <c>1 000 FCFA</c> in every UI language.
/// </summary>
public readonly record struct Xaf(long Amount) : IComparable<Xaf>
{
    public const string Symbol = "FCFA";
    public const string IsoCode = "XAF";

    private const char NoBreakSpace = ' ';

    public static Xaf Zero => new(0);

    public bool IsNegative => Amount < 0;

    public static Xaf operator +(Xaf a, Xaf b) => new(checked(a.Amount + b.Amount));
    public static Xaf operator -(Xaf a, Xaf b) => new(checked(a.Amount - b.Amount));
    public static Xaf operator -(Xaf a) => new(checked(-a.Amount));
    public static Xaf operator *(Xaf a, int quantity) => new(checked(a.Amount * quantity));
    public static bool operator <(Xaf a, Xaf b) => a.Amount < b.Amount;
    public static bool operator >(Xaf a, Xaf b) => a.Amount > b.Amount;
    public static bool operator <=(Xaf a, Xaf b) => a.Amount <= b.Amount;
    public static bool operator >=(Xaf a, Xaf b) => a.Amount >= b.Amount;

    public int CompareTo(Xaf other) => Amount.CompareTo(other.Amount);

    public static Xaf Sum(IEnumerable<Xaf> amounts) => amounts.Aggregate(Zero, (total, a) => total + a);

    /// <summary><c>1 000 FCFA</c> with a no-break space.</summary>
    public override string ToString() => ToString(XafStyle.Display);

    public string ToString(XafStyle style) => $"{FormatNumber(Amount, style)}{Separator(style)}{Symbol}";

    /// <summary>Number only, e.g. <c>1 000</c>, for table columns that already say "FCFA" in the header.</summary>
    public string ToNumberString(XafStyle style = XafStyle.Display) => FormatNumber(Amount, style);

    private static char Separator(XafStyle style) => style == XafStyle.Ascii ? ' ' : NoBreakSpace;

    private static string FormatNumber(long amount, XafStyle style)
    {
        var digits = amount.ToString(CultureInfo.InvariantCulture).TrimStart('-');
        var sb = new StringBuilder(digits.Length + digits.Length / 3 + 1);
        if (amount < 0)
            sb.Append('-');

        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
                sb.Append(Separator(style));
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses what staff typically type: <c>1000</c>, <c>1 000</c>, <c>1.000</c>, <c>1,000</c>,
    /// <c>1 000 FCFA</c>, <c>1000 XAF</c>, <c>-500</c>. Decimals (<c>1000,50</c>) are rejected.
    /// </summary>
    public static bool TryParse(string? text, out Xaf value)
    {
        value = Zero;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        foreach (var suffix in new[] { Symbol, "F CFA", "CFA", IsoCode, "F" })
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^suffix.Length].TrimEnd();
                break;
            }
        }

        var negative = s.StartsWith('-');
        if (negative)
            s = s[1..].TrimStart();
        if (s.Length == 0)
            return false;

        // Group separators must split the digits into a 1–3 digit head followed by groups of exactly
        // three, so "1,50" or "1.5" (decimals) are refused instead of silently becoming 150 or 15.
        var groups = s.Split([' ', NoBreakSpace, ' ', '.', ',']);
        if (groups.Any(g => g.Length == 0 || !g.All(char.IsAsciiDigit)))
            return false;
        if (groups.Length > 1 && (groups[0].Length > 3 || groups.Skip(1).Any(g => g.Length != 3)))
            return false;

        if (!long.TryParse(string.Concat(groups), NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
            return false;

        value = new Xaf(negative ? -amount : amount);
        return true;
    }

    public static Xaf Parse(string text) =>
        TryParse(text, out var value) ? value : throw new FormatException($"Not a valid FCFA amount: '{text}'.");
}

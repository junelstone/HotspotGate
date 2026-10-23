namespace StonesHotspot.Core.Rules;

/// <summary>Republic of the Congo mobile numbers: +242 followed by 9 digits (06 123 45 67).</summary>
public static class PhoneNumber
{
    public const string CountryCode = "+242";
    public const int NationalLength = 9;

    /// <summary>
    /// Accepts "06 123 45 67", "061234567", "+242 06 123 45 67", "242061234567", "00242061234567" and returns
    /// "+242061234567"; null when it is not a Congo number.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var text = input.Trim().Replace("(", "").Replace(")", "").Trim();
        var plus = text.StartsWith('+');
        if (text.Any(c => !(char.IsAsciiDigit(c) || c is ' ' or '.' or '-' or '(' or ')' or '+')) || text.LastIndexOf('+') > 0)
            return null;
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());

        if (!plus && digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];
        else if (!plus && digits.Length == NationalLength)
            digits = "242" + digits;

        return digits.Length == 3 + NationalLength && digits.StartsWith("242", StringComparison.Ordinal) && digits[3] == '0'
            ? "+" + digits
            : null;
    }

    /// <summary>"+24206, 05; +24204" → ["+24206", "+24205", "+24204"]. Entries that are not number starts are dropped.</summary>
    public static IReadOnlyList<string> ParsePrefixes(string? text) =>
        (text ?? "").Split((char[])[',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.StartsWith('+') ? p : p.StartsWith("242", StringComparison.Ordinal) ? "+" + p : CountryCode + p)
            .Where(p => p.Length > 1 && p.Skip(1).All(char.IsAsciiDigit))
            .Distinct().ToList();

    /// <summary>True when there are no prefixes, or the normalized number starts with one of them.</summary>
    public static bool IsAllowed(string normalized, IReadOnlyList<string> prefixes) =>
        prefixes.Count == 0 || prefixes.Any(p => normalized.StartsWith(p, StringComparison.Ordinal));

    /// <summary>"+242 06 123 45 67" for display.</summary>
    public static string Format(string normalized) =>
        normalized.Length == 13 ? $"{normalized[..4]} {normalized[4..6]} {normalized[6..9]} {normalized[9..11]} {normalized[11..]}" : normalized;
}

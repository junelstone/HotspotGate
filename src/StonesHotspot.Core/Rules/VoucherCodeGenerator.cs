using System.Security.Cryptography;

namespace StonesHotspot.Core.Rules;

/// <summary>Random 8-character voucher codes without look-alike characters (no O, 0, I, 1, L).</summary>
public static class VoucherCodeGenerator
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 8;

    public static string Next()
    {
        Span<char> code = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(code);
    }

    /// <summary>Uppercases and removes spaces/dashes so "abcd-efgh" matches "ABCDEFGH".</summary>
    public static string Normalize(string? input) =>
        input is null ? "" : new string(input.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>"ABCDEFGH" → "ABCD-EFGH" for printing.</summary>
    public static string Format(string code) => code.Length == Length ? $"{code[..4]}-{code[4..]}" : code;
}

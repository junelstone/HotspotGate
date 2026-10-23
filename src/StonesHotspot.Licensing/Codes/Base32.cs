using System.Text;

namespace StonesHotspot.Licensing.Codes;

/// <summary>
/// Base32 with the 32 characters that cannot be confused when read aloud or typed (no 0/O, 1/I): what licence codes
/// and Machine IDs are written with.
/// </summary>
public static class Base32
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>Decodes, ignoring spaces, dashes and case; null when a character is not in the alphabet.</summary>
    public static byte[]? Decode(string text)
    {
        var bytes = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var raw in text)
        {
            if (raw is '-' or ' ' or '\r' or '\n' or '\t')
                continue;
            var value = Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (value < 0)
                return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
            buffer &= (1 << bits) - 1;
        }
        return [.. bytes];
    }

    /// <summary>"ABCDEFGHIJ" → "ABCDE-FGHIJ".</summary>
    public static string Group(string text, int size = 5) =>
        string.Join('-', Enumerable.Range(0, (text.Length + size - 1) / size).Select(i => text.Substring(i * size, Math.Min(size, text.Length - i * size))));
}

using System.Security.Cryptography;

namespace StonesHotspot.Core.Security;

public static class PasswordGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    /// <summary>Random password without look-alike characters, easy to copy from a file or screen.</summary>
    public static string Generate(int length = 14)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}

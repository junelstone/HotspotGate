using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StonesHotspot.Licensing.Codes;

namespace StonesHotspot.LicenseIssuer;

/// <summary>The owner's two key pairs: the licence key (signs licence codes) and the server key (signs server answers).</summary>
public sealed record OwnerKeys(byte[] LicencePrivate, byte[] ServerPrivate)
{
    public byte[] LicencePublic => Ed25519.PublicKeyOf(LicencePrivate);
    public byte[] ServerPublic => Ed25519.PublicKeyOf(ServerPrivate);
}

public sealed class WrongPasswordException() : Exception("Wrong password, or the key file was changed.");

/// <summary>
/// The private keys, encrypted with the owner's password: PBKDF2-SHA256 (600 000 rounds) → AES-256-GCM. The file
/// alone is useless without the password; the password alone is useless without the file. Lose both copies of the
/// file (or the password) and no new licence can ever be signed with this key.
/// </summary>
public static class KeyVault
{
    public const int MinPasswordLength = 12;
    public const int Iterations = 600_000;

    private sealed record VaultFile(int Version, string Kdf, int Iterations, string Salt, string Nonce, string Tag, string Ciphertext,
        string LicencePublic, string ServerPublic, DateTimeOffset CreatedAt);

    public static OwnerKeys Generate() => new(Ed25519.GenerateKeyPair().PrivateKey, Ed25519.GenerateKeyPair().PrivateKey);

    public static void Save(OwnerKeys keys, string password, string path, TimeProvider? clock = null)
    {
        if (password.Length < MinPasswordLength)
            throw new ArgumentException($"The password must have at least {MinPasswordLength} characters.", nameof(password));
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = new byte[64];
        keys.LicencePrivate.CopyTo(plain, 0);
        keys.ServerPrivate.CopyTo(plain, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(DeriveKey(password, salt, Iterations), tag.Length))
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes("StonesHotspot.KeyVault.v1"));
        CryptographicOperations.ZeroMemory(plain);

        var file = new VaultFile(1, "pbkdf2-sha256", Iterations, B64(salt), B64(nonce), B64(tag), B64(cipher),
            B64(keys.LicencePublic), B64(keys.ServerPublic), (clock ?? TimeProvider.System).GetUtcNow());
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    public static OwnerKeys Open(string path, string password)
    {
        var file = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(path)) ?? throw new WrongPasswordException();
        var cipher = Convert.FromBase64String(file.Ciphertext);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(DeriveKey(password, Convert.FromBase64String(file.Salt), file.Iterations), 16);
            aes.Decrypt(Convert.FromBase64String(file.Nonce), cipher, Convert.FromBase64String(file.Tag), plain, Encoding.UTF8.GetBytes("StonesHotspot.KeyVault.v1"));
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new WrongPasswordException();
        }
        var keys = new OwnerKeys(plain[..32], plain[32..64]);
        if (!keys.LicencePublic.SequenceEqual(Convert.FromBase64String(file.LicencePublic)))
            throw new WrongPasswordException();
        return keys;
    }

    /// <summary>Public keys of a vault file (no password needed): what goes into the hotspot build.</summary>
    public static (string LicencePublic, string ServerPublic) PublicKeys(string path)
    {
        var file = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(path)) ?? throw new InvalidDataException("Not a key file.");
        return (file.LicencePublic, file.ServerPublic);
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes);
}

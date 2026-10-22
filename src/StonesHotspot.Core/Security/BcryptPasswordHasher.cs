namespace StonesHotspot.Core.Security;

/// <summary>BCrypt with a per-password salt. Work factor 11 ≈ 0.1–0.2 s per check on a normal PC.</summary>
public sealed class BcryptPasswordHasher(int workFactor = BcryptPasswordHasher.DefaultWorkFactor) : IPasswordHasher
{
    public const int DefaultWorkFactor = 11;

    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, workFactor);

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return false;
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}

namespace StonesHotspot.Core.Security;

/// <summary>
/// Encrypts settings that are secrets (the SMS provider URL holds its API key). The service uses ASP.NET Data
/// Protection, whose keys are DPAPI-protected on Windows: a copied database alone does not reveal the secret.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plain);

    /// <summary>Null when the value cannot be decrypted (keys lost or value not encrypted).</summary>
    string? Unprotect(string protectedValue);
}

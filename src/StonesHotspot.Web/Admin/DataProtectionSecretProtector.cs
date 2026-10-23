using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using StonesHotspot.Core.Security;

namespace StonesHotspot.Web.Admin;

/// <summary>Secret settings encrypted with the service's Data Protection keys (DPAPI-protected on Windows).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("StonesHotspot.Settings.Secrets");

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;   // keys from another PC (restored backup): the admin types the secret again
        }
    }
}

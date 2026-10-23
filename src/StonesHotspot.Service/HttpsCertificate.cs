using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using StonesHotspot.Core;

namespace StonesHotspot.Service;

/// <summary>
/// The installation's own self-signed HTTPS certificate: created on the first start (RSA 2048, 5 years, valid for
/// the PC name, localhost and the hotspot address), then reused. Each PC gets a different key; it is never shipped.
/// The .pfx password is random and kept DPAPI-protected (machine scope) on Windows.
/// </summary>
public static class HttpsCertificate
{
    public const string FileName = "https.pfx";
    public const string PasswordFileName = "https.key";
    public const string HotspotAddress = "192.168.137.1";
    private static readonly byte[] Entropy = "StonesHotspot.Https.v1"u8.ToArray();

    /// <summary>Loads the certificate from the data folder, or creates it (again) when missing, unreadable or ending within 30 days.</summary>
    public static X509Certificate2 LoadOrCreate(AppPaths paths, TimeProvider clock, string? machineName = null)
    {
        var pfx = Path.Combine(paths.Root, FileName);
        var keyFile = Path.Combine(paths.Root, PasswordFileName);
        var now = clock.GetUtcNow();

        if (File.Exists(pfx) && File.Exists(keyFile))
        {
            try
            {
                var existing = new X509Certificate2(pfx, Unprotect(File.ReadAllBytes(keyFile)), X509KeyStorageFlags.EphemeralKeySet);
                if (existing.NotAfter.ToUniversalTime() > now.UtcDateTime.AddDays(30))
                    return existing;
                existing.Dispose();
            }
            catch (CryptographicException)
            {
                // damaged or from another PC (DPAPI): make a new one
            }
        }

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        using var created = Create(machineName ?? Environment.MachineName, now);
        File.WriteAllBytes(pfx, created.Export(X509ContentType.Pfx, password));
        File.WriteAllBytes(keyFile, Protect(password));
        return new X509Certificate2(pfx, password, X509KeyStorageFlags.EphemeralKeySet);
    }

    internal static X509Certificate2 Create(string machineName, DateTimeOffset now)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={ProductInfo.DisplayName} ({machineName})", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(machineName);
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Parse(HotspotAddress));
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false)); // server authentication
        return request.CreateSelfSigned(now.AddDays(-1), now.AddYears(5));
    }

    private static byte[] Protect(string password) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.LocalMachine)
            : Encoding.UTF8.GetBytes(password); // running from source on Linux/macOS: the data folder's permissions protect it

    private static string Unprotect(byte[] stored) =>
        OperatingSystem.IsWindows()
            ? Encoding.UTF8.GetString(ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.LocalMachine))
            : Encoding.UTF8.GetString(stored);
}

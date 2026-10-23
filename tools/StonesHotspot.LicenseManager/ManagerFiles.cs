using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StonesHotspot.LicenseManager;

/// <summary>Server address and upload token, the token encrypted for the Windows user (DPAPI).</summary>
internal sealed record ManagerSettings(string ServerUrl, string Token);

/// <summary>Where LicenseManager keeps its files: %LocalAppData%\StonesHotspot.LicenseManager.</summary>
internal static class ManagerFiles
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StonesHotspot.LicenseManager");
    public static string KeyFile => Path.Combine(Folder, "owner-keys.shkey");
    public static string Database => Path.Combine(Folder, "licences.db");
    private static string SettingsFile => Path.Combine(Folder, "settings.json");

    public static ManagerSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new("", "");
            var stored = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(SettingsFile)) ?? new("", "");
            var token = stored.Token.Length == 0 ? "" : Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(stored.Token), null, DataProtectionScope.CurrentUser));
            return stored with { Token = token };
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or FormatException)
        {
            return new("", "");
        }
    }

    public static void SaveSettings(ManagerSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var token = settings.Token.Length == 0 ? "" : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.Token), null, DataProtectionScope.CurrentUser));
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings with { Token = token }));
    }
}

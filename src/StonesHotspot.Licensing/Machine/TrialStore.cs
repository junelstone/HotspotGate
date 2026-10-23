using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using StonesHotspot.Licensing.Codes;

namespace StonesHotspot.Licensing.Machine;

/// <summary>Where the trial record is kept. Every location holds the same sealed text; missing ones are rewritten.</summary>
public interface ITrialStore
{
    IReadOnlyList<string> ReadAll();
    void WriteAll(string sealedRecord);
}

/// <summary>First run and latest time seen on this PC.</summary>
/// <param name="Registered">The trial was registered on the licence server.</param>
public sealed record TrialRecord(string MachineId, DateTimeOffset FirstRun, DateTimeOffset LatestSeen, bool Registered = false)
{
    /// <summary>
    /// JSON + HMAC keyed with the Machine ID: an edited record, or one copied from another PC, is ignored. This stops
    /// casual editing, not a determined attacker (see docs/licensing.md).
    /// </summary>
    public string Seal()
    {
        var json = JsonSerializer.Serialize(this);
        return json + "\n" + Convert.ToBase64String(Mac(json, MachineId));
    }

    public static TrialRecord? Unseal(string text)
    {
        var parts = text.Split('\n');
        if (parts.Length != 2)
            return null;
        try
        {
            var record = JsonSerializer.Deserialize<TrialRecord>(parts[0]);
            if (record is null || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]), Mac(parts[0], record.MachineId)))
                return null;
            return record;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }

    private static byte[] Mac(string json, string machineId) =>
        HMACSHA256.HashData(SHA256.HashData(Encoding.UTF8.GetBytes("StonesHotspot-trial-v1|" + machineId)), Encoding.UTF8.GetBytes(json));
}

/// <summary>Tests and non-Windows development: files in one folder.</summary>
public sealed class FileTrialStore(params string[] files) : ITrialStore
{
    public IReadOnlyList<string> ReadAll() => files.Where(File.Exists).Select(f => Try(() => File.ReadAllText(f))).OfType<string>().ToList();

    public void WriteAll(string sealedRecord)
    {
        foreach (var file in files)
        {
            Try(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, sealedRecord);
                return "";
            });
        }
    }

    private static string? Try(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Windows: a DPAPI-protected (machine scope) file in <c>%ProgramData%\StonesHotspot.Licence</c> — outside the data
/// folder, so deleting the data or uninstalling keeps it — and a registry value under
/// <c>HKLM\SOFTWARE\StonesHotspot\Licence</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTrialStore : ITrialStore
{
    private const string RegistryKey = @"SOFTWARE\StonesHotspot\Licence";
    private const string RegistryValue = "State";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StonesHotspot.Trial");

    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StonesHotspot.Licence", "trial.dat");

    public IReadOnlyList<string> ReadAll()
    {
        var found = new List<string>();
        try
        {
            if (File.Exists(FilePath))
                found.Add(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.LocalMachine)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
        }
        try
        {
            if (Registry.LocalMachine.OpenSubKey(RegistryKey)?.GetValue(RegistryValue) is byte[] value)
                found.Add(Encoding.UTF8.GetString(ProtectedData.Unprotect(value, Entropy, DataProtectionScope.LocalMachine)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or System.Security.SecurityException)
        {
        }
        return found;
    }

    public void WriteAll(string sealedRecord)
    {
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(sealedRecord), Entropy, DataProtectionScope.LocalMachine);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllBytes(FilePath, protectedBytes);
            File.SetAttributes(FilePath, FileAttributes.Hidden | FileAttributes.System);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistryKey);
            key.SetValue(RegistryValue, protectedBytes, RegistryValueKind.Binary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}

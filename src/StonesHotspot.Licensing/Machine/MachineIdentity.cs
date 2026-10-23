using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;
using StonesHotspot.Licensing.Codes;

namespace StonesHotspot.Licensing.Machine;

/// <summary>Reads the hardware identifiers the Machine ID is made from.</summary>
public interface IMachineIdentitySource
{
    MachineId Read();
}

/// <summary>A fixed Machine ID (tests, and the licence server never needs the real one).</summary>
public sealed class FixedMachineIdentity(MachineId id) : IMachineIdentitySource
{
    public MachineId Read() => id;
}

public static class MachineIdentity
{
    /// <summary>The source for this operating system.</summary>
    public static IMachineIdentitySource ForThisComputer() =>
        OperatingSystem.IsWindows() ? new WindowsMachineIdentity() : new LinuxMachineIdentity();
}

/// <summary>
/// Windows: MachineGuid (registry), motherboard serial (or BIOS serial), serial of the disk Windows boots from, CPU id
/// (WMI). Read once at start; a part that cannot be read counts as missing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMachineIdentity : IMachineIdentitySource
{
    public MachineId Read() => MachineId.FromRaw(
        Safe(() => Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")?.GetValue("MachineGuid") as string),
        Safe(() => Wmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber")) is { Length: > 3 } board && !board.Contains("O.E.M", StringComparison.OrdinalIgnoreCase)
            ? board
            : Safe(() => Wmi("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber")),
        Safe(BootDiskSerial),
        Safe(() => Wmi("SELECT ProcessorId FROM Win32_Processor", "ProcessorId")));

    private static string? BootDiskSerial()
    {
        var index = Wmi("SELECT DiskIndex FROM Win32_DiskPartition WHERE BootPartition = TRUE", "DiskIndex") ?? "0";
        return Wmi($"SELECT SerialNumber FROM Win32_DiskDrive WHERE Index = {int.Parse(index, System.Globalization.CultureInfo.InvariantCulture)}", "SerialNumber");
    }

    private static string? Wmi(string query, string property)
    {
        using var searcher = new ManagementObjectSearcher(query);
        foreach (var item in searcher.Get())
        {
            using (item)
            {
                if (item[property]?.ToString()?.Trim() is { Length: > 0 } value)
                    return value;
            }
        }
        return null;
    }

    private static string? Safe(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or FormatException or IOException)
        {
            return null;
        }
    }
}

/// <summary>Linux (development and the licence server): machine-id, DMI board serial and product UUID, CPU model.</summary>
public sealed class LinuxMachineIdentity : IMachineIdentitySource
{
    public MachineId Read() => MachineId.FromRaw(
        File("/etc/machine-id"),
        File("/sys/class/dmi/id/board_serial") ?? File("/sys/class/dmi/id/product_serial"),
        File("/sys/class/dmi/id/product_uuid"),
        File("/proc/cpuinfo")?.Split('\n').FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal)));

    private static string? File(string path)
    {
        try
        {
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

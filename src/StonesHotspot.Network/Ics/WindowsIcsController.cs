using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StonesHotspot.Network.Ics;

/// <summary>
/// Controls Windows Internet Connection Sharing through the <c>HNetCfg.HNetShare</c> COM object (the same API
/// as the "Sharing" tab of an adapter). Windows gives the private (LAN) adapter 192.168.137.1/24 and runs its
/// own DHCP server and DNS proxy there. Needs administrator rights.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsIcsController : IIcsController
{
    private const int SharingPublic = 0;   // ICSSHARINGTYPE_PUBLIC
    private const int SharingPrivate = 1;  // ICSSHARINGTYPE_PRIVATE

    public IcsStatus GetStatus()
    {
        var shares = new List<IcsShare>();
        Run(manager =>
        {
            foreach (var (id, name, config) in Connections(manager))
            {
                if ((bool)config.SharingEnabled)
                    shares.Add(new IcsShare(id, name, (int)config.SharingConnectionType == SharingPublic ? IcsRole.Public : IcsRole.Private));
            }
        });
        return new IcsStatus(true, shares);
    }

    public void Enable(string wanId, string lanId)
    {
        EnsureIcsSurvivesReboot();
        Run(manager =>
        {
            var connections = Connections(manager).ToList();
            var wan = connections.FirstOrDefault(c => SameId(c.Id, wanId));
            var lan = connections.FirstOrDefault(c => SameId(c.Id, lanId));
            if (wan.Config is null)
                throw new IcsException($"Windows does not list the internet adapter {wanId} as a network connection.");
            if (lan.Config is null)
                throw new IcsException($"Windows does not list the hotspot adapter {lanId} as a network connection.");

            // ICS allows a single public/private pair: switch off sharing everywhere else first.
            foreach (var (id, _, config) in connections)
            {
                if ((bool)config.SharingEnabled && !SameId(id, wanId) && !SameId(id, lanId))
                    config.DisableSharing();
            }

            if (!(bool)wan.Config.SharingEnabled || (int)wan.Config.SharingConnectionType != SharingPublic)
            {
                if ((bool)wan.Config.SharingEnabled)
                    wan.Config.DisableSharing();
                wan.Config.EnableSharing(SharingPublic);
            }
            if (!(bool)lan.Config.SharingEnabled || (int)lan.Config.SharingConnectionType != SharingPrivate)
            {
                if ((bool)lan.Config.SharingEnabled)
                    lan.Config.DisableSharing();
                lan.Config.EnableSharing(SharingPrivate);
            }
        });
    }

    public void Disable() => Run(manager =>
    {
        foreach (var (_, _, config) in Connections(manager))
        {
            if ((bool)config.SharingEnabled)
                config.DisableSharing();
        }
    });

    private static IEnumerable<(string Id, string Name, dynamic Config)> Connections(object sharingManager)
    {
        dynamic manager = sharingManager;
        foreach (var connection in manager.EnumEveryConnection)
        {
            var props = manager.NetConnectionProps[connection];
            yield return ((string)props.Guid, (string)props.Name, manager.INetSharingConfigurationForINetConnection[connection]);
        }
    }

    private static void Run(Action<object> action)
    {
        var type = Type.GetTypeFromProgID("HNetCfg.HNetShare")
                   ?? throw new IcsException("Internet Connection Sharing is not available on this Windows edition.");
        dynamic? manager = null;
        try
        {
            manager = Activator.CreateInstance(type)!;
            if (!(bool)manager.SharingInstalled)
                throw new IcsException("Internet Connection Sharing is not installed on this PC.");
            action((object)manager);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IcsException("Administrator rights are required to change Internet Connection Sharing.", ex);
        }
        catch (COMException ex)
        {
            throw new IcsException($"Windows refused the Internet Connection Sharing change (0x{ex.HResult:X8}): {ex.Message}", ex);
        }
        finally
        {
            if (manager is not null)
                Marshal.FinalReleaseComObject(manager);
        }
    }

    /// <summary>
    /// Without these, Windows may drop ICS after a reboot: keep shared connections across reboots and start the
    /// ICS service (SharedAccess) automatically.
    /// </summary>
    private static void EnsureIcsSurvivesReboot()
    {
        try
        {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedAccess"))
                key.SetValue("EnableRebootPersistConnection", 1, RegistryValueKind.DWord);
            using (var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess", writable: true))
                service?.SetValue("Start", 2, RegistryValueKind.DWord); // 2 = automatic
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IcsException("Administrator rights are required to configure Internet Connection Sharing.", ex);
        }
    }

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

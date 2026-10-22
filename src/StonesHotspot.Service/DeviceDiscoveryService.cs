using StonesHotspot.Core.Data;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Service;

/// <summary>
/// Every few seconds, reads the neighbour (ARP) table of the LAN adapter, publishes the connected devices for
/// the dashboard and records them in the devices table.
/// </summary>
internal sealed class DeviceDiscoveryService(
    IServiceScopeFactory scopes,
    INetworkAdapterProvider adapters,
    INeighborTable neighbors,
    ConnectedDeviceRegistry registry,
    TimeProvider clock,
    ILogger<DeviceDiscoveryService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Device scan failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task ScanOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var config = await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().GetConfigurationAsync(ct);
        var lan = adapters.GetAdapters().FirstOrDefault(a => string.Equals(a.Id, config.LanAdapterId, StringComparison.OrdinalIgnoreCase));
        if (lan is null)
        {
            registry.Update(DeviceScanResult.Empty);
            return;
        }

        var result = ConnectedDeviceScanner.Scan(neighbors.GetIpv4Entries(), lan, IcsHostsFile.Read(), clock.GetUtcNow().UtcDateTime);
        registry.Update(result);

        var devices = scope.ServiceProvider.GetRequiredService<DeviceService>();
        foreach (var device in result.Devices)
            await devices.TouchAsync(device.Mac, device.Ip.ToString(), device.Hostname, ct);
        await scope.ServiceProvider.GetRequiredService<HotspotDbContext>().SaveChangesAsync(ct);
    }
}

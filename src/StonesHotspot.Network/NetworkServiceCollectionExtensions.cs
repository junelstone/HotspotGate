using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Bandwidth;
using StonesHotspot.Network.Dns;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Gate.WinDivert;
using StonesHotspot.Network.Ics;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Printing;
using StonesHotspot.Core.Printing;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Network;

public static class NetworkServiceCollectionExtensions
{
    /// <summary>Registers the Windows network implementations (harmless stand-ins on other systems).</summary>
    public static IServiceCollection AddHotspotNetwork(
        this IServiceCollection services, HotspotNetworkOptions? options = null, SpeedTestOptions? speedTest = null)
    {
        services.AddSingleton(options ?? new HotspotNetworkOptions());
        services.AddSingleton(speedTest ?? new SpeedTestOptions());
        services.AddSingleton(sp => new WanSpeedTest(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, sp.GetRequiredService<SpeedTestOptions>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<WanSpeedTest>>()));
        services.AddSingleton<INetworkAdapterProvider, SystemNetworkAdapterProvider>();
        services.AddSingleton<IPrivilegeChecker, ProcessPrivilegeChecker>();
        services.AddSingleton<ConnectedDeviceRegistry>();
        services.AddSingleton<PacketProcessor>();
        services.AddSingleton<PortalLocation>();
        services.AddSingleton<LiveTrafficMonitor>();
        services.AddSingleton<AccessRefreshSignal>();
        services.AddSingleton<IPrinterTransportFactory, PrinterTransportFactory>();
        services.AddSingleton<ClientIdentityResolver>();
        services.AddSingleton<DnsResponder>();
        services.AddSingleton<IDnsUpstream>(sp => new UdpDnsUpstream(() => WanDnsServers(sp), sp.GetRequiredService<ILogger<UdpDnsUpstream>>()));

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IIcsController, WindowsIcsController>();
            services.AddSingleton<INeighborTable, WindowsNeighborTable>();
            services.AddSingleton<IPacketGate, WinDivertPacketGate>();
        }
        else
        {
            services.AddSingleton<IIcsController, UnsupportedIcsController>();
            services.AddSingleton<INeighborTable, EmptyNeighborTable>();
            services.AddSingleton<IPacketGate, UnsupportedPacketGate>();
        }

        services.AddScoped<NetworkSetupService>();
        return services;
    }

    /// <summary>DNS servers of the adapters that reach the internet (those with a default gateway).</summary>
    private static IReadOnlyList<System.Net.IPAddress> WanDnsServers(IServiceProvider services) =>
        services.GetRequiredService<INetworkAdapterProvider>().GetAdapters()
            .Where(a => a.IsUp && a.HasDefaultGateway)
            .SelectMany(a => a.DnsServers)
            .ToList();
}

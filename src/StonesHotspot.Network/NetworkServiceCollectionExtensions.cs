using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Ics;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Network;

public static class NetworkServiceCollectionExtensions
{
    /// <summary>Registers the Windows network implementations (harmless stand-ins on other systems).</summary>
    public static IServiceCollection AddHotspotNetwork(this IServiceCollection services)
    {
        services.AddSingleton<INetworkAdapterProvider, SystemNetworkAdapterProvider>();
        services.AddSingleton<IPrivilegeChecker, ProcessPrivilegeChecker>();
        services.AddSingleton<ConnectedDeviceRegistry>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IIcsController, WindowsIcsController>();
            services.AddSingleton<INeighborTable, WindowsNeighborTable>();
        }
        else
        {
            services.AddSingleton<IIcsController, UnsupportedIcsController>();
            services.AddSingleton<INeighborTable, EmptyNeighborTable>();
        }

        services.AddScoped<NetworkSetupService>();
        return services;
    }
}

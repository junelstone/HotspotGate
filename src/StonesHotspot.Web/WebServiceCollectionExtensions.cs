using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web;

public static class WebServiceCollectionExtensions
{
    /// <summary>Services used by the portal and dashboard pages.</summary>
    public static IServiceCollection AddHotspotWeb(this IServiceCollection services)
    {
        services.AddRazorPages();
        services.AddSingleton<PortalClient>();
        return services;
    }
}

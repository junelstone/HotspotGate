using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Web.Auth;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web;

public static class WebServiceCollectionExtensions
{
    /// <summary>Services used by the portal and dashboard pages.</summary>
    public static IServiceCollection AddHotspotWeb(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddStaffAuthentication();
        services.AddRazorPages(options =>
        {
            // The whole dashboard needs a signed-in staff member; admin-only pages add [Authorize(Policy = Admin)].
            options.Conventions.AuthorizeFolder("/Admin", StaffPolicies.Staff);
            options.Conventions.AllowAnonymousToPage("/Admin/Login");
            options.Conventions.AllowAnonymousToPage("/Admin/Denied");
            options.Conventions.ConfigureFilter(new ForcePasswordChangeFilter());
        });
        services.AddSingleton<PortalClient>();
        return services;
    }
}

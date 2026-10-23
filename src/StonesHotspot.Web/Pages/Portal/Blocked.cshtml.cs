using StonesHotspot.Core;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web.Pages.Portal;

public sealed class BlockedModel(SettingsService settings, AppPaths paths) : PortalPageModel(settings, paths)
{
    public string Host { get; private set; } = "";

    public void OnGet()
    {
        Host = HttpContext.Items[CaptivePortalMiddleware.BlockedHostItem] as string ?? "";
        Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden;
    }
}

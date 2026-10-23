using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StonesHotspot.Web.Pages.Portal;

public sealed class BlockedModel : PageModel
{
    public string Host { get; private set; } = "";

    public void OnGet()
    {
        Host = HttpContext.Items[CaptivePortalMiddleware.BlockedHostItem] as string ?? "";
        Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden;
    }
}

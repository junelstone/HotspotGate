using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Licensing;

namespace StonesHotspot.Web.Pages.Admin;

public sealed class IndexModel(ILicenseService licence, TimeProvider clock) : PageModel
{
    public int DaysLeft { get; private set; }

    public void OnGet() => DaysLeft = licence.GetStatus().DaysLeft(clock.GetUtcNow()) ?? 0;
}

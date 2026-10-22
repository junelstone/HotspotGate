using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Web.Pages.Admin;

[LocalOnly]
public sealed class DevicesModel(ConnectedDeviceRegistry registry, NetworkSetupService setup) : PageModel
{
    public DeviceScanResult Scan { get; private set; } = DeviceScanResult.Empty;
    public bool LanConfigured { get; private set; }

    public async Task OnGetAsync()
    {
        Scan = registry.Latest;
        LanConfigured = (await setup.GetConfigurationAsync(HttpContext.RequestAborted)).LanAdapterId is not null;
    }
}

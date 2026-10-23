using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;

namespace StonesHotspot.Web.Pages.Portal;

/// <summary>The page customers land on. Phase 3: welcome and status; the login forms come in phase 4.</summary>
public sealed class IndexModel(SettingsService settings, ClientIdentityResolver identities, IPacketGate gate) : PageModel
{
    public string BusinessName { get; private set; } = "";
    public string? Mac { get; private set; }
    public string? Ip { get; private set; }
    public bool IsAuthorized { get; private set; }

    /// <summary>The page the customer wanted; only http(s) URLs are kept (never a script: link).</summary>
    [FromQuery(Name = "url")]
    public string? OriginalUrl { get; set; }

    public async Task OnGetAsync()
    {
        BusinessName = await settings.GetAsync(SettingKeys.BusinessName, HttpContext.RequestAborted) ?? "";
        var remote = HttpContext.Connection.RemoteIpAddress;
        Ip = (remote?.IsIPv4MappedToIPv6 == true ? remote.MapToIPv4() : remote)?.ToString();
        Mac = identities.ResolveMac(remote);
        IsAuthorized = remote is not null && gate.Processor.Snapshot?.Authorized.ContainsKey(PacketProcessor.ToUInt32(remote.MapToIPv4())) == true;
        if (!Uri.TryCreate(OriginalUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            OriginalUrl = null;
    }
}

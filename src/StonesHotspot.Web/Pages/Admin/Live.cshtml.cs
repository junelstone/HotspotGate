using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Customers online now, with what is left and their speed; add time, log out, block, whitelist.</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class LiveModel(
    SessionService sessions, DeviceService devices, LiveTrafficMonitor speeds, AccessRefreshSignal refresh, CurrentStaff current) : PageModel
{
    public IReadOnlyList<SessionStatus> Sessions { get; private set; } = [];

    public (double Down, double Up) Speed(long sessionId) => speeds.For(sessionId);

    public async Task OnGetAsync() =>
        Sessions = await sessions.GetActiveStatusesAsync(HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostAddTimeAsync(long sessionId, int minutes)
    {
        try
        {
            var added = await sessions.AddTimeAsync(sessionId, minutes, await current.GetAsync(), HttpContext.RequestAborted);
            if (added) TempData.Success(string.Format(Strings.Live_TimeAdded, minutes));
            else TempData.Error(Strings.Live_NoTimeToAdd);
        }
        catch (BusinessRuleException)
        {
            TempData.Error(Strings.Live_NoTimeToAdd);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostKickAsync(long sessionId)
    {
        await sessions.EndAsync(sessionId, SessionEndReason.KickedByStaff, await current.GetAsync(), HttpContext.RequestAborted);
        refresh.Request();
        TempData.Success(Strings.Live_Kicked);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBlockAsync(string mac)
    {
        await devices.SetBlockedAsync(mac, true, await current.GetAsync(), HttpContext.RequestAborted);
        refresh.Request();
        TempData.Success(string.Format(Strings.Live_Blocked, mac));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWhitelistAsync(string mac)
    {
        await devices.SetWhitelistedAsync(mac, true, await current.GetAsync(), HttpContext.RequestAborted);
        refresh.Request();
        TempData.Success(string.Format(Strings.Live_Whitelisted, mac));
        return RedirectToPage();
    }
}

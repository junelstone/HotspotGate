using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Web.Pages.Admin;

public enum DeviceAccess { None, Session, Whitelist, Blocked }

public sealed record GateDeviceRow(ConnectedDevice Device, DeviceAccess Access);

/// <summary>Phase 3 test page: gate status and counters, authorise/revoke a device, web-filter rules.</summary>
[LocalOnly]
public sealed class GateModel(
    IPacketGate gate,
    ConnectedDeviceRegistry registry,
    HotspotDbContext db,
    DeviceService devices,
    SessionService sessions,
    WebFilterService filters) : PageModel
{
    public GateStatus Status { get; private set; } = GateStatus.Stopped;
    public GateCounters Counters { get; private set; } = new();
    public PacketProcessor Processor => gate.Processor;
    public IReadOnlyList<GateDeviceRow> Devices { get; private set; } = [];
    public IReadOnlyList<WebFilterRule> Rules { get; private set; } = [];
    public string? Error { get; private set; }

    [BindProperty]
    public string? Pattern { get; set; }

    [BindProperty]
    public WebFilterKind Kind { get; set; } = WebFilterKind.Block;

    public Task OnGetAsync() => LoadAsync();

    public async Task<IActionResult> OnPostAuthorizeAsync(string mac)
    {
        await devices.SetWhitelistedAsync(mac, true, null, HttpContext.RequestAborted);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string mac)
    {
        await devices.SetWhitelistedAsync(mac, false, null, HttpContext.RequestAborted);
        if (await sessions.GetActiveSessionForDeviceAsync(mac, HttpContext.RequestAborted) is { } session)
            await sessions.EndAsync(session.Id, SessionEndReason.KickedByStaff, null, HttpContext.RequestAborted);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddRuleAsync()
    {
        try
        {
            await filters.AddAsync(Pattern ?? "", Kind, null, null, HttpContext.RequestAborted);
            return RedirectToPage();
        }
        catch (BusinessRuleException ex)
        {
            Error = ex.Code == ErrorCodes.DuplicateName ? Strings.Gate_Error_DuplicateRule : Strings.Gate_Error_InvalidRule;
            await LoadAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveRuleAsync(int id)
    {
        await filters.RemoveAsync(id, null, HttpContext.RequestAborted);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Status = gate.Status;
        Counters = gate.Processor.Counters;
        Rules = await filters.ListAsync(HttpContext.RequestAborted);

        var seen = registry.Latest.Devices;
        var macs = seen.Select(d => d.Mac).ToList();
        var flags = await db.Devices.AsNoTracking().Where(d => macs.Contains(d.Mac))
            .ToDictionaryAsync(d => d.Mac, d => (d.Whitelisted, d.Blocked), HttpContext.RequestAborted);
        var withSession = (await db.Sessions.AsNoTracking().Where(s => s.EndedAt == null).Select(s => s.Device.Mac)
            .ToListAsync(HttpContext.RequestAborted)).ToHashSet();

        Devices = seen.Select(d =>
        {
            var (whitelisted, blocked) = flags.GetValueOrDefault(d.Mac);
            var access = blocked ? DeviceAccess.Blocked
                : withSession.Contains(d.Mac) ? DeviceAccess.Session
                : whitelisted ? DeviceAccess.Whitelist
                : DeviceAccess.None;
            return new GateDeviceRow(d, access);
        }).ToList();
    }

    public static string AccessText(DeviceAccess access) => access switch
    {
        DeviceAccess.Session => Strings.Gate_Access_Session,
        DeviceAccess.Whitelist => Strings.Gate_Access_Whitelist,
        DeviceAccess.Blocked => Strings.Gate_Access_Blocked,
        _ => Strings.Gate_Access_None,
    };
}

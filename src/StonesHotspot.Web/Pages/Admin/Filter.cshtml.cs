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

/// <summary>Blocked sites (with ready-made lists), family DNS, free-access sites, MAC white and black lists.</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class FilterModel(
    WebFilterService filters, DeviceService devices, SettingsService settings, AccessRefreshSignal refresh, CurrentStaff current) : PageModel
{
    public IReadOnlyList<WebFilterRule> Blocked { get; private set; } = [];
    public IReadOnlyList<WebFilterRule> Free { get; private set; } = [];
    public IReadOnlyList<Device> Whitelist { get; private set; } = [];
    public IReadOnlyList<Device> Blacklist { get; private set; } = [];
    public bool FamilyDns { get; private set; }

    public int CountInList(string list) => Blocked.Count(r => r.Category == list);

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var rules = await filters.ListAsync(ct);
        Blocked = rules.Where(r => r.Kind == WebFilterKind.Block).ToList();
        Free = rules.Where(r => r.Kind == WebFilterKind.FreeAccess).ToList();
        Whitelist = await devices.ListWhitelistedAsync(ct);
        Blacklist = await devices.ListBlockedAsync(ct);
        FamilyDns = await settings.GetBoolAsync(SettingKeys.FilterFamilyDns, ct);
    }

    public Task<IActionResult> OnPostAddRuleAsync(string? pattern, WebFilterKind kind) => ChangeAsync(async me =>
    {
        await filters.AddAsync(pattern ?? "", kind, null, me, HttpContext.RequestAborted);
        return Strings.Common_Saved;
    });

    public Task<IActionResult> OnPostRemoveRuleAsync(int id) => ChangeAsync(async me =>
    {
        await filters.RemoveAsync(id, me, HttpContext.RequestAborted);
        return Strings.Common_Saved;
    });

    public Task<IActionResult> OnPostAddListAsync(string list) => ChangeAsync(async me =>
        string.Format(Strings.Filter_ListAdded, await filters.AddListAsync(list, me, HttpContext.RequestAborted)));

    public Task<IActionResult> OnPostRemoveListAsync(string list) => ChangeAsync(async me =>
        string.Format(Strings.Filter_ListRemoved, await filters.RemoveListAsync(list, me, HttpContext.RequestAborted)));

    public Task<IActionResult> OnPostFamilyDnsAsync(bool enabled) => ChangeAsync(async me =>
    {
        await settings.SetAsync(SettingKeys.FilterFamilyDns, enabled ? "true" : "false", me, HttpContext.RequestAborted);
        return Strings.Common_Saved;
    });

    public Task<IActionResult> OnPostWhitelistAsync(string? mac, string? note, bool add) => !ValidMac(mac) ? InvalidMac() : ChangeAsync(async me =>
    {
        await devices.SetWhitelistedAsync(mac ?? "", add, me, HttpContext.RequestAborted, add ? note ?? "" : null);
        return Strings.Common_Saved;
    });

    public Task<IActionResult> OnPostBlacklistAsync(string? mac, string? note, bool add) => !ValidMac(mac) ? InvalidMac() : ChangeAsync(async me =>
    {
        await devices.SetBlockedAsync(mac ?? "", add, me, HttpContext.RequestAborted, add ? note ?? "" : null);
        return Strings.Common_Saved;
    });

    private static bool ValidMac(string? mac) => StonesHotspot.Core.Rules.MacAddress.TryNormalize(mac, out _);

    private Task<IActionResult> InvalidMac()
    {
        TempData.Error(Strings.Filter_InvalidMac);
        return Task.FromResult<IActionResult>(RedirectToPage());
    }

    /// <summary>Runs a change, applies it to the gate at once, and reports the outcome after the redirect.</summary>
    private async Task<IActionResult> ChangeAsync(Func<Staff, Task<string>> change)
    {
        try
        {
            TempData.Success(await change(await current.GetAsync(HttpContext.RequestAborted)));
            refresh.RequestRulesReload();
        }
        catch (BusinessRuleException ex)
        {
            TempData.Error(ex.Code switch
            {
                ErrorCodes.DuplicateName => Strings.Gate_Error_DuplicateRule,
                _ => Strings.Gate_Error_InvalidRule,
            });
        }
        return RedirectToPage();
    }
}

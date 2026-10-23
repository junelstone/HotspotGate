using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Bandwidth;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Plan speed limits overview, fair share and the WAN speed test.</summary>
[Authorize(Policy = StaffPolicies.Admin)]
public sealed class BandwidthModel(
    SettingsService settings, TariffService tariffs, WanSpeedTest speedTest, IPacketGate gate, AccessRefreshSignal refresh, CurrentStaff current) : PageModel
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(10);

    public IReadOnlyList<Tariff> Tariffs { get; private set; } = [];
    public SpeedTestResult? LastTest => speedTest.Last;
    public int ActiveDevices { get; private set; }
    public int? CurrentShareDown { get; private set; }
    public string? Error { get; private set; }

    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public int TotalDownKbps { get; set; }
    [BindProperty] public int TotalUpKbps { get; set; }
    [BindProperty] public int MinKbps { get; set; } = 256;
    [BindProperty] public int MaxKbps { get; set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Enabled = await settings.GetBoolAsync(SettingKeys.FairShareEnabled, ct);
        TotalDownKbps = await settings.GetIntAsync(SettingKeys.FairShareTotalDownKbps, 0, ct);
        TotalUpKbps = await settings.GetIntAsync(SettingKeys.FairShareTotalUpKbps, 0, ct);
        MinKbps = await settings.GetIntAsync(SettingKeys.FairShareMinKbps, 256, ct);
        MaxKbps = await settings.GetIntAsync(SettingKeys.FairShareMaxKbps, 0, ct);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (TotalDownKbps < 0 || TotalUpKbps < 0 || MinKbps < 1 || MaxKbps < 0 || (MaxKbps > 0 && MaxKbps < MinKbps)
            || (Enabled && TotalDownKbps == 0 && TotalUpKbps == 0))
        {
            Error = Strings.Bandwidth_Invalid;
            await LoadAsync();
            return Page();
        }

        var ct = HttpContext.RequestAborted;
        var me = await current.GetAsync(ct);
        await settings.SetAsync(SettingKeys.FairShareEnabled, Enabled ? "true" : "false", me, ct);
        await settings.SetAsync(SettingKeys.FairShareTotalDownKbps, TotalDownKbps.ToString(System.Globalization.CultureInfo.InvariantCulture), me, ct);
        await settings.SetAsync(SettingKeys.FairShareTotalUpKbps, TotalUpKbps.ToString(System.Globalization.CultureInfo.InvariantCulture), me, ct);
        await settings.SetAsync(SettingKeys.FairShareMinKbps, MinKbps.ToString(System.Globalization.CultureInfo.InvariantCulture), me, ct);
        await settings.SetAsync(SettingKeys.FairShareMaxKbps, MaxKbps.ToString(System.Globalization.CultureInfo.InvariantCulture), me, ct);
        refresh.RequestRulesReload();
        TempData.Success(Strings.Common_Saved);
        return RedirectToPage();
    }

    /// <summary>Runs the WAN speed test (about 15 seconds) and shows the result.</summary>
    public async Task<IActionResult> OnPostTestAsync()
    {
        var result = await speedTest.RunAsync(HttpContext.RequestAborted);
        if (result.Error is not null)
            TempData.Error(string.Format(Strings.Bandwidth_TestFailed, result.Error));
        return RedirectToPage();
    }

    /// <summary>Uses 90 % of the measured speeds as fair-share totals (keeps headroom for the PC itself).</summary>
    public async Task<IActionResult> OnPostUseTestAsync()
    {
        if (speedTest.Last is { DownKbps: { } down, UpKbps: { } up })
        {
            var ct = HttpContext.RequestAborted;
            var me = await current.GetAsync(ct);
            await settings.SetAsync(SettingKeys.FairShareTotalDownKbps, (down * 9 / 10).ToString(System.Globalization.CultureInfo.InvariantCulture), me, ct);
            await settings.SetAsync(SettingKeys.FairShareTotalUpKbps, (up * 9 / 10).ToString(System.Globalization.CultureInfo.InvariantCulture), me, ct);
            refresh.RequestRulesReload();
            TempData.Success(Strings.Common_Saved);
        }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Tariffs = await tariffs.ListAsync(ct: HttpContext.RequestAborted);
        ActiveDevices = gate.Processor.RecentlyActive(ActiveWindow).Count;
        CurrentShareDown = Enabled
            ? BandwidthPolicy.Share(TotalDownKbps, ActiveDevices, new FairShare(true, TotalDownKbps, TotalUpKbps, MinKbps, MaxKbps))
            : null;
    }
}

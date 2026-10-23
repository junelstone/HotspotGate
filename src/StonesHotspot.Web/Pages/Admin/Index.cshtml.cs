using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Network.Setup;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

public sealed class IndexModel(
    CurrentStaff current,
    CashService cash,
    SessionService sessions,
    FreeAccessService free,
    ConnectedDeviceRegistry devices,
    ILicenseService licence,
    TimeProvider clock) : PageModel
{
    public Staff Me { get; private set; } = null!;
    public Xaf SalesToday { get; private set; }
    public int SalesCountToday { get; private set; }
    public int ActiveSessions { get; private set; }
    public int ConnectedDevices { get; private set; }
    public Shift? OpenShift { get; private set; }
    public bool ShiftsEnabled { get; private set; }
    public int? TrialDaysLeft { get; private set; }

    /// <summary>Today's shared Wi-Fi password, for staff to tell customers; null when that mode is off.</summary>
    public string? PasswordOfTheDay { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Me = await current.GetAsync(ct);
        var startOfDay = new DateTimeOffset(clock.GetLocalNow().Date, clock.GetLocalNow().Offset).UtcDateTime;
        (SalesToday, SalesCountToday) = await cash.SalesSinceAsync(startOfDay, Me.Role == StaffRole.Admin ? null : Me.Id, ct);
        ActiveSessions = (await sessions.GetActiveSessionsAsync(ct)).Count;
        ConnectedDevices = devices.Latest.Devices.Count;
        ShiftsEnabled = licence.IsAllowed(Feature.Shifts);
        OpenShift = ShiftsEnabled ? await cash.GetOpenShiftAsync(Me.Id, ct) : null;
        var status = licence.GetStatus();
        TrialDaysLeft = status.Edition == Edition.Trial ? status.DaysLeft(clock.GetUtcNow()) : null;
        if (licence.IsAllowed(Feature.FreeAccessModes) && (await free.GetSettingsAsync(ct)).Password.Usable)
            PasswordOfTheDay = await free.PasswordOfTheDayAsync(ct);
    }
}

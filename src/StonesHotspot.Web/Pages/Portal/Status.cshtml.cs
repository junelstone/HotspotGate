using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web.Pages.Portal;

/// <summary>What the customer sees while connected: time/data left, speed, log out.</summary>
public sealed class StatusModel(
    SettingsService settings,
    AppPaths paths,
    SessionService sessions,
    PortalClient client,
    LiveTrafficMonitor speeds,
    AccessRefreshSignal refresh) : PortalPageModel(settings, paths)
{
    /// <summary>Where to forward the customer right after login (already chosen by the login page).</summary>
    [BindProperty(SupportsGet = true, Name = "next")]
    public string? Next { get; set; }

    [BindProperty(SupportsGet = true, Name = "welcome")]
    public bool JustLoggedIn { get; set; }

    public SessionStatus Status { get; private set; } = null!;
    public StatusView View { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() is null)
            return RedirectToPage("/Portal/Index");
        if (!PortalTarget.IsSafe(Next))
            Next = null;
        return Page();
    }

    /// <summary>Polled every second by the page.</summary>
    public async Task<IActionResult> OnGetJsonAsync()
    {
        Response.Headers.CacheControl = "no-store";
        var status = await LoadAsync();
        return status is null ? new JsonResult(new { active = false }) : new JsonResult(View with { Active = true });
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        if (await LoadAsync() is { } status)
        {
            await sessions.EndAsync(status.Session.Id, SessionEndReason.Logout, null, HttpContext.RequestAborted);
            refresh.Request();
        }
        return RedirectToPage("/Portal/Index");
    }

    private async Task<SessionStatus?> LoadAsync()
    {
        var device = client.Identify(HttpContext);
        if (device.Mac is null || await sessions.GetStatusForDeviceAsync(device.Mac, HttpContext.RequestAborted) is not { } status)
            return null;
        Status = status;
        View = StatusView.From(status, speeds.For(status.Session.Id), CultureInfo.CurrentUICulture);
        return status;
    }
}

/// <summary>The status values, formatted for the customer's language (also sent as JSON to the page).</summary>
public sealed record StatusView(
    bool Active,
    long? SecondsLeft,
    string TimeLeft,
    string? DataLeft,
    string Used,
    string Down,
    string Up,
    string? EndsAt)
{
    public static StatusView From(SessionStatus s, (double DownBps, double UpBps) speed, CultureInfo culture) => new(
        true,
        s.SecondsLeft,
        s.SecondsLeft is { } seconds ? Units.Duration(seconds, culture) : Strings.Status_Unlimited,
        s.BytesLeft is { } bytes ? Units.Bytes(bytes, culture) : null,
        Units.Bytes(s.Session.BytesUp + s.Session.BytesDown, culture),
        Units.Speed(speed.DownBps, culture),
        Units.Speed(speed.UpBps, culture),
        s.EndsAt?.ToLocalTime().ToString("g", culture));
}

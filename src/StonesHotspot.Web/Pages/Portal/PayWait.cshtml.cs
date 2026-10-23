using Microsoft.AspNetCore.Mvc;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Payments;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web.Pages.Portal;

/// <summary>
/// "Approve the payment on your phone": reloads itself every few seconds, asking the operator each time; shows the
/// code once paid. Only the device that started the payment can see it.
/// </summary>
public sealed class PayWaitModel(
    SettingsService settings, AppPaths paths, MobileMoneyService payments, PortalClient client, AccessRefreshSignal refresh) : PortalPageModel(settings, paths)
{
    public const int RefreshSeconds = 4;

    [BindProperty(SupportsGet = true, Name = "ref")]
    public string? Reference { get; set; }

    public MobilePayment Payment { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var device = client.Identify(HttpContext);
        if (string.IsNullOrEmpty(Reference) || device.Mac is null)
            return RedirectToPage("/Portal/Index");
        var payment = await payments.FindAsync(Reference, ct);
        if (payment?.Device is null || payment.Device.Mac != MacAddress.Normalize(device.Mac))
            return RedirectToPage("/Portal/Index");

        if (payment.Status == MobilePaymentStatus.Pending)
        {
            payment = await payments.CheckAsync(Reference, ct: ct) ?? payment;
            if (payment.Status == MobilePaymentStatus.Successful)
                refresh.Request();   // the device was just logged in: let it through now
        }
        Payment = payment;
        return Page();
    }
}

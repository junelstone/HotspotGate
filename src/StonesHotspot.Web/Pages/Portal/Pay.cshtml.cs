using Microsoft.AspNetCore.Mvc;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Payments;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Web.Portal;

namespace StonesHotspot.Web.Pages.Portal;

/// <summary>Buy a plan with MTN MoMo or Airtel Money: choose the plan, the operator and the number (Pro).</summary>
public sealed class PayModel(
    SettingsService settings, AppPaths paths, MobileMoneyService payments, PortalClient client, ILicenseService licence)
    : PortalPageModel(settings, paths)
{
    [BindProperty] public int TariffId { get; set; }
    [BindProperty] public PaymentMethod Method { get; set; } = PaymentMethod.MtnMoMo;
    [BindProperty] public string? Phone { get; set; }

    public bool Available { get; private set; }
    public IReadOnlyList<Tariff> Plans { get; private set; } = [];
    public IReadOnlyList<PaymentMethod> Methods { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!Available)
            return Page();
        var device = client.Identify(HttpContext);
        if (device.Mac is null)
        {
            Error = Strings.Portal_DeviceUnknown;
            return Page();
        }

        var result = await payments.StartAsync(device.Mac, device.Ip?.ToString(), TariffId, Method, Phone, HttpContext.RequestAborted);
        if (result.Status is PayStartStatus.Started or PayStartStatus.AlreadyPending)
            return Redirect($"/portal/pay/wait?ref={Uri.EscapeDataString(result.Reference!)}");
        Error = PortalTexts.PayError(result, Method);
        return Page();
    }

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (!licence.GetStatus().HotspotAllowed || !licence.IsAllowed(Feature.MobileMoneyPayments))
            return;
        var config = await payments.GetSettingsAsync(ct);
        Methods = new[] { PaymentMethod.MtnMoMo, PaymentMethod.AirtelMoney }
            .Where(m => m == PaymentMethod.MtnMoMo ? config.Mtn.Configured : config.Airtel.Configured).ToList();
        Plans = await payments.ListSellableTariffsAsync(config, ct);
        Available = Methods.Count > 0 && Plans.Count > 0;
        if (TariffId == 0 && Plans.Count > 0)
            TariffId = Plans[0].Id;
        if (!Methods.Contains(Method) && Methods.Count > 0)
            Method = Methods[0];
    }
}

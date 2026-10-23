using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>Form values of a plan as typed (price as text: "1 000").</summary>
public sealed class TariffInput
{
    public string Name { get; set; } = "";
    public string Price { get; set; } = "";
    public TariffType Type { get; set; } = TariffType.TimeBased;
    public long Quota { get; set; } = 60;
    public CountdownMode CountdownMode { get; set; } = CountdownMode.UsageOnly;
    public int? ValidityHours { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public int? DownloadKbps { get; set; }
    public int? UploadKbps { get; set; }
    public int MaxDevices { get; set; } = 1;
    public int? IdleTimeoutMinutes { get; set; }
    public bool IsActive { get; set; } = true;

    public static TariffInput From(Tariff t) => new()
    {
        Name = t.Name, Price = t.Price.ToNumberString(XafStyle.Ascii), Type = t.Type, Quota = t.Quota, CountdownMode = t.CountdownMode,
        ValidityHours = t.ValidityHours, ValidUntil = t.ValidUntil is { } u ? DateOnly.FromDateTime(u.ToLocalTime()) : null,
        DownloadKbps = t.DownloadKbps, UploadKbps = t.UploadKbps, MaxDevices = t.MaxDevices, IdleTimeoutMinutes = t.IdleTimeoutMinutes,
        IsActive = t.IsActive,
    };

    public Tariff ToTariff(Xaf price) => new()
    {
        Name = Name, Price = price, Type = Type, Quota = Type == TariffType.Unlimited ? 0 : Quota, CountdownMode = CountdownMode,
        ValidityHours = ValidityHours,
        // Last day included: valid until the end of that day, local time.
        ValidUntil = ValidUntil is { } d ? d.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Local).ToUniversalTime() : null,
        DownloadKbps = DownloadKbps, UploadKbps = UploadKbps, MaxDevices = MaxDevices, IdleTimeoutMinutes = IdleTimeoutMinutes,
        IsActive = IsActive,
    };
}

[Authorize(Policy = StaffPolicies.Admin)]
public sealed class TariffModel(TariffService tariffs, CurrentStaff current) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public TariffInput Input { get; set; } = new();
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var tariff = await tariffs.FindAsync(id, HttpContext.RequestAborted);
            if (tariff is null)
                return NotFound();
            Input = TariffInput.From(tariff);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid || !Xaf.TryParse(Input.Price, out var price))
        {
            Error = Strings.Common_InvalidAmount;
            return Page();
        }
        try
        {
            var actor = await current.GetAsync(HttpContext.RequestAborted);
            if (Id is { } id)
                await tariffs.UpdateAsync(id, Input.ToTariff(price), actor, HttpContext.RequestAborted);
            else
                await tariffs.CreateAsync(Input.ToTariff(price), actor, HttpContext.RequestAborted);
            TempData.Success(Strings.Common_Saved);
            return Redirect("/admin/tariffs");
        }
        catch (BusinessRuleException ex)
        {
            Error = ex.Code == ErrorCodes.DuplicateName ? Strings.Tariff_DuplicateName : ex.Message;
            return Page();
        }
    }
}

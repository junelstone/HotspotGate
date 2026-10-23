using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Setup;
using StonesHotspot.Web.Admin;

namespace StonesHotspot.Web.Pages;

/// <summary>
/// First-run wizard: language, business and Wi-Fi name, administrator password, internet/customer connections, first
/// plan prices. Only from the PC itself (localhost) and only until it is completed; afterwards it is gone.
/// </summary>
public sealed class SetupModel(SetupService setup, SettingsService settings, NetworkSetupService network) : PageModel
{
    public const int LastStep = 6;

    [BindProperty(SupportsGet = true)] public int Step { get; set; } = 1;
    [BindProperty] public string? Language { get; set; }
    [BindProperty] public string? BusinessName { get; set; }
    [BindProperty] public string? WifiName { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public string? Confirm { get; set; }
    [BindProperty] public string? WanId { get; set; }
    [BindProperty] public string? LanId { get; set; }
    public const string PricePrefix = "price-";

    /// <summary>Opened from another device: only a message is shown.</summary>
    public bool Remote { get; private set; }
    public string? Error { get; private set; }
    public NetworkOverview? Network { get; private set; }
    public IReadOnlyList<NetworkProblem> NetworkProblems { get; private set; } = [];
    public List<Tariff> Tariffs { get; private set; } = [];

    public static bool IsLocal(HttpContext http) => http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);

    public override async Task OnPageHandlerExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context,
        Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutionDelegate next)
    {
        if (await setup.IsCompletedAsync(HttpContext.RequestAborted))
        {
            context.Result = Redirect("/admin");
            return;
        }
        if (!IsLocal(HttpContext))
        {
            Remote = true;
            context.Result = Page();
            return;
        }
        Step = Math.Clamp(Step, 1, LastStep);
        await next();
    }

    public Task OnGetAsync() => LoadAsync();

    public async Task<IActionResult> OnPostLanguageAsync()
    {
        var language = Language == "en" ? "en" : "fr";
        await setup.SaveLanguageAsync(language, HttpContext.RequestAborted);
        return Redirect($"/setup?step=2&lang={language}");
    }

    public Task<IActionResult> OnPostBusinessAsync() =>
        SaveAsync(() => setup.SaveBusinessAsync(BusinessName ?? "", WifiName, HttpContext.RequestAborted), 3);

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var (password, confirm) = (Password ?? "", Confirm ?? "");
        Password = Confirm = null;
        if (password != confirm)
        {
            Error = Strings.Password_Mismatch;
            await LoadAsync();
            return Page();
        }
        return await SaveAsync(() => setup.SetAdminPasswordAsync(password, confirm, HttpContext.RequestAborted), 4,
            string.Format(Strings.Password_TooWeak, StaffService.MinPasswordLength));
    }

    public async Task<IActionResult> OnPostNetworkAsync()
    {
        var result = await network.ApplyAsync(WanId, LanId, null, HttpContext.RequestAborted);
        if (result.Succeeded)
            return Redirect("/setup?step=5");
        NetworkProblems = result.Problems;
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPricesAsync()
    {
        var prices = new Dictionary<int, long>();
        // Read by hand: a bound dictionary would also take every other form field when no price is posted.
        foreach (var (key, text) in Request.Form.Where(f => f.Key.StartsWith(PricePrefix, StringComparison.Ordinal)))
        {
            if (!int.TryParse(key.AsSpan(PricePrefix.Length), out var id) || !Xaf.TryParse(text, out var price) || price.IsNegative)
            {
                Error = Strings.Common_InvalidAmount;
                await LoadAsync();
                return Page();
            }
            prices[id] = price.Amount;
        }
        return await SaveAsync(() => setup.SavePricesAsync(prices, HttpContext.RequestAborted), 6);
    }

    public async Task<IActionResult> OnPostFinishAsync()
    {
        try
        {
            await setup.CompleteAsync(HttpContext.RequestAborted);
        }
        catch (BusinessRuleException)
        {
            return Redirect("/setup?step=3");
        }
        TempData.Success(Strings.Setup_Done);
        return Redirect("/admin/login");
    }

    private async Task<IActionResult> SaveAsync(Func<Task> save, int nextStep, string? errorText = null)
    {
        try
        {
            await save();
            return Redirect($"/setup?step={nextStep}");
        }
        catch (BusinessRuleException)
        {
            Error = errorText ?? Strings.Common_InvalidValue;
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        Language ??= await settings.GetAsync(SettingKeys.DefaultLanguage, ct);
        BusinessName ??= await settings.GetAsync(SettingKeys.BusinessName, ct);
        WifiName ??= await settings.GetAsync(SettingKeys.WifiName, ct);
        if (Step == 4)
        {
            Network = await network.GetOverviewAsync(ct);
            WanId ??= Network.Configuration.WanAdapterId ?? Network.Suggestion.Wan?.Id;
            LanId ??= Network.Configuration.LanAdapterId ?? Network.Suggestion.LanCandidates.FirstOrDefault()?.Id;
        }
        if (Step is 5 or 6)
            Tariffs = await setup.TariffsAsync(ct);
    }
}

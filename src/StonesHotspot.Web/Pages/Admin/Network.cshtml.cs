using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core.Localization;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Web.Pages.Admin;

[Microsoft.AspNetCore.Authorization.Authorize(Policy = Auth.StaffPolicies.Admin)]
public sealed class NetworkModel(NetworkSetupService setup) : PageModel
{
    public NetworkOverview Overview { get; private set; } = null!;

    [BindProperty]
    public string? WanId { get; set; }

    [BindProperty]
    public string? LanId { get; set; }

    /// <summary>Outcome of the button just clicked (problems found while applying, or a success message).</summary>
    public IReadOnlyList<NetworkProblem> ActionProblems { get; private set; } = [];
    public string? SuccessMessage { get; private set; }

    public async Task OnGetAsync()
    {
        Overview = await setup.GetOverviewAsync(HttpContext.RequestAborted);
        WanId = Overview.Configuration.WanAdapterId ?? Overview.Suggestion.Wan?.Id;
        LanId = Overview.Configuration.LanAdapterId ?? Overview.Suggestion.LanCandidates.FirstOrDefault()?.Id;
    }

    public async Task OnPostApplyAsync()
    {
        var result = await setup.ApplyAsync(WanId, LanId, null, HttpContext.RequestAborted);
        await ShowResultAsync(result, Strings.Network_Applied);
    }

    public async Task OnPostDisableAsync()
    {
        var result = await setup.DisableAsync(null, HttpContext.RequestAborted);
        await ShowResultAsync(result, Strings.Network_Disabled);
    }

    private async Task ShowResultAsync(NetworkApplyResult result, string success)
    {
        ActionProblems = result.Problems;
        SuccessMessage = result.Succeeded ? success : null;
        Overview = await setup.GetOverviewAsync(HttpContext.RequestAborted);
    }

    /// <summary>Problems to show: those of the action just done, else those of the saved configuration.</summary>
    public IEnumerable<NetworkProblem> VisibleProblems =>
        ActionProblems.Count > 0 || SuccessMessage is not null ? ActionProblems : Overview.Problems;
}

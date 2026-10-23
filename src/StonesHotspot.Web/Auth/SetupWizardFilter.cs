using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Web.Auth;

/// <summary>
/// Until the first-run wizard is finished, every dashboard page leads to <c>/setup</c> (which, from another device,
/// says to finish the setup on the PC). Setting <c>StonesHotspot:SetupWizard</c> = false turns this off (tests).
/// </summary>
public sealed class SetupWizardFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        if (context.ActionDescriptor.ViewEnginePath.StartsWith("/Admin", StringComparison.Ordinal)
            && services.GetRequiredService<IConfiguration>().GetValue("StonesHotspot:SetupWizard", true)
            && !await services.GetRequiredService<SetupService>().IsCompletedAsync(context.HttpContext.RequestAborted))
        {
            context.Result = new RedirectResult("/setup");
            return;
        }
        await next();
    }
}

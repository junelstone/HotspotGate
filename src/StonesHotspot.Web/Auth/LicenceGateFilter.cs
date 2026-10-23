using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Licensing;

namespace StonesHotspot.Web.Auth;

/// <summary>
/// Once the trial or licence has ended (after the grace period) the dashboard only offers the licence page, the
/// reports and the backup: nothing is deleted, nothing is locked, customers' data can always be taken away.
/// </summary>
public sealed class LicenceGateFilter : IAsyncPageFilter
{
    public static readonly string[] AllowedWhenStopped =
        ["/Admin/Licence", "/Admin/Reports", "/Admin/Backup", "/Admin/Audit", "/Admin/Login", "/Admin/Logout", "/Admin/Password", "/Admin/Denied", "/Admin/Receipt", "/Admin/Shift"];

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var page = context.ActionDescriptor.ViewEnginePath;
        if (page.StartsWith("/Admin", StringComparison.Ordinal) && !AllowedWhenStopped.Contains(page)
            && context.HttpContext.RequestServices.GetService<ILicenseService>() is { } licence && licence.GetStatus().Stopped)
        {
            context.Result = new RedirectToPageResult("/Admin/Licence");
            return;
        }
        await next();
    }
}

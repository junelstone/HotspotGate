using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StonesHotspot.Web.Auth;

/// <summary>A staff member with a temporary password can only use the "change password" page and sign out.</summary>
public sealed class ForcePasswordChangeFilter : IAsyncPageFilter
{
    private static readonly string[] Allowed = ["/Admin/Password", "/Admin/Logout", "/Admin/Login", "/Admin/Denied"];

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var page = context.ActionDescriptor.ViewEnginePath;
        if (user.Identity?.IsAuthenticated == true
            && user.FindFirst(StaffClaims.MustChangePassword)?.Value == "1"
            && page.StartsWith("/Admin", StringComparison.Ordinal)
            && !Allowed.Contains(page))
        {
            context.Result = new RedirectToPageResult("/Admin/Password");
            return;
        }
        await next();
    }
}

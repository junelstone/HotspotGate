using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Web;

/// <summary>
/// Page answers only to requests from the PC itself. Protects the setup pages until staff login exists
/// (phase 5): the admin port is reachable from the customers' Wi-Fi.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LocalOnlyAttribute : Attribute, IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (!IsLocal(context.HttpContext.Connection.RemoteIpAddress))
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "text/plain; charset=utf-8",
                Content = Strings.Admin_LocalOnly,
            };
            return;
        }
        await next();
    }

    public static bool IsLocal(IPAddress? remote) => remote is not null && IPAddress.IsLoopback(remote);
}

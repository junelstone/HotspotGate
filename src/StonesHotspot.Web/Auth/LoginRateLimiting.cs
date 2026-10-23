using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Web.Auth;

/// <summary>
/// Limits the posts of every login form (staff sign-in, password change, wizard, portal codes, accounts, password of
/// the day, free access, SMS, Mobile Money): per address <c>StonesHotspot:LoginRateLimit:PerAddress</c> (10) per
/// minute, all customers together <c>…:Total</c> (300) per minute. This comes on top of the per-device and
/// per-address locks, so changing MAC address gives no new tries.
/// </summary>
public static class LoginRateLimiting
{
    private static readonly PathString[] LoginForms = ["/admin/login", "/admin/password", "/setup", "/portal", "/portal/pay"];

    public static bool IsLoginPost(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) && LoginForms.Any(p => request.Path.Equals(p, StringComparison.OrdinalIgnoreCase));

    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var perAddress = configuration.GetValue("StonesHotspot:LoginRateLimit:PerAddress", 10);
        var total = configuration.GetValue("StonesHotspot:LoginRateLimit:Total", 300);
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsLoginPost(context.Request)
                    ? RateLimitPartition.GetFixedWindowLimiter(Address(context), _ => Window(perAddress))
                    : RateLimitPartition.GetNoLimiter("")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsLoginPost(context.Request)
                    ? RateLimitPartition.GetFixedWindowLimiter("all", _ => Window(total))
                    : RateLimitPartition.GetNoLimiter("")));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                response.Headers.RetryAfter = "60";
                response.ContentType = "text/html; charset=utf-8";
                var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                await response.WriteAsync($"""
                    <!doctype html><html lang="{lang}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>{WebUtility.HtmlEncode(Strings.AppName)}</title></head>
                    <body style="font-family:system-ui,sans-serif;margin:2rem;max-width:30rem">
                    <h1 style="font-size:1.3rem">{WebUtility.HtmlEncode(Strings.Common_TooManyTries)}</h1>
                    <p><a href="javascript:history.back()">{WebUtility.HtmlEncode(Strings.Setup_Back)}</a></p></body></html>
                    """, ct);
            };
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) =>
        new() { PermitLimit = Math.Max(1, permits), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

    private static string Address(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "?";
}

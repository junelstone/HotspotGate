using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Tests.Infrastructure;

/// <summary>Creates a staff member (password already changed) and returns a browser-like client signed in as them.</summary>
public static class StaffSignIn
{
    public const string Password = "Mot-de-passe-2026";

    public static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    public sealed record SignedIn(HttpClient Http, Staff Staff);

    public static async Task<SignedIn> AsAsync(WebApplicationFactory<Program> factory, StaffRole role = StaffRole.Admin, string? remoteIp = null)
    {
        var username = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..20];
        Staff staff;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            staff = await scope.ServiceProvider.GetRequiredService<StaffService>().CreateAsync(username, Password, role, null, null);
            var db = scope.ServiceProvider.GetRequiredService<HotspotDbContext>();
            await db.Staff.Where(s => s.Id == staff.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.MustChangePassword, false));
        }

        var http = Client(factory, remoteIp);
        var response = await PostFormAsync(http, "/admin/login", "/admin/login", new() { ["Username"] = username, ["Password"] = Password });
        if (response.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException($"Sign-in failed: {response.StatusCode}");
        return new SignedIn(http, staff);
    }

    /// <summary>Cookie-keeping client that does not follow redirects; optionally from a customer-network address.</summary>
    public static HttpClient Client(WebApplicationFactory<Program> factory, string? remoteIp = null) =>
        remoteIp is null
            ? factory.CreateDefaultClient(new CookieContainerHandler())
            : factory.CreateDefaultClient(new CookieContainerHandler(), new RemoteIpHandler(remoteIp));

    /// <summary>GETs <paramref name="pageUrl"/> for its anti-forgery token, then posts the form to <paramref name="postUrl"/>.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient http, string pageUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await http.GetStringAsync(pageUrl);
        fields["__RequestVerificationToken"] = TokenRegex.Match(page).Groups[1].Value;
        return await http.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}

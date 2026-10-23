using System.Net;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Licensing;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.StaffSignIn;

namespace StonesHotspot.Tests;

/// <summary>Licence page, activation, banners and the dashboard once the licence has stopped.</summary>
public sealed class LicencePageTests(ServiceSmokeTests.Factory factory) : IClassFixture<ServiceSmokeTests.Factory>
{
    private static async Task<string> Html(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Licence_page_shows_the_machine_id_and_admins_activate_codes()
    {
        var admin = await AsAsync(factory);
        var page = await Html(await admin.Http.GetAsync("/admin/licence"));
        Assert.Contains("MID-AAAA-BBBB-CCCC", page);
        Assert.Contains("https://wa.me/?text=", page);
        Assert.Contains("MID-AAAA-BBBB-CCCC", Uri.UnescapeDataString(page));

        var bad = await Html(await PostFormAsync(admin.Http, "/admin/licence", "/admin/licence", new() { ["Code"] = "pas un code" }));
        Assert.Contains("Code invalide", bad);
        var ok = await PostFormAsync(admin.Http, "/admin/licence", "/admin/licence", new() { ["Code"] = "SHS1-ABCDE" });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("SHS1-ABCDE", factory.Licence.LastActivatedCode);
        Assert.Contains("Licence activée", await Html(await admin.Http.GetAsync("/admin/licence")));

        var cashier = await AsAsync(factory, StaffRole.Cashier);
        var cashierPage = await Html(await cashier.Http.GetAsync("/admin/licence"));
        Assert.Contains("MID-AAAA-BBBB-CCCC", cashierPage);
        Assert.DoesNotContain("name=\"Code\"", cashierPage);
        var refused = await PostFormAsync(cashier.Http, "/admin/licence", "/admin/licence", new() { ["Code"] = "SHS1-X" });
        Assert.Contains("/admin/denied", refused.Headers.Location?.OriginalString ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SHS1-ABCDE", factory.Licence.LastActivatedCode);   // nothing activated by the cashier
    }

    [Fact]
    public async Task Stopped_licence_limits_the_dashboard_and_the_portal_but_keeps_data_reachable()
    {
        var admin = await AsAsync(factory);
        factory.Licence.HotspotAllowed = false;
        try
        {
            var tariffs = await admin.Http.GetAsync("/admin/tariffs");
            Assert.Equal(HttpStatusCode.Redirect, tariffs.StatusCode);
            Assert.Equal("/admin/licence", tariffs.Headers.Location!.OriginalString, ignoreCase: true);
            Assert.Equal(HttpStatusCode.Redirect, (await admin.Http.GetAsync("/admin/till")).StatusCode);

            var licence = await Html(await admin.Http.GetAsync("/admin/licence"));
            Assert.Contains("Service arrêté", licence);
            Assert.Equal(HttpStatusCode.OK, (await admin.Http.GetAsync("/admin/reports")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.Http.GetAsync("/admin/backup")).StatusCode);

            Assert.Contains("Service temporairement indisponible", WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync("/portal")));
            var health = await factory.CreateClient().GetStringAsync("/health");
            Assert.Contains("\"hotspotAllowed\":false", health);
        }
        finally
        {
            factory.Licence.HotspotAllowed = true;
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.Http.GetAsync("/admin/tariffs")).StatusCode);
    }
}

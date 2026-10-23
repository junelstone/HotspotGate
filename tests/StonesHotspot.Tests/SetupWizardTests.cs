using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Firewall;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

/// <summary>The first-run wizard on a new installation (the other web tests switch it off).</summary>
public sealed class SetupWizardTests(SetupWizardTests.WizardFactory factory) : IClassFixture<SetupWizardTests.WizardFactory>
{
    public sealed class WizardFactory : ServiceSmokeTests.Factory
    {
        protected override bool SetupWizard => true;
    }

    private static Task<HttpResponseMessage> Post(HttpClient http, int step, string handler, Dictionary<string, string> fields)
    {
        fields["Step"] = step.ToString();
        return StaffSignIn.PostFormAsync(http, $"/setup?step={step}", $"/setup?handler={handler}", fields);
    }

    [Fact]
    public async Task New_installation_is_set_up_from_the_pc_only_then_the_wizard_is_gone()
    {
        var local = StaffSignIn.Client(factory);
        var remote = StaffSignIn.Client(factory, "192.168.137.50");

        // Dashboard leads to the wizard; another device is told to use the PC.
        var login = await local.GetAsync("/admin/login");
        Assert.Equal("/setup", login.Headers.Location?.OriginalString);
        var fromPhone = await remote.GetStringAsync("http://localhost/setup");
        Assert.DoesNotContain("name=\"Language\"", fromPhone);

        Assert.Equal(HttpStatusCode.Redirect, (await Post(local, 1, "Language", new() { ["Language"] = "en" })).StatusCode);
        Assert.Equal("/setup?step=3", (await Post(local, 2, "Business", new() { ["BusinessName"] = "Cyber Stone", ["WifiName"] = "Stone-WiFi" })).Headers.Location?.OriginalString);

        // Finishing before choosing the password sends back to that step.
        Assert.Equal("/setup?step=3", (await Post(local, 6, "Finish", [])).Headers.Location?.OriginalString);

        var weak = await Post(local, 3, "Password", new() { ["Password"] = "court", ["Confirm"] = "court" });
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);   // page again with the error
        Assert.Equal("/setup?step=4", (await Post(local, 3, "Password", new() { ["Password"] = "Mon-Hotspot-2026", ["Confirm"] = "Mon-Hotspot-2026" })).Headers.Location?.OriginalString);

        // Network: WAN = Ethernet, LAN = Ethernet 2 → firewall rules on the LAN adapter.
        Assert.Equal("/setup?step=5", (await Post(local, 4, "Network", new() { ["WanId"] = FakeAdapters.EthernetId, ["LanId"] = FakeAdapters.LanId })).Headers.Location?.OriginalString);
        var firewall = (NoFirewall)factory.Services.GetRequiredService<IFirewallManager>();
        Assert.Contains(firewall.Applied, r => r.InterfaceName == "Ethernet 2" && r.Port == 53);

        int hourPlan;
        await using (var scope = factory.Services.CreateAsyncScope())
            hourPlan = (await scope.ServiceProvider.GetRequiredService<HotspotDbContext>().Tariffs.SingleAsync(t => t.Name == "1 heure")).Id;
        Assert.Equal("/setup?step=6", (await Post(local, 5, "Prices", new() { [$"price-{hourPlan}"] = "250" })).Headers.Location?.OriginalString);
        Assert.Equal("/admin/login", (await Post(local, 6, "Finish", [])).Headers.Location?.OriginalString);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HotspotDbContext>();
            var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
            Assert.Equal("Cyber Stone", await settings.GetAsync(SettingKeys.BusinessName));
            Assert.Equal("en", await settings.GetAsync(SettingKeys.DefaultLanguage));
            Assert.Equal(250, (await db.Tariffs.SingleAsync(t => t.Id == hourPlan)).Price.Amount);
            Assert.False((await db.Staff.SingleAsync(s => s.Username == DatabaseInitializer.AdminUsername)).MustChangePassword);
        }
        var paths = factory.Services.GetRequiredService<AppPaths>();
        Assert.False(File.Exists(Path.Combine(paths.Root, AppPaths.InitialAdminPasswordFileName)));

        // The admin signs in with the chosen password; the wizard no longer exists.
        var signIn = await StaffSignIn.PostFormAsync(local, "/admin/login", "/admin/login", new() { ["Username"] = "admin", ["Password"] = "Mon-Hotspot-2026" });
        Assert.Equal("/admin", signIn.Headers.Location?.OriginalString);
        Assert.Equal("/admin", (await local.GetAsync("/setup")).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await local.GetAsync("/admin")).StatusCode);
    }
}

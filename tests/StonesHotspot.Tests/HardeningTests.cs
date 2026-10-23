using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;
using StonesHotspot.Web;

namespace StonesHotspot.Tests;

/// <summary>Phase 12: dashboard reachable only from the PC and the customer network, login rate limits, headers, secrets.</summary>
public sealed class HardeningTests(HardeningTests.StrictFactory factory) : IClassFixture<HardeningTests.StrictFactory>
{
    /// <summary>The real limits of a shop, scaled down: 3 login posts per address per minute.</summary>
    public sealed class StrictFactory : ServiceSmokeTests.Factory
    {
        protected override int LoginRateLimit => 3;
    }

    private async Task SetUpNetworkAsync()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().ApplyAsync(FakeAdapters.EthernetId, FakeAdapters.LanId, null);
        await factory.Services.GetServices<IHostedService>().OfType<HotspotEngineService>().Single().TickAsync(CancellationToken.None);
    }

    private static DashboardAccessMiddleware Access(params string[] extra)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            extra.Select((n, i) => new KeyValuePair<string, string?>($"StonesHotspot:AdminNetworks:{i}", n))).Build();
        return new DashboardAccessMiddleware(_ => Task.CompletedTask, config);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.137.45", true)]       // phone on the hotspot Wi-Fi
    [InlineData("::ffff:192.168.137.45", true)]
    [InlineData("192.168.1.30", false)]        // internet box side
    [InlineData("41.202.10.7", false)]         // internet
    [InlineData("fe80::1", false)]
    [InlineData("10.0.0.9", true)]             // office network listed in StonesHotspot:AdminNetworks
    public void Dashboard_is_reachable_only_from_the_pc_the_customer_network_and_listed_networks(string ip, bool allowed)
    {
        Assert.Equal(allowed, Access("10.0.0.0/24").IsAllowed(IPAddress.Parse(ip), Ipv4Subnet.Parse("192.168.137.1/24")));
        Assert.Equal(ip is "127.0.0.1" or "::1", Access().IsAllowed(IPAddress.Parse(ip), null));   // no LAN yet: the PC only
    }

    [Fact]
    public async Task Dashboard_answers_404_to_the_internet_side_but_the_portal_and_callbacks_stay()
    {
        await SetUpNetworkAsync();
        var wan = StaffSignIn.Client(factory, "192.168.1.30");

        foreach (var url in new[] { "/admin/login", "/admin", "/setup", "/health" })
            Assert.Equal(HttpStatusCode.NotFound, (await wan.GetAsync("http://localhost" + url)).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await wan.PostAsync("http://localhost/api/payments/mtn", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StaffSignIn.Client(factory, "192.168.137.80").GetAsync("http://localhost/admin/login")).StatusCode);
    }

    [Fact]
    public async Task Login_forms_are_limited_per_address()
    {
        var http = StaffSignIn.Client(factory, "127.0.0.9");   // an address only this test uses
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codes.Add((await StaffSignIn.PostFormAsync(http, "/admin/login", "/admin/login", new() { ["Username"] = "admin", ["Password"] = "faux" + i })).StatusCode);

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, codes.Take(3));
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[3]);
        var refused = await StaffSignIn.PostFormAsync(http, "/admin/login", "/admin/login", new() { ["Username"] = "admin", ["Password"] = "x" });
        Assert.Equal("60", refused.Headers.RetryAfter?.ToString());
        Assert.Contains("essais. R", await refused.Content.ReadAsStringAsync());

        // Another address and plain page views are not affected.
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/admin/login")).StatusCode);
        var other = await StaffSignIn.PostFormAsync(StaffSignIn.Client(factory, "127.0.0.10"), "/admin/login", "/admin/login", new() { ["Username"] = "admin", ["Password"] = "faux" });
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode);
    }

    [Fact]
    public async Task Pages_carry_the_browser_protection_headers_and_no_server_name()
    {
        var response = await StaffSignIn.Client(factory).GetAsync("/admin/login");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }

    [Fact]
    public async Task Secret_settings_never_appear_in_the_audit_log()
    {
        await using var host = await TestHost.CreateAsync();
        foreach (var key in SettingKeys.Secrets)
            await host.Run<SettingsService>(s => s.SetAsync(key, "TRES-SECRET-" + key, null));   // caller forgot secret: true

        var details = await host.Query(db => db.AuditLogs.Select(a => a.Details).ToListAsync());
        Assert.DoesNotContain(details, d => d != null && d.Contains("TRES-SECRET", StringComparison.Ordinal));
        Assert.Equal(SettingKeys.Secrets.Count, details.Count(d => d != null && d.Contains("(secret changed)", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Every log call in the source: no argument may be a password, code, key, secret or token (phone numbers go
    /// through <c>SmsService.Mask</c>). Fails with the file and line of the offending call.
    /// </summary>
    [Fact]
    public void No_log_call_writes_a_secret()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "StonesHotspot.sln")))
            root = root.Parent!;
        var call = new Regex(@"\.Log(Trace|Debug|Information|Warning|Error|Critical)\((.*?)\);", RegexOptions.Singleline);
        var strings = new Regex(@"\$?""(?:[^""\\]|\\.)*""");
        var secret = new Regex(@"(?i)\b(\w*password\w*|\w*secret\w*|\w*apikey\w*|\w*token\w*|code|\w+\.code|pin|phone)\b");

        var offenders = new List<string>();
        foreach (var dir in new[] { "src", "tools" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root.FullName, dir), "*.cs", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            {
                var text = File.ReadAllText(file);
                foreach (Match m in call.Matches(text))
                {
                    var arguments = strings.Replace(m.Groups[2].Value, "\"\"").Replace("SmsService.Mask(phone)", "masked").Replace("Mask(phone)", "masked");
                    if (secret.Match(arguments) is { Success: true } hit)
                        offenders.Add($"{Path.GetRelativePath(root.FullName, file)}:{text[..m.Index].Count(c => c == '\n') + 1} ({hit.Value})");
                }
            }
        Assert.Empty(offenders);
    }
}

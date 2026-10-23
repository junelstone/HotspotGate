using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core.Data;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Ics;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

/// <summary>The real service host (pages, database, background services) with the Windows network parts faked.</summary>
public sealed class ServiceSmokeTests : IClassFixture<ServiceSmokeTests.Factory>
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public string DataDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "StonesHotspotTests", Guid.NewGuid().ToString("N"));

        public FakeIcsController Ics { get; } = new();
        public FakeAdapterProvider Adapters { get; } = new(FakeAdapters.TypicalPc());
        public FakeNeighborTable Neighbors { get; } = new();
        public FakeLicenseService Licence { get; } = new();
        public FakeSmsTransportFactory Sms { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("StonesHotspot:DataDirectory", DataDirectory);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IIcsController>(Ics);
                services.AddSingleton<INetworkAdapterProvider>(Adapters);
                services.AddSingleton<INeighborTable>(Neighbors);
                services.AddSingleton<IPacketGate>(sp => new FakePacketGate(sp.GetRequiredService<PacketProcessor>()));
                services.AddSingleton<IPrivilegeChecker>(new FakePrivileges());
                services.AddSingleton<StonesHotspot.Licensing.ILicenseService>(Licence);
                services.AddSingleton<StonesHotspot.Core.Sms.ISmsTransportFactory>(Sms);
                services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
            });
        }
    }

    /// <summary>TestServer has no client address: take it from a header, 127.0.0.1 by default.</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(
                    context.Request.Headers.TryGetValue(RemoteIpHeader, out var ip) ? ip.ToString() : "127.0.0.1");
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private readonly Factory _factory;

    public ServiceSmokeTests(Factory factory) => _factory = factory;

    private sealed record Health(string Status, string Edition, int? DaysLeft);

    [Fact]
    public async Task Health_endpoint_reports_ok_and_trial()
    {
        var health = await _factory.CreateClient().GetFromJsonAsync<Health>("/health");

        Assert.Equal("ok", health?.Status);
        Assert.Equal("Trial", health?.Edition);
        Assert.Equal(30, health?.DaysLeft);
    }

    [Theory]
    [InlineData("", "lang=\"fr\"", "Version d'essai Pro")]
    [InlineData("?lang=en", "lang=\"en\"", "Pro trial")]
    public async Task Admin_home_is_localised(string query, string langAttribute, string text)
    {
        var admin = await StaffSignIn.AsAsync(_factory);
        var response = await admin.Http.GetAsync("/admin" + query);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(langAttribute, html);
        Assert.Contains(text, html);
    }

    [Fact]
    public async Task Language_choice_is_remembered_for_the_next_pages()
    {
        var client = (await StaffSignIn.AsAsync(_factory)).Http;
        await client.GetAsync("/admin?lang=en");

        var html = await client.GetStringAsync("/admin/network");
        Assert.Contains("Network setup", html);
    }

    [Fact]
    public async Task Service_creates_its_data_and_log_folders_and_database()
    {
        await _factory.CreateClient().GetAsync("/health");

        Assert.True(Directory.Exists(Path.Combine(_factory.DataDirectory, "logs")));
        Assert.True(File.Exists(Path.Combine(_factory.DataDirectory, "stoneshotspot.db")));
        Assert.True(File.Exists(Path.Combine(_factory.DataDirectory, "initial-admin-password.txt")));
    }

    [Fact]
    public async Task Network_page_lists_adapters_on_the_pc_itself()
    {
        var html = WebUtility.HtmlDecode(await (await StaffSignIn.AsAsync(_factory)).Http.GetStringAsync("/admin/network"));

        Assert.Contains("Configuration réseau", html);
        Assert.Contains("Ethernet 2", html);
        Assert.Contains("USB 3.0 Gigabit Ethernet", html);
    }

    [Theory]
    [InlineData("/admin/network")]
    [InlineData("/admin/devices")]
    [InlineData("/admin/live")]
    [InlineData("/admin/settings")]
    public async Task Setup_pages_need_an_administrator(string url)
    {
        // Anyone on the Wi-Fi: sent to the sign-in page.
        var anonymous = await StaffSignIn.Client(_factory, "192.168.137.23").GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/admin/login", anonymous.Headers.Location!.PathAndQuery);

        // A cashier: refused.
        var cashier = await StaffSignIn.AsAsync(_factory, StonesHotspot.Core.Data.Entities.StaffRole.Cashier, "192.168.137.23");
        var refused = await cashier.Http.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.StartsWith("/admin/denied", refused.Headers.Location!.PathAndQuery);

        // An administrator, even from a phone on the Wi-Fi.
        var admin = await StaffSignIn.AsAsync(_factory, remoteIp: "192.168.137.24");
        Assert.Equal(HttpStatusCode.OK, (await admin.Http.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Sharing_form_enables_ics_with_anti_forgery_protection()
    {
        var client = (await StaffSignIn.AsAsync(_factory)).Http;
        var page = await client.GetStringAsync("/admin/network");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        var withoutToken = await client.PostAsync("/admin/network?handler=Apply", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["WanId"] = FakeAdapters.EthernetId, ["LanId"] = FakeAdapters.LanId,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        var response = await client.PostAsync("/admin/network?handler=Apply", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["WanId"] = FakeAdapters.EthernetId, ["LanId"] = FakeAdapters.LanId, ["__RequestVerificationToken"] = token,
        }));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Partage activé.", html);
        Assert.True(_factory.Ics.GetStatus().IsSharing(FakeAdapters.EthernetId, FakeAdapters.LanId));
    }

    [Fact]
    public async Task Discovered_devices_are_listed_and_recorded()
    {
        await using (var setupScope = _factory.Services.CreateAsyncScope())
            await setupScope.ServiceProvider.GetRequiredService<NetworkSetupService>().ApplyAsync(FakeAdapters.EthernetId, FakeAdapters.LanId, null);
        var lan = _factory.Adapters.Adapters.Single(a => a.Id == FakeAdapters.LanId);
        _factory.Adapters.Adapters[_factory.Adapters.Adapters.IndexOf(lan)] =
            lan with { Addresses = [Ipv4Subnet.Parse("192.168.137.1/24")] };
        _factory.Neighbors.Add(new NeighborEntry(lan.InterfaceIndex, IPAddress.Parse("192.168.137.42"), "DA:A1:19:00:00:42", NeighborState.Reachable));

        var discovery = _factory.Services.GetServices<IHostedService>().OfType<DeviceDiscoveryService>().Single();
        await discovery.ScanOnceAsync(CancellationToken.None);

        var html = WebUtility.HtmlDecode(await (await StaffSignIn.AsAsync(_factory)).Http.GetStringAsync("/admin/devices?lang=en"));
        Assert.Contains("DA:A1:19:00:00:42", html);
        Assert.Contains("192.168.137.42", html);
        Assert.Contains("Private MAC", html);

        await using var scope = _factory.Services.CreateAsyncScope();
        var device = await scope.ServiceProvider.GetRequiredService<HotspotDbContext>().Devices.SingleAsync(d => d.Mac == "DA:A1:19:00:00:42");
        Assert.Equal("192.168.137.42", device.LastIp);
    }
}

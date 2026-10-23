using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Gate.Packets;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

/// <summary>Engine, access table, redirect middleware and pages working together in the real service host.</summary>
public sealed class GateIntegrationTests : IClassFixture<ServiceSmokeTests.Factory>, IAsyncLifetime
{
    private const string PhoneIp = "192.168.137.23";
    private const string PhoneMac = "DA:A1:19:00:00:23";

    private readonly ServiceSmokeTests.Factory _factory;

    public GateIntegrationTests(ServiceSmokeTests.Factory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<NetworkSetupService>()
            .ApplyAsync(FakeAdapters.EthernetId, FakeAdapters.LanId, null);
        Assert.True(result.Succeeded);
        await TickAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task TickAsync() =>
        _factory.Services.GetServices<IHostedService>().OfType<HotspotEngineService>().Single().TickAsync(CancellationToken.None);

    private PacketProcessor Processor => _factory.Services.GetRequiredService<IPacketGate>().Processor;

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private HttpRequestMessage FromPhone(string url, string? host = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(ServiceSmokeTests.RemoteIpHeader, PhoneIp);
        if (host is not null)
            request.Headers.Host = host;
        return request;
    }

    private HttpClient Client() => _factory.CreateClient(new() { AllowAutoRedirect = false });

    [Fact]
    public async Task Whitelisting_opens_the_gate_and_revoking_closes_it_at_the_next_tick()
    {
        const string ip = "192.168.137.61", mac = "DA:A1:19:00:00:61";
        _factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(ip), mac, NeighborState.Reachable));
        var packet = Tcp(ip, 50000, "93.184.216.34", 443, TcpFlags.Syn);

        await InScope(sp => sp.GetRequiredService<DeviceService>().SetWhitelistedAsync(mac, true, null).ContinueWith(_ => 0));
        await TickAsync();
        Assert.Equal(GateVerdict.Pass, Processor.Process((byte[])packet.Clone(), PacketSource.Forward, false));

        await InScope(sp => sp.GetRequiredService<DeviceService>().SetWhitelistedAsync(mac, false, null).ContinueWith(_ => 0));
        await TickAsync(); // the engine ticks every second
        Assert.Equal(GateVerdict.Reset, Processor.Process(packet, PacketSource.Forward, false));
    }

    [Fact]
    public async Task Traffic_through_the_gate_is_charged_to_the_session()
    {
        const string ip = "192.168.137.62", mac = "DA:A1:19:00:00:62";
        _factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(ip), mac, NeighborState.Reachable));
        var session = await InScope(async sp =>
        {
            var tariff = (await sp.GetRequiredService<TariffService>().ListAsync()).First();
            var voucher = (await sp.GetRequiredService<VoucherService>().GenerateBatchAsync(tariff.Id, 1, "gate-test", false, null)).Single();
            return (await sp.GetRequiredService<SessionService>().LoginWithVoucherAsync(mac, ip, voucher.Code)).Session!;
        });
        await TickAsync();

        Processor.Process(Tcp(ip, 50000, "93.184.216.34", 443, TcpFlags.Ack, payload: new byte[460]), PacketSource.Forward, false);
        Processor.Process(Tcp("93.184.216.34", 443, ip, 50000, TcpFlags.Ack, payload: new byte[1460]), PacketSource.Forward, false);
        await TickAsync();

        var stored = await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id));
        Assert.Equal(500, stored.BytesUp);
        Assert.Equal(1500, stored.BytesDown);
    }

    [Theory]
    [InlineData("connectivitycheck.gstatic.com", "/generate_204")]          // Android
    [InlineData("captive.apple.com", "/hotspot-detect.html")]              // Apple
    [InlineData("www.msftconnecttest.com", "/connecttest.txt")]            // Windows
    [InlineData("detectportal.firefox.com", "/canonical.html")]            // Firefox
    [InlineData("www.example.com", "/news?id=1")]
    public async Task Customer_web_requests_are_sent_to_the_portal(string host, string path)
    {
        _factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(PhoneIp), PhoneMac, NeighborState.Reachable));

        var response = await Client().SendAsync(FromPhone(path, host));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        var port = _factory.Services.GetRequiredService<HotspotNetworkOptions>().PortalPort; // 8081 in Development
        Assert.StartsWith(port == 80 ? "http://192.168.137.1/portal?" : $"http://192.168.137.1:{port}/portal?", location);
        Assert.Contains("mac=" + Uri.EscapeDataString(PhoneMac), location);
        Assert.Contains("url=" + Uri.EscapeDataString($"http://{host}{path}"), location);
        Assert.True(response.Headers.ConnectionClose);
    }

    [Fact]
    public async Task Portal_page_answers_on_the_gateway_address()
    {
        var response = await Client().SendAsync(FromPhone("/portal?url=http%3A%2F%2Fwww.example.com%2F", "192.168.137.1"));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bienvenue sur le Wi-Fi de", html);
        Assert.Contains("Entrez le code de votre ticket", html);
    }

    [Fact]
    public async Task Blocked_domain_shows_the_blocked_page()
    {
        await InScope(sp => sp.GetRequiredService<WebFilterService>().AddAsync("tiktok.com", WebFilterKind.Block, null, null));
        Processor.Update(Processor.Snapshot! with
        {
            Rules = HotspotEngineService.ToFilterRules(await InScope(sp => sp.GetRequiredService<WebFilterService>().ListEnabledAsync())),
        });

        var request = FromPhone("/foryou", "www.tiktok.com");
        request.Headers.Add("Accept-Language", "en");
        var response = await Client().SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Site blocked", html);
        Assert.Contains("www.tiktok.com", html);
    }

    [Fact]
    public async Task Requests_from_the_pc_itself_are_never_redirected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "www.example.com";
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Gate_page_creates_a_test_voucher()
    {
        var client = Client();
        var page = await client.GetStringAsync("/admin/gate");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var tariffId = await InScope(async sp => (await sp.GetRequiredService<TariffService>().ListAsync()).First().Id);

        var response = await client.PostAsync("/admin/gate?handler=CreateVoucher", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tariffId"] = tariffId.ToString(), ["__RequestVerificationToken"] = token,
        }));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        var code = Regex.Match(html, "Nouveau code : ([A-Z0-9]{4}-[A-Z0-9]{4})").Groups[1].Value;
        Assert.NotEmpty(code);
        Assert.True(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Vouchers.AnyAsync(v => v.Code == code.Replace("-", "") && v.BatchName == "Test")));
    }

    [Fact]
    public async Task Gate_page_authorises_a_device_with_anti_forgery_protection()
    {
        const string mac = "DA:A1:19:00:00:64";
        var client = Client();
        var page = await client.GetStringAsync("/admin/gate");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.Contains("Filtrage des paquets", WebUtility.HtmlDecode(page));

        var response = await client.PostAsync("/admin/gate?handler=Authorize", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["mac"] = mac, ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Devices.AnyAsync(d => d.Mac == mac && d.Whitelisted)));
    }
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

/// <summary>The customer side end to end: login pages, status page, logout, errors.</summary>
public sealed class PortalTests : IClassFixture<ServiceSmokeTests.Factory>, IAsyncLifetime
{
    private readonly ServiceSmokeTests.Factory _factory;

    public PortalTests(ServiceSmokeTests.Factory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().ApplyAsync(FakeAdapters.EthernetId, FakeAdapters.LanId, null);
        await _factory.Services.GetServices<IHostedService>().OfType<HotspotEngineService>().Single().TickAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A phone on the Wi-Fi: its own IP, MAC (in the neighbour table) and cookie jar.</summary>
    private sealed record Phone(string Ip, string Mac, HttpClient Http);

    private Phone NewPhone(int n, bool knownMac = true)
    {
        var ip = $"192.168.137.{100 + n}";
        var mac = $"DA:A1:19:00:01:{n:X2}";
        if (knownMac)
            _factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(ip), mac, NeighborState.Reachable));
        var http = _factory.CreateDefaultClient(new CookieContainerHandler(), new RemoteIpHandler(ip));
        return new Phone(ip, mac, http);
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<Voucher> VoucherAsync(string tariffName = "30 minutes") => InScope(async sp =>
    {
        var tariff = (await sp.GetRequiredService<TariffService>().ListAsync()).Single(t => t.Name == tariffName);
        return (await sp.GetRequiredService<VoucherService>().GenerateBatchAsync(tariff.Id, 1, "portal-test", false, null)).Single();
    });

    private static async Task<HttpResponseMessage> PostAsync(Phone phone, string pageUrl, string handler, Dictionary<string, string> fields)
    {
        var page = await phone.Http.GetStringAsync(pageUrl);
        fields["__RequestVerificationToken"] = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var path = pageUrl.Split('?')[0];
        return await phone.Http.PostAsync($"{path}?handler={handler}", new FormUrlEncodedContent(fields));
    }

    private static Task<HttpResponseMessage> LoginAsync(Phone phone, string code, string? url = null) =>
        PostAsync(phone, url is null ? "/portal" : $"/portal?url={Uri.EscapeDataString(url)}", "Voucher",
            new() { ["Code"] = code, ["url"] = url ?? "" });

    private static async Task<string> Html(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Login_page_shows_the_code_form_in_french_and_english()
    {
        var phone = NewPhone(1);
        var fr = await Html(await phone.Http.GetAsync("/portal"));
        Assert.Contains("Bienvenue sur le Wi-Fi de Stone's Hotspot", fr);
        Assert.Contains("Entrez le code de votre ticket", fr);
        Assert.Contains("conditions d'utilisation", fr);

        var en = await Html(await phone.Http.GetAsync("/portal?lang=en"));
        Assert.Contains("Enter the code from your ticket", en);
    }

    [Fact]
    public async Task Code_from_a_qr_link_is_prefilled()
    {
        var html = await Html(await NewPhone(2).Http.GetAsync("/portal?code=ABCD-EFGH"));
        Assert.Contains("value=\"ABCD-EFGH\"", html);
    }

    [Fact]
    public async Task Voucher_login_opens_internet_and_forwards_to_the_page_asked_for()
    {
        var phone = NewPhone(3);
        var voucher = await VoucherAsync();

        var response = await LoginAsync(phone, voucher.Code.ToLowerInvariant(), "http://www.example.com/news");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("/portal/status?", location);
        Assert.Contains("next=" + Uri.EscapeDataString("http://www.example.com/news"), location);

        var status = await Html(await phone.Http.GetAsync(location));
        Assert.Contains("Vous êtes connecté", status);
        Assert.Contains("<meta http-equiv=\"refresh\" content=\"3;url=http://www.example.com/news\">", status);
        Assert.Contains("30 minutes", status);              // plan
        Assert.Matches(@"(30 min 00 s|29 min \d\d s)", status);

        // The engine was woken: the phone passes the gate within a moment, without waiting for staff.
        var processor = _factory.Services.GetRequiredService<IPacketGate>().Processor;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (processor.Snapshot?.Authorized.ContainsKey(PacketProcessor.ToUInt32(IPAddress.Parse(phone.Ip))) != true && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(processor.Snapshot?.Authorized.ContainsKey(PacketProcessor.ToUInt32(IPAddress.Parse(phone.Ip))));
    }

    [Fact]
    public async Task Connectivity_check_urls_are_not_used_as_destination()
    {
        var phone = NewPhone(4);
        var response = await LoginAsync(phone, (await VoucherAsync()).Code, "http://connectivitycheck.gstatic.com/generate_204");

        Assert.DoesNotContain("next=", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Status_json_gives_the_live_countdown()
    {
        var phone = NewPhone(5);
        await LoginAsync(phone, (await VoucherAsync("1 heure")).Code);

        var json = JsonDocument.Parse(await phone.Http.GetStringAsync("/portal/status?handler=json&lang=en")).RootElement;

        Assert.True(json.GetProperty("active").GetBoolean());
        Assert.InRange(json.GetProperty("secondsLeft").GetInt64(), 3590, 3600);
        Assert.Matches(@"^(1 h 00 min|59 min \d\d s)$", json.GetProperty("timeLeft").GetString());
        Assert.Equal("0 kbit/s", json.GetProperty("down").GetString());
    }

    [Fact]
    public async Task Logging_out_ends_the_session_and_says_so()
    {
        var phone = NewPhone(6);
        await LoginAsync(phone, (await VoucherAsync()).Code);

        var response = await PostAsync(phone, "/portal/status", "Logout", []);

        Assert.Equal("/portal", response.Headers.Location!.ToString());
        Assert.Contains("Vous êtes déconnecté.", await Html(await phone.Http.GetAsync("/portal")));
        Assert.False(JsonDocument.Parse(await phone.Http.GetStringAsync("/portal/status?handler=json")).RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.Redirect, (await phone.Http.GetAsync("/portal/status")).StatusCode);
    }

    [Fact]
    public async Task When_time_runs_out_the_portal_explains_why()
    {
        var phone = NewPhone(7);
        await LoginAsync(phone, (await VoucherAsync()).Code);
        await InScope(async sp =>
        {
            var sessions = sp.GetRequiredService<SessionService>();
            var session = await sessions.GetActiveSessionForDeviceAsync(phone.Mac);
            return await sessions.EndAsync(session!.Id, SessionEndReason.QuotaUsed);
        });

        Assert.Contains("Votre temps ou vos données sont épuisés", await Html(await phone.Http.GetAsync("/portal")));
    }

    [Fact]
    public async Task Wrong_codes_show_an_error_then_the_lockout()
    {
        var phone = NewPhone(8);
        for (var i = 0; i < 4; i++)
            Assert.Contains("Code invalide", await Html(await LoginAsync(phone, "WRONG00" + i)));

        Assert.Contains("Trop d'essais. Réessayez dans 5 minute(s).", await Html(await LoginAsync(phone, "WRONG005")));
    }

    [Fact]
    public async Task Code_in_use_on_too_many_devices_is_refused_with_a_clear_message()
    {
        var voucher = await VoucherAsync(); // 1 device
        await LoginAsync(NewPhone(9), voucher.Code);

        var html = await Html(await LoginAsync(NewPhone(10), voucher.Code));

        Assert.Contains("déjà utilisé sur le nombre maximum d'appareils", html);
    }

    [Fact]
    public async Task Account_login_shows_the_account_and_its_balance()
    {
        var phone = NewPhone(11);
        await InScope(async sp =>
        {
            var tariff = (await sp.GetRequiredService<TariffService>().ListAsync()).First();
            var accounts = sp.GetRequiredService<AccountService>();
            var account = await accounts.CreateAsync(new UserAccount { Username = "mireille", TariffId = tariff.Id }, "secret1", null);
            var admin = await sp.GetRequiredService<HotspotDbContext>().Staff.FirstAsync();
            return await accounts.DepositAsync(account.Id, new Xaf(1000), PaymentMethod.Cash, admin);
        });

        var wrong = await PostAsync(phone, "/portal", "Account", new() { ["Username"] = "mireille", ["Password"] = "nope" });
        var wrongHtml = await Html(wrong);
        Assert.Contains("Nom d'utilisateur ou mot de passe incorrect.", wrongHtml);
        Assert.DoesNotContain("nope", wrongHtml);

        var ok = await PostAsync(phone, "/portal", "Account", new() { ["Username"] = "mireille", ["Password"] = "secret1" });
        var status = await Html(await phone.Http.GetAsync(ok.Headers.Location!.ToString()));
        Assert.Contains("mireille", status);
        Assert.Contains("900 FCFA", status.Replace(' ', ' ')); // 1 000 − the 100 FCFA package
    }

    [Fact]
    public async Task Already_connected_device_goes_straight_to_its_status()
    {
        var phone = NewPhone(12);
        await LoginAsync(phone, (await VoucherAsync()).Code);

        var response = await phone.Http.GetAsync("/portal");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/portal/status", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Unidentified_device_is_asked_to_reconnect()
    {
        var phone = NewPhone(13, knownMac: false);
        Assert.Contains("Votre appareil n'a pas pu être identifié", await Html(await LoginAsync(phone, (await VoucherAsync()).Code)));
    }

    [Fact]
    public async Task Status_page_never_forwards_to_a_script_link()
    {
        var phone = NewPhone(14);
        await LoginAsync(phone, (await VoucherAsync()).Code);

        var html = await phone.Http.GetStringAsync("/portal/status?welcome=1&next=javascript%3Aalert(1)");

        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("http-equiv=\"refresh\"", html);
    }

    [Fact]
    public async Task Terms_page_shows_the_default_terms()
    {
        var html = await Html(await NewPhone(15).Http.GetAsync("/portal/terms?lang=en"));
        Assert.Contains("Terms of use", html);
        Assert.Contains("your device's address (MAC)", html);
    }

    [Fact]
    public async Task Stopped_licence_shows_service_unavailable_and_refuses_logins()
    {
        var phone = NewPhone(16);
        var voucher = await VoucherAsync();
        var page = await phone.Http.GetStringAsync("/portal"); // token while still allowed
        _factory.Licence.HotspotAllowed = false;
        try
        {
            Assert.Contains("Service temporairement indisponible", await Html(await phone.Http.GetAsync("/portal")));
            var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
            var post = await phone.Http.PostAsync("/portal?handler=Voucher", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Code"] = voucher.Code, ["__RequestVerificationToken"] = token,
            }));
            Assert.Contains("Service temporairement indisponible", await Html(post));
            Assert.False(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Sessions.AnyAsync(s => s.VoucherId == voucher.Id)));
        }
        finally
        {
            _factory.Licence.HotspotAllowed = true;
        }
    }

    [Fact]
    public async Task Uploaded_logo_is_served_and_shown()
    {
        var paths = _factory.Services.GetRequiredService<AppPaths>();
        await File.WriteAllBytesAsync(Path.Combine(paths.Branding, "logo.png"), [0x89, 0x50, 0x4E, 0x47]);
        try
        {
            var phone = NewPhone(17);
            var logo = await phone.Http.GetAsync("/portal/logo");
            Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<img src=\"/portal/logo\"", await phone.Http.GetStringAsync("/portal"));
        }
        finally
        {
            File.Delete(Path.Combine(paths.Branding, "logo.png"));
        }
    }
}

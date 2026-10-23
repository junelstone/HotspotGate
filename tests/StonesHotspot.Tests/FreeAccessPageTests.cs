using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.StaffSignIn;

namespace StonesHotspot.Tests;

/// <summary>Free access, password of the day and SMS login through the real portal and settings pages.</summary>
public sealed class FreeAccessPageTests(ServiceSmokeTests.Factory factory) : IClassFixture<ServiceSmokeTests.Factory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().ApplyAsync(FakeAdapters.EthernetId, FakeAdapters.LanId, null);
        await factory.Services.GetServices<IHostedService>().OfType<HotspotEngineService>().Single().TickAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Phone(int n)
    {
        var ip = $"192.168.137.{150 + n}";
        factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(ip), $"DA:A1:19:00:08:{n:X2}", NeighborState.Reachable));
        return factory.CreateDefaultClient(new CookieContainerHandler(), new RemoteIpHandler(ip));
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task Configure(params (string Key, string Value)[] values) => InScope(async sp =>
    {
        var settings = sp.GetRequiredService<SettingsService>();
        foreach (var (key, value) in values)
            await settings.SetAsync(key, value, null);
        return 0;
    });

    private Task<int> PlanIdAsync() => InScope(async sp => (await sp.GetRequiredService<TariffService>().ListAsync()).Single(t => t.Name == "30 minutes").Id);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient http, string handler, Dictionary<string, string> fields)
    {
        var page = await http.GetStringAsync("/portal");
        fields["__RequestVerificationToken"] = TokenRegex.Match(page).Groups[1].Value;
        return await http.PostAsync($"/portal?handler={handler}", new FormUrlEncodedContent(fields));
    }

    private static async Task<string> Html(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Free_access_needs_the_terms_ticked()
    {
        var plan = await PlanIdAsync();
        var phone = Phone(1);
        Assert.DoesNotContain("Accès gratuit", await phone.GetStringAsync("/portal"));

        await Configure((SettingKeys.FreeEnabled, "true"), (SettingKeys.FreeTariffId, $"{plan}"));
        try
        {
            Assert.Contains("Accès gratuit", WebUtility.HtmlDecode(await phone.GetStringAsync("/portal")));
            Assert.Contains("Cochez la case", await Html(await PostAsync(phone, "Free", new())));

            var ok = await PostAsync(phone, "Free", new() { ["AcceptTerms"] = "true" });
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            var status = await Html(await phone.GetAsync(ok.Headers.Location!.OriginalString));
            Assert.Contains("30 minutes", status);
            Assert.DoesNotContain("Code d'accès", status);   // the hidden voucher's code is not shown

            factory.Licence.Edition = Edition.Basic;
            try
            {
                Assert.DoesNotContain("Accès gratuit", WebUtility.HtmlDecode(await Phone(2).GetStringAsync("/portal")));
            }
            finally
            {
                factory.Licence.Edition = Edition.Trial;
            }
        }
        finally
        {
            await Configure((SettingKeys.FreeEnabled, "false"));
        }
    }

    [Fact]
    public async Task Password_of_the_day_is_shown_to_staff_and_lets_customers_in()
    {
        var plan = await PlanIdAsync();
        await Configure((SettingKeys.PasswordEnabled, "true"), (SettingKeys.PasswordTariffId, $"{plan}"), (SettingKeys.PasswordDaily, "true"));
        try
        {
            var cashier = await AsAsync(factory, StaffRole.Cashier);
            var home = await Html(await cashier.Http.GetAsync("/admin"));
            var password = Regex.Match(home, "Mot de passe Wi-Fi du jour : <strong[^>]*>([A-Z0-9]{6})</strong>").Groups[1].Value;
            Assert.Equal(6, password.Length);

            var phone = Phone(3);
            var wrong = await Html(await PostAsync(phone, "Password", new() { ["SharedPassword"] = "NOPE42" }));
            Assert.Contains("Mot de passe incorrect", wrong);
            Assert.DoesNotContain("NOPE42", wrong);   // never echoed back
            var ok = await PostAsync(phone, "Password", new() { ["SharedPassword"] = password.ToLowerInvariant() });
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            Assert.Equal(LoginMethod.SharedPassword, await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Sessions
                .Where(s => s.Device.Mac == "DA:A1:19:00:08:03").Select(s => s.Method).SingleAsync()));
        }
        finally
        {
            await Configure((SettingKeys.PasswordEnabled, "false"));
        }
    }

    [Fact]
    public async Task Sms_login_end_to_end()
    {
        var plan = await PlanIdAsync();
        await Configure((SettingKeys.SmsEnabled, "true"), (SettingKeys.SmsTariffId, $"{plan}"), (SettingKeys.SmsTransport, "http"));
        await InScope(async sp =>
        {
            await sp.GetRequiredService<StonesHotspot.Core.Sms.SmsService>().SaveProviderAsync("https://sms.example/send?to={phone}&text={message}", null, null);
            return 0;
        });
        try
        {
            var phone = Phone(4);
            Assert.Contains("Numéro invalide", await Html(await PostAsync(phone, "SmsSend", new() { ["Phone"] = "12" })));

            var sent = await Html(await PostAsync(phone, "SmsSend", new() { ["Phone"] = "06 777 88 99" }));
            Assert.Contains("Code envoyé au +242 06 777 88 99", sent);
            Assert.Contains("value=\"+242067778899\"", sent);   // the code form keeps the number
            var code = factory.Sms.LastCode("+242067778899");

            var ok = await PostAsync(phone, "SmsVerify", new() { ["Phone"] = "+242067778899", ["SmsCode"] = code });
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            var voucher = await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Vouchers.SingleAsync(v => v.Phone == "+242067778899"));
            Assert.Equal(VoucherSource.Sms, voucher.Source);
        }
        finally
        {
            await Configure((SettingKeys.SmsEnabled, "false"));
        }
    }

    [Fact]
    public async Task Settings_page_saves_keeps_the_provider_secret_and_sends_a_test()
    {
        var plan = await PlanIdAsync();
        var admin = await AsAsync(factory);
        var saved = await PostFormAsync(admin.Http, "/admin/access", "/admin/access", new()
        {
            ["FreeEnabled"] = "true", ["FreeTariffId"] = $"{plan}", ["FreePerDay"] = "2",
            ["PasswordPerDay"] = "3", ["SmsPerDay"] = "1", ["SmsPerNumber"] = "3", ["SmsPerDevice"] = "3", ["SmsTotal"] = "50",
            ["SmsPrefixes"] = "06, 05", ["Transport"] = "http", ["ModemBaud"] = "115200",
            ["HttpUrl"] = "https://api.sms-provider.example/send?apikey=TOPSECRET99&to={phone}&text={message}",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        try
        {
            var page = await Html(await admin.Http.GetAsync("/admin/access"));
            Assert.DoesNotContain("TOPSECRET99", page);
            Assert.Contains("api.sms-provider.example", page);
            Assert.Contains("value=\"+24206, +24205\"", page);
            var stored = await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Settings.SingleAsync(s => s.Key == SettingKeys.SmsHttpUrl));
            Assert.DoesNotContain("TOPSECRET99", stored.Value);

            // Saving again without typing the address keeps it.
            await PostFormAsync(admin.Http, "/admin/access", "/admin/access", new()
            {
                ["FreePerDay"] = "2", ["PasswordPerDay"] = "3", ["SmsPerDay"] = "1", ["SmsPerNumber"] = "3", ["SmsPerDevice"] = "3",
                ["SmsTotal"] = "50", ["Transport"] = "http", ["ModemBaud"] = "115200",
            });
            Assert.Contains("api.sms-provider.example", await Html(await admin.Http.GetAsync("/admin/access")));

            var test = await PostFormAsync(admin.Http, "/admin/access", "/admin/access?handler=Test", new() { ["TestPhone"] = "05 111 22 33" });
            Assert.Equal(HttpStatusCode.Redirect, test.StatusCode);
            Assert.Contains(factory.Sms.Sent, s => s.Phone == "+242051112233" && s.Text.Contains("SMS de test"));
            Assert.Contains("SMS de test envoyé", await Html(await admin.Http.GetAsync("/admin/access")));

            var cashier = await AsAsync(factory, StaffRole.Cashier);
            Assert.Contains("/admin/denied", (await cashier.Http.GetAsync("/admin/access")).Headers.Location?.OriginalString ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await Configure((SettingKeys.FreeEnabled, "false"), (SettingKeys.SmsTransport, "none"));
        }
    }
}

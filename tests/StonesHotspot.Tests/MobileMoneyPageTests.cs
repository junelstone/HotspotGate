using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.StaffSignIn;

namespace StonesHotspot.Tests;

/// <summary>Buying a plan with Mobile Money through the portal, the operator callback, the settings page and the report.</summary>
public sealed class MobileMoneyPageTests(ServiceSmokeTests.Factory factory) : IClassFixture<ServiceSmokeTests.Factory>, IAsyncLifetime
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
        var ip = $"192.168.137.{170 + n}";
        factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(ip), $"DA:A1:19:00:09:{n:X2}", NeighborState.Reachable));
        return factory.CreateDefaultClient(new CookieContainerHandler(), new RemoteIpHandler(ip));
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static async Task<string> Html(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    /// <summary>Turns MTN on through the admin page (keys typed like the owner would).</summary>
    private async Task<SignedIn> EnableMtnAsync()
    {
        var admin = await AsAsync(factory);
        var saved = await PostFormAsync(admin.Http, "/admin/payments", "/admin/payments", new()
        {
            ["MtnEnabled"] = "true", ["MtnBaseUrl"] = "https://sandbox.momodeveloper.mtn.com", ["MtnTarget"] = "sandbox", ["MtnCurrency"] = "eur",
            ["MtnApiUser"] = "3f1c-user", ["MtnSubscriptionKey"] = "SUBKEY-SECRET-1", ["MtnApiKey"] = "APIKEY-SECRET-2", ["MtnPrefixes"] = "06",
            ["AirtelBaseUrl"] = "https://openapiuat.airtel.africa", ["AirtelCountry"] = "CG", ["AirtelCurrency"] = "XAF", ["TimeoutSeconds"] = "180",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        return admin;
    }

    private static async Task<HttpResponseMessage> BuyAsync(HttpClient phone, int tariffId, string number)
    {
        var page = await phone.GetStringAsync("/portal/pay");
        return await phone.PostAsync("/portal/pay", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TariffId"] = $"{tariffId}", ["Method"] = "MtnMoMo", ["Phone"] = number,
            ["__RequestVerificationToken"] = TokenRegex.Match(page).Groups[1].Value,
        }));
    }

    private Task<int> PlanAsync() => InScope(async sp => (await sp.GetRequiredService<TariffService>().ListAsync()).Single(t => t.Name == "1 heure").Id);

    [Fact]
    public async Task Customer_buys_a_plan_and_waits_for_approval()
    {
        var admin = await EnableMtnAsync();
        var settingsPage = await Html(await admin.Http.GetAsync("/admin/payments"));
        Assert.DoesNotContain("APIKEY-SECRET-2", settingsPage);
        Assert.DoesNotContain("SUBKEY-SECRET-1", settingsPage);
        Assert.Contains("MTN MoMo ✓", settingsPage);
        var test = await PostFormAsync(admin.Http, "/admin/payments", "/admin/payments?handler=Test&method=MtnMoMo", new());
        Assert.Equal(HttpStatusCode.Redirect, test.StatusCode);
        Assert.Contains("connexion réussie", await Html(await admin.Http.GetAsync("/admin/payments")));

        var phone = Phone(1);
        Assert.Contains("Acheter un forfait avec Mobile Money", WebUtility.HtmlDecode(await phone.GetStringAsync("/portal")));
        Assert.Contains("n'est pas un numéro MTN MoMo", await Html(await BuyAsync(phone, await PlanAsync(), "05 555 12 34")));

        var started = await BuyAsync(phone, await PlanAsync(), "06 444 55 66");
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        var waitUrl = started.Headers.Location!.OriginalString;
        Assert.StartsWith("/portal/pay/wait?ref=", waitUrl);
        var reference = Uri.UnescapeDataString(waitUrl.Split("ref=")[1]);
        Assert.Equal("+242064445566", factory.Payments.Requests.Single(r => r.Reference == reference).Phone);

        var waiting = await Html(await phone.GetAsync(waitUrl));
        Assert.Contains("Validez le paiement sur votre téléphone", waiting);
        Assert.Contains("http-equiv=\"refresh\"", waiting);
        // Another device cannot look at this payment (nor its code).
        Assert.Equal(HttpStatusCode.Redirect, (await Phone(2).GetAsync(waitUrl)).StatusCode);

        factory.Payments.Pay(reference, "MTN-TX-77");
        await Task.Delay(3100);   // the page asks the operator at most every 3 s
        var done = await Html(await phone.GetAsync(waitUrl));
        Assert.Contains("Paiement reçu, vous êtes connecté", done);
        var code = await InScope(sp => sp.GetRequiredService<HotspotDbContext>().MobilePayments.Include(p => p.Voucher)
            .Where(p => p.Reference == reference).Select(p => p.Voucher!.Code).SingleAsync());
        Assert.Contains(StonesHotspot.Core.Rules.VoucherCodeGenerator.Format(code), done);
        Assert.Contains("1 heure", await Html(await phone.GetAsync("/portal/status")));

        // The sale shows in the reports as MTN MoMo, and in the Mobile Money report with the operator's id.
        var report = await Html(await admin.Http.GetAsync("/admin/reports?r=mobilemoney"));
        Assert.Contains("MTN-TX-77", report);
        var csv = Encoding.UTF8.GetString(await admin.Http.GetByteArrayAsync("/admin/reports?handler=Csv&r=transactions"));
        Assert.Contains("MTN MoMo", csv);
    }

    [Fact]
    public async Task Operator_callback_completes_a_payment_after_asking_the_operator()
    {
        await EnableMtnAsync();
        var phone = Phone(3);
        var started = await BuyAsync(phone, await PlanAsync(), "06 333 22 11");
        var reference = Uri.UnescapeDataString(started.Headers.Location!.OriginalString.Split("ref=")[1]);
        var http = factory.CreateClient();

        // A forged "successful" callback: the operator still says pending, so nothing happens.
        var forged = await http.PutAsync("/api/payments/mtn", new StringContent($$"""{"externalId":"{{reference}}","status":"SUCCESSFUL"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
        Assert.Equal(MobilePaymentStatus.Pending, await StatusAsync(reference));

        factory.Payments.Pay(reference, "CB-1");
        await Task.Delay(3100);
        await http.PutAsync("/api/payments/mtn", new StringContent($$"""{"externalId":"{{reference}}"}""", Encoding.UTF8, "application/json"));
        // The callback or the background job (whichever claims it first) completes it.
        for (var i = 0; i < 50 && await StatusAsync(reference) != MobilePaymentStatus.Successful; i++)
            await Task.Delay(100);
        Assert.Equal(MobilePaymentStatus.Successful, await StatusAsync(reference));
        Assert.Equal(1, await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Transactions.CountAsync(t => t.ExternalReference == "CB-1")));

        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/api/payments/airtel", new StringContent("not json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync("/api/payments/other", new StringContent("{}"))).StatusCode);
    }

    private Task<MobilePaymentStatus> StatusAsync(string reference) =>
        InScope(sp => sp.GetRequiredService<HotspotDbContext>().MobilePayments.Where(p => p.Reference == reference).Select(p => p.Status).SingleAsync());

    [Fact]
    public async Task Cashiers_cannot_open_the_mobile_money_settings()
    {
        var cashier = await AsAsync(factory, StaffRole.Cashier);
        Assert.Contains("/admin/denied", (await cashier.Http.GetAsync("/admin/payments")).Headers.Location?.OriginalString ?? "", StringComparison.OrdinalIgnoreCase);
    }
}

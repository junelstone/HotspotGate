using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Payments;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class MobileMoneyTests
{
    private const string MtnNumber = "06 123 45 67";
    private static readonly FakeSecretProtector Protector = new();

    private static async Task<(TestHost Host, Tariff Plan)> HostAsync(bool sms = false)
    {
        var host = await TestHost.CreateAsync();
        var plan = await host.CreateTariffAsync(price: 500, name: "1 heure web");
        (string, string)[] values =
        [
            (SettingKeys.MtnEnabled, "true"), (SettingKeys.MtnApiUser, "user"),
            (SettingKeys.MtnSubscriptionKey, Protector.Protect("sub")), (SettingKeys.MtnApiKey, Protector.Protect("key")),
            (SettingKeys.AirtelEnabled, "true"), (SettingKeys.AirtelClientId, "client"), (SettingKeys.AirtelClientSecret, Protector.Protect("secret")),
        ];
        foreach (var (key, value) in values)
            await host.Run<SettingsService>(s => s.SetAsync(key, value, null));
        if (sms)
        {
            await host.Run<SettingsService>(s => s.SetAsync(SettingKeys.SmsTransport, "http", null));
            await host.Run<StonesHotspot.Core.Sms.SmsService>(s => s.SaveProviderAsync("https://sms.example/send?to={phone}&text={message}", null, null));
        }
        return (host, plan);
    }

    private static Task<PayStartResult> StartAsync(TestHost host, Tariff plan, string phone = MtnNumber, PaymentMethod method = PaymentMethod.MtnMoMo, string mac = TestHost.PhoneMac) =>
        host.Run<MobileMoneyService, PayStartResult>(s => s.StartAsync(mac, "192.168.137.10", plan.Id, method, phone));

    private static Task<MobilePayment?> CheckAsync(TestHost host, string reference, bool force = false) =>
        host.Run<MobileMoneyService, MobilePayment?>(s => s.CheckAsync(reference, force));

    [Fact]
    public async Task Paid_plan_gives_a_code_a_sale_a_login_and_an_sms()
    {
        var (host, plan) = await HostAsync(sms: true);
        await using var _ = host;

        var start = await StartAsync(host, plan);
        Assert.Equal(PayStartStatus.Started, start.Status);
        var request = host.Payments.Last;
        Assert.Equal(("+242061234567", 500L, start.Reference), (request.Phone, request.Amount.Amount, request.Reference));
        Assert.Contains("1 heure web", request.Description);
        Assert.Null(request.CallbackUrl);
        Assert.Equal(MobilePaymentStatus.Pending, (await CheckAsync(host, start.Reference!))!.Status);

        host.Payments.Pay(start.Reference!, "MP260302.0800.A12345", 500m);
        host.Advance(TimeSpan.FromSeconds(5));
        var paid = (await CheckAsync(host, start.Reference!))!;

        Assert.Equal(MobilePaymentStatus.Successful, paid.Status);
        Assert.Equal("MP260302.0800.A12345", paid.OperatorTransactionId);
        Assert.Equal((VoucherSource.MobileMoney, VoucherStatus.Active, "+242061234567"), (paid.Voucher!.Source, paid.Voucher.Status, paid.Voucher.Phone));
        var sale = await host.Query(db => db.Transactions.SingleAsync());
        Assert.Equal((TransactionType.VoucherSale, PaymentMethod.MtnMoMo, 500L, "MP260302.0800.A12345", (int?)null),
            (sale.Type, sale.PaymentMethod, sale.Amount.Amount, sale.ExternalReference, sale.StaffId));
        Assert.Equal(paid.ReceiptNumber, sale.ReceiptNumber);
        var session = await host.Query(db => db.Sessions.SingleAsync(s => s.EndedAt == null));
        Assert.Equal((LoginMethod.MobileMoney, paid.VoucherId), (session.Method, session.VoucherId));
        var (phone, text) = host.Sms.Sent.Single();
        Assert.Equal("+242061234567", phone);
        Assert.Contains(StonesHotspot.Core.Rules.VoucherCodeGenerator.Format(paid.Voucher.Code), text);
    }

    [Fact]
    public async Task A_payment_completes_once_even_with_concurrent_checks()
    {
        var (host, plan) = await HostAsync();
        await using var _ = host;
        var reference = (await StartAsync(host, plan)).Reference!;
        host.Payments.Pay(reference);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CheckAsync(host, reference, force: true)));
        await CheckAsync(host, reference, force: true);   // a late duplicate callback

        Assert.Equal(1, await host.Query(db => db.Vouchers.CountAsync()));
        Assert.Equal(1, await host.Query(db => db.Transactions.CountAsync()));
        Assert.Equal(MobilePaymentStatus.Successful, (await host.Query(db => db.MobilePayments.SingleAsync())).Status);
    }

    [Fact]
    public async Task Declined_payment_creates_nothing()
    {
        var (host, plan) = await HostAsync();
        await using var _ = host;
        var reference = (await StartAsync(host, plan)).Reference!;
        host.Payments.Decline(reference, "APPROVAL_REJECTED");

        var payment = (await CheckAsync(host, reference))!;

        Assert.Equal((MobilePaymentStatus.Failed, "APPROVAL_REJECTED"), (payment.Status, payment.FailureReason));
        Assert.Equal(0, await host.Query(db => db.Vouchers.CountAsync()));
        Assert.Equal(0, await host.Query(db => db.Transactions.CountAsync()));
    }

    [Fact]
    public async Task Timed_out_payment_can_still_succeed_later()
    {
        var (host, plan) = await HostAsync();
        await using var _ = host;
        var reference = (await StartAsync(host, plan)).Reference!;
        host.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(MobilePaymentStatus.TimedOut, (await CheckAsync(host, reference))!.Status);

        // The background job leaves timed-out payments alone; staff "Vérifier" (or a callback) asks again.
        Assert.Equal(0, await host.Run<MobileMoneyService, int>(s => s.CheckPendingAsync()));
        host.Payments.Pay(reference, "LATE-1");
        var late = (await CheckAsync(host, reference, force: true))!;
        Assert.Equal((MobilePaymentStatus.Successful, "LATE-1"), (late.Status, late.OperatorTransactionId));
        Assert.NotNull(late.Voucher);
    }

    [Fact]
    public async Task Wrong_amount_or_operator_error_fails_the_payment()
    {
        var (host, plan) = await HostAsync();
        await using var _ = host;
        var reference = (await StartAsync(host, plan)).Reference!;
        host.Payments.Pay(reference, amount: 5m);
        var wrong = (await CheckAsync(host, reference))!;
        Assert.Equal(MobilePaymentStatus.Failed, wrong.Status);
        Assert.Contains("differs", wrong.FailureReason);
        Assert.Equal(0, await host.Query(db => db.Vouchers.CountAsync()));

        host.Payments.RequestError = "MTN MoMo refused the payment request: HTTP 500";
        var error = await StartAsync(host, plan, mac: TestHost.LaptopMac);
        Assert.Equal(PayStartStatus.OperatorError, error.Status);
        Assert.Equal(MobilePaymentStatus.Failed, (await host.Run<MobileMoneyService, MobilePayment?>(s => s.FindAsync(error.Reference!)))!.Status);
    }

    [Fact]
    public async Task Requests_are_checked_and_limited()
    {
        var (host, plan) = await HostAsync();
        await using var _ = host;

        Assert.Equal(PayStartStatus.InvalidNumber, (await StartAsync(host, plan, "123")).Status);
        Assert.Equal(PayStartStatus.WrongOperator, (await StartAsync(host, plan, "05 555 12 34")).Status);   // Airtel number on MTN
        Assert.Equal(PayStartStatus.NotAvailable, (await StartAsync(host, plan, method: PaymentMethod.Cash)).Status);

        var first = await StartAsync(host, plan);
        var again = await StartAsync(host, plan);
        Assert.Equal((PayStartStatus.AlreadyPending, first.Reference), (again.Status, again.Reference));
        Assert.Single(host.Payments.Requests);

        for (var i = 0; i < 4; i++)
        {
            host.Advance(TimeSpan.FromMinutes(4));   // each previous one has timed out
            await CheckAsync(host, host.Payments.Last.Reference);
            Assert.Equal(PayStartStatus.Started, (await StartAsync(host, plan)).Status);
        }
        host.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(PayStartStatus.TooMany, (await StartAsync(host, plan)).Status);   // 5 per hour
        Assert.Equal(PayStartStatus.Started, (await StartAsync(host, plan, "05 555 12 34", PaymentMethod.AirtelMoney, TestHost.LaptopMac)).Status);

        var other = await host.CreateTariffAsync(price: 1000, name: "Jour");
        await host.Run<SettingsService>(s => s.SetAsync(SettingKeys.PayTariffs, $"{other.Id}", null));
        Assert.Equal(PayStartStatus.PlanNotSold, (await StartAsync(host, plan, mac: TestHost.TabletMac)).Status);
    }

    [Fact]
    public async Task Operator_is_not_asked_more_than_every_few_seconds()
    {
        var (host, plan) = await HostAsync();
        await using var _ = host;
        var reference = (await StartAsync(host, plan)).Reference!;
        await CheckAsync(host, reference);
        await CheckAsync(host, reference);
        Assert.Equal(1, host.Payments.StatusCalls);
        host.Advance(TimeSpan.FromSeconds(4));
        await CheckAsync(host, reference);
        Assert.Equal(2, host.Payments.StatusCalls);
    }

    // ---- Operator clients against a fake HTTP server ----

    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];
        public Func<HttpRequestMessage, (HttpStatusCode, string)> Answer { get; set; } = _ => (HttpStatusCode.OK, "{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            var (status, body) = Answer(request);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static PaymentSettings Settings(string mtnBase = "https://sandbox.momodeveloper.mtn.com", string airtelBase = "https://openapiuat.airtel.africa") => new(
        new MtnSettings(true, mtnBase, "sandbox", "EUR", "SUBKEY", "API-USER", "API-KEY", ["+24206"]),
        new AirtelSettings(true, airtelBase, "CLIENT", "SECRET", "CG", "XAF", ["+24205"]),
        [], null, TimeSpan.FromMinutes(3));

    [Fact]
    public async Task Mtn_client_speaks_the_collection_api()
    {
        var api = new FakeApi
        {
            Answer = r => r.RequestUri!.AbsolutePath switch
            {
                "/collection/token/" => (HttpStatusCode.OK, """{"access_token":"TOKEN1","token_type":"access_token","expires_in":3600}"""),
                "/collection/v1_0/requesttopay" => (HttpStatusCode.Accepted, ""),
                var p when p.EndsWith("/ok", StringComparison.Ordinal) => (HttpStatusCode.OK, """{"amount":"500","currency":"EUR","financialTransactionId":"123456","status":"SUCCESSFUL"}"""),
                var p when p.EndsWith("/no", StringComparison.Ordinal) => (HttpStatusCode.OK, """{"status":"FAILED","reason":{"code":"APPROVAL_REJECTED","message":"x"}}"""),
                _ => (HttpStatusCode.OK, """{"status":"PENDING"}"""),
            },
        };
        var factory = new HttpPaymentProviderFactory(new HttpClient(api), TimeProvider.System);
        var mtn = factory.Create(PaymentMethod.MtnMoMo, Settings())!;
        var reference = mtn.NewReference();
        Assert.True(Guid.TryParse(reference, out _));

        await mtn.RequestToPayAsync(new PaymentRequest(reference, "+242061234567", new(500), "Wi-Fi 1 heure", "https://wifi.example/api/payments/mtn"), CancellationToken.None);
        var token = api.Calls[0].Request;
        Assert.Equal(HttpMethod.Post, token.Method);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("API-USER:API-KEY")), token.Headers.Authorization!.ToString());
        Assert.Equal("SUBKEY", token.Headers.GetValues("Ocp-Apim-Subscription-Key").Single());
        var (pay, body) = api.Calls[1];
        Assert.Equal("Bearer TOKEN1", pay.Headers.Authorization!.ToString());
        Assert.Equal(reference, pay.Headers.GetValues("X-Reference-Id").Single());
        Assert.Equal("sandbox", pay.Headers.GetValues("X-Target-Environment").Single());
        Assert.Equal("https://wifi.example/api/payments/mtn", pay.Headers.GetValues("X-Callback-Url").Single());
        var json = JsonDocument.Parse(body!).RootElement;
        Assert.Equal(("500", "EUR", "242061234567"), (json.GetProperty("amount").GetString(), json.GetProperty("currency").GetString(),
            json.GetProperty("payer").GetProperty("partyId").GetString()));

        var ok = await mtn.GetStatusAsync("ok", CancellationToken.None);
        Assert.Equal((OperatorPaymentState.Successful, "123456", 500m), (ok.State, ok.OperatorTransactionId, ok.Amount));
        var no = await mtn.GetStatusAsync("no", CancellationToken.None);
        Assert.Equal((OperatorPaymentState.Failed, "APPROVAL_REJECTED"), (no.State, no.Reason));
        Assert.Equal(OperatorPaymentState.Pending, (await mtn.GetStatusAsync("wait", CancellationToken.None)).State);
        Assert.Single(api.Calls, c => c.Request.RequestUri!.AbsolutePath == "/collection/token/");   // token reused

        api.Answer = _ => (HttpStatusCode.Unauthorized, """{"message":"Access denied"}""");
        var refused = factory.Create(PaymentMethod.MtnMoMo, Settings("https://other.example"))!;
        Assert.Contains("401", (await Assert.ThrowsAsync<PaymentException>(() => refused.TestConnectionAsync(CancellationToken.None))).Message);
    }

    [Fact]
    public async Task Airtel_client_speaks_the_collections_api()
    {
        var api = new FakeApi
        {
            Answer = r => r.RequestUri!.AbsolutePath switch
            {
                "/auth/oauth2/token" => (HttpStatusCode.OK, """{"access_token":"AT","expires_in":"180","token_type":"bearer"}"""),
                "/merchant/v1/payments/" => (HttpStatusCode.OK, """{"data":{"transaction":{"id":"x","status":"Success."}},"status":{"code":"200","message":"SUCCESS","success":true}}"""),
                var p when p.EndsWith("/ok", StringComparison.Ordinal) => (HttpStatusCode.OK, """{"data":{"transaction":{"airtel_money_id":"CI210101.1234.A00001","id":"ok","status":"TS"}},"status":{"success":true}}"""),
                var p when p.EndsWith("/no", StringComparison.Ordinal) => (HttpStatusCode.OK, """{"data":{"transaction":{"id":"no","message":"Insufficient funds","status":"TF"}},"status":{"success":true}}"""),
                _ => (HttpStatusCode.OK, """{"data":{"transaction":{"id":"w","status":"TIP"}},"status":{"success":true}}"""),
            },
        };
        var airtel = new HttpPaymentProviderFactory(new HttpClient(api), TimeProvider.System).Create(PaymentMethod.AirtelMoney, Settings())!;
        var reference = airtel.NewReference();
        Assert.Matches("^SH[0-9A-F]{18}$", reference);

        await airtel.RequestToPayAsync(new PaymentRequest(reference, "+242051234567", new(1000), "Wi-Fi Jour", null), CancellationToken.None);
        var token = JsonDocument.Parse(api.Calls[0].Body!).RootElement;
        Assert.Equal(("CLIENT", "SECRET", "client_credentials"), (token.GetProperty("client_id").GetString(), token.GetProperty("client_secret").GetString(), token.GetProperty("grant_type").GetString()));
        var (pay, body) = api.Calls[1];
        Assert.Equal(("Bearer AT", "CG", "XAF"), (pay.Headers.Authorization!.ToString(), pay.Headers.GetValues("X-Country").Single(), pay.Headers.GetValues("X-Currency").Single()));
        var json = JsonDocument.Parse(body!).RootElement;
        Assert.Equal("051234567", json.GetProperty("subscriber").GetProperty("msisdn").GetString());
        Assert.Equal((1000, reference), (json.GetProperty("transaction").GetProperty("amount").GetInt32(), json.GetProperty("transaction").GetProperty("id").GetString()));

        Assert.Equal((OperatorPaymentState.Successful, "CI210101.1234.A00001"), ((await airtel.GetStatusAsync("ok", CancellationToken.None)) is var ok ? (ok.State, ok.OperatorTransactionId) : default));
        Assert.Equal((OperatorPaymentState.Failed, "Insufficient funds"), ((await airtel.GetStatusAsync("no", CancellationToken.None)) is var no ? (no.State, no.Reason) : default));
        Assert.Equal(OperatorPaymentState.Pending, (await airtel.GetStatusAsync("w", CancellationToken.None)).State);

        api.Answer = r => r.RequestUri!.AbsolutePath == "/merchant/v1/payments/"
            ? (HttpStatusCode.OK, """{"status":{"code":"400","message":"Invalid msisdn","success":false}}""")
            : (HttpStatusCode.OK, """{"access_token":"AT","expires_in":180}""");
        Assert.Contains("Invalid msisdn", (await Assert.ThrowsAsync<PaymentException>(() =>
            airtel.RequestToPayAsync(new PaymentRequest(airtel.NewReference(), "+242051234567", new(500), "x", null), CancellationToken.None))).Message);
    }

    [Fact]
    public async Task Mobile_money_settings_keep_keys_encrypted()
    {
        var (host, _) = await HostAsync();
        await using var __ = host;
        var stored = await host.Query(db => db.Settings.SingleAsync(s => s.Key == SettingKeys.MtnApiKey));
        Assert.NotEqual("key", stored.Value);
        var settings = await host.Run<MobileMoneyService, PaymentSettings>(s => s.GetSettingsAsync());
        Assert.True(settings.Mtn.Configured && settings.Airtel.Configured);
        Assert.Equal(("EUR", "sandbox", "https://sandbox.momodeveloper.mtn.com"), (settings.Mtn.Currency, settings.Mtn.TargetEnvironment, settings.Mtn.BaseUrl));
        Assert.Equal(TimeSpan.FromMinutes(3), settings.Timeout);
    }

    [Fact]
    public async Task Payment_left_processing_is_picked_up_again()
    {
        var (host, plan) = await HostAsync();
        await using var _ = host;
        var reference = (await StartAsync(host, plan)).Reference!;
        await host.Query(db => db.MobilePayments.ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, MobilePaymentStatus.Processing)));
        host.Payments.Pay(reference);

        host.Advance(TimeSpan.FromMinutes(3));
        await host.Run<MobileMoneyService, int>(s => s.CheckPendingAsync());

        Assert.Equal(MobilePaymentStatus.Successful, (await host.Query(db => db.MobilePayments.SingleAsync())).Status);
        Assert.Equal(1, await host.Query(db => db.Vouchers.CountAsync()));
    }
}

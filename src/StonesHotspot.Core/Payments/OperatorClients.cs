using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Payments;

/// <summary>Creates operator clients that share one HttpClient and keep their access tokens until they expire.</summary>
public sealed class HttpPaymentProviderFactory(HttpClient http, TimeProvider clock) : IPaymentProviderFactory
{
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _tokens = new();

    public IPaymentProvider? Create(PaymentMethod method, PaymentSettings settings) => method switch
    {
        PaymentMethod.MtnMoMo when settings.Mtn.Configured => new MtnMoMoClient(http, settings.Mtn, Tokens),
        PaymentMethod.AirtelMoney when settings.Airtel.Configured => new AirtelMoneyClient(http, settings.Airtel, Tokens),
        _ => null,
    };

    private TokenCache Tokens => new(_tokens, clock);
}

/// <summary>Access tokens per (address, account), renewed a minute before they expire.</summary>
public sealed class TokenCache(ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> store, TimeProvider clock)
{
    public async Task<string> GetAsync(string key, Func<Task<(string Token, int ExpiresInSeconds)>> fetch)
    {
        if (store.TryGetValue(key, out var cached) && cached.Expires > clock.GetUtcNow())
            return cached.Token;
        var (token, seconds) = await fetch();
        store[key] = (token, clock.GetUtcNow().AddSeconds(Math.Max(30, seconds - 60)));
        return token;
    }

    public void Forget(string key) => store.TryRemove(key, out _);
}

internal static class OperatorHttp
{
    public static async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpClient http, HttpRequestMessage request, string operatorName, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentException($"Cannot reach {operatorName} ({request.RequestUri?.Host}): {ex.InnerException?.Message ?? ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PaymentException($"{operatorName} did not answer in time.", ex);
        }
    }

    /// <summary>JSON with a Content-Length (no chunked body: some operator gateways refuse it).</summary>
    public static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    public static JsonElement Parse(string body, string operatorName)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new PaymentException($"{operatorName} sent an answer that is not JSON: {Short(body)}", ex);
        }
    }

    public static string? Text(JsonElement e, params string[] path)
    {
        foreach (var name in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e))
                return null;
        }
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => e.GetRawText(),
            JsonValueKind.Object => e.GetRawText(),
            _ => null,
        };
    }

    public static int Seconds(JsonElement e, string name) =>
        int.TryParse(Text(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 300;

    public static string Short(string text) => text.Length > 200 ? text[..200] + "…" : text;
}

/// <summary>
/// MTN MoMo Collection API ("Request to Pay"). Sandbox: base https://sandbox.momodeveloper.mtn.com, target
/// environment "sandbox", currency EUR. Production: the base URL and target environment MTN Congo gives, XAF.
/// </summary>
public sealed class MtnMoMoClient(HttpClient http, MtnSettings settings, TokenCache tokens) : IPaymentProvider
{
    private const string Name = "MTN MoMo";

    public PaymentMethod Method => PaymentMethod.MtnMoMo;

    /// <summary>MTN requires a UUID v4 as X-Reference-Id.</summary>
    public string NewReference() => Guid.NewGuid().ToString();

    public async Task RequestToPayAsync(PaymentRequest request, CancellationToken ct)
    {
        var message = await AuthorizedAsync(HttpMethod.Post, "/collection/v1_0/requesttopay", ct);
        message.Headers.Add("X-Reference-Id", request.Reference);
        if (request.CallbackUrl is not null)
            message.Headers.Add("X-Callback-Url", request.CallbackUrl);
        message.Content = OperatorHttp.Json(new
        {
            amount = request.Amount.Amount.ToString(CultureInfo.InvariantCulture),
            currency = settings.Currency,
            externalId = request.Reference,
            payer = new { partyIdType = "MSISDN", partyId = request.Phone.TrimStart('+') },
            payerMessage = Clip(request.Description, 160),
            payeeNote = Clip(request.Description, 160),
        });

        var (status, body) = await OperatorHttp.SendAsync(http, message, Name, ct);
        if (status is HttpStatusCode.Accepted or HttpStatusCode.Conflict)   // 409: this reference was already sent
            return;
        if (status == HttpStatusCode.Unauthorized)
            Forget();
        throw new PaymentException($"{Name} refused the payment request: HTTP {(int)status} {OperatorHttp.Short(body)}");
    }

    public async Task<OperatorPaymentStatus> GetStatusAsync(string reference, CancellationToken ct)
    {
        var message = await AuthorizedAsync(HttpMethod.Get, $"/collection/v1_0/requesttopay/{Uri.EscapeDataString(reference)}", ct);
        var (status, body) = await OperatorHttp.SendAsync(http, message, Name, ct);
        if (status == HttpStatusCode.NotFound)
            return new OperatorPaymentStatus(OperatorPaymentState.Pending, Reason: "not found yet");
        if (status != HttpStatusCode.OK)
        {
            if (status == HttpStatusCode.Unauthorized)
                Forget();
            throw new PaymentException($"{Name} status check failed: HTTP {(int)status} {OperatorHttp.Short(body)}");
        }

        var json = OperatorHttp.Parse(body, Name);
        var reason = OperatorHttp.Text(json, "reason") is { } r && r.StartsWith('{')
            ? OperatorHttp.Text(OperatorHttp.Parse(r, Name), "code") ?? r
            : OperatorHttp.Text(json, "reason");
        decimal? amount = decimal.TryParse(OperatorHttp.Text(json, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var a) ? a : null;
        return (OperatorHttp.Text(json, "status") ?? "").ToUpperInvariant() switch
        {
            "SUCCESSFUL" => new(OperatorPaymentState.Successful, OperatorHttp.Text(json, "financialTransactionId"), null, amount),
            "FAILED" or "REJECTED" or "TIMEOUT" or "EXPIRED" => new(OperatorPaymentState.Failed, null, reason ?? OperatorHttp.Text(json, "status")),
            _ => new(OperatorPaymentState.Pending),
        };
    }

    public Task TestConnectionAsync(CancellationToken ct) => TokenAsync(ct);

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var token = await TokenAsync(ct);
        var message = new HttpRequestMessage(method, settings.BaseUrl + path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("X-Target-Environment", settings.TargetEnvironment);
        message.Headers.Add("Ocp-Apim-Subscription-Key", settings.SubscriptionKey);
        return message;
    }

    private Task<string> TokenAsync(CancellationToken ct) => tokens.GetAsync(CacheKey, async () =>
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl + "/collection/token/");
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiUser}:{settings.ApiKey}")));
        message.Headers.Add("Ocp-Apim-Subscription-Key", settings.SubscriptionKey);
        message.Content = new StringContent("");
        var (status, body) = await OperatorHttp.SendAsync(http, message, Name, ct);
        if (status != HttpStatusCode.OK)
            throw new PaymentException($"{Name} refused the keys: HTTP {(int)status} {OperatorHttp.Short(body)}");
        var json = OperatorHttp.Parse(body, Name);
        return (OperatorHttp.Text(json, "access_token") ?? throw new PaymentException($"{Name} sent no access token."), OperatorHttp.Seconds(json, "expires_in"));
    });

    private string CacheKey => $"mtn|{settings.BaseUrl}|{settings.ApiUser}";
    private void Forget() => tokens.Forget(CacheKey);
    private static string Clip(string text, int max) => text.Length > max ? text[..max] : text;
}

/// <summary>
/// Airtel Money Collections API. Sandbox (UAT): https://openapiuat.airtel.africa; production: https://openapi.airtel.africa.
/// Numbers are sent without the country code (Congo: the 9 national digits).
/// </summary>
public sealed class AirtelMoneyClient(HttpClient http, AirtelSettings settings, TokenCache tokens) : IPaymentProvider
{
    private const string Name = "Airtel Money";

    public PaymentMethod Method => PaymentMethod.AirtelMoney;

    /// <summary>Short upper-case id (Airtel limits transaction ids in length).</summary>
    public string NewReference() => "SH" + Guid.NewGuid().ToString("N")[..18].ToUpperInvariant();

    public async Task RequestToPayAsync(PaymentRequest request, CancellationToken ct)
    {
        var message = await AuthorizedAsync(HttpMethod.Post, "/merchant/v1/payments/", ct);
        message.Content = OperatorHttp.Json(new
        {
            reference = request.Description.Length > 64 ? request.Description[..64] : request.Description,
            subscriber = new { country = settings.Country, currency = settings.Currency, msisdn = National(request.Phone) },
            transaction = new { amount = request.Amount.Amount, country = settings.Country, currency = settings.Currency, id = request.Reference },
        });

        var (status, body) = await OperatorHttp.SendAsync(http, message, Name, ct);
        if (status == HttpStatusCode.Unauthorized)
            Forget();
        if (status != HttpStatusCode.OK)
            throw new PaymentException($"{Name} refused the payment request: HTTP {(int)status} {OperatorHttp.Short(body)}");
        var json = OperatorHttp.Parse(body, Name);
        if (OperatorHttp.Text(json, "status", "success") is not "true")
            throw new PaymentException($"{Name} refused the payment request: {OperatorHttp.Text(json, "status", "message") ?? OperatorHttp.Short(body)}");
    }

    public async Task<OperatorPaymentStatus> GetStatusAsync(string reference, CancellationToken ct)
    {
        var message = await AuthorizedAsync(HttpMethod.Get, $"/standard/v1/payments/{Uri.EscapeDataString(reference)}", ct);
        var (status, body) = await OperatorHttp.SendAsync(http, message, Name, ct);
        if (status == HttpStatusCode.Unauthorized)
            Forget();
        if (status == HttpStatusCode.NotFound)
            return new OperatorPaymentStatus(OperatorPaymentState.Pending, Reason: "not found yet");
        if (status != HttpStatusCode.OK)
            throw new PaymentException($"{Name} status check failed: HTTP {(int)status} {OperatorHttp.Short(body)}");

        var json = OperatorHttp.Parse(body, Name);
        var reason = OperatorHttp.Text(json, "data", "transaction", "message");
        return (OperatorHttp.Text(json, "data", "transaction", "status") ?? "").ToUpperInvariant() switch
        {
            "TS" => new(OperatorPaymentState.Successful, OperatorHttp.Text(json, "data", "transaction", "airtel_money_id")),
            "TF" or "TE" => new(OperatorPaymentState.Failed, null, reason ?? "TF"),
            _ => new(OperatorPaymentState.Pending),   // TIP (in progress), TA (ambiguous: ask again later)
        };
    }

    public Task TestConnectionAsync(CancellationToken ct) => TokenAsync(ct);

    /// <summary>"+242051234567" → "051234567".</summary>
    public static string National(string phone) => phone.StartsWith("+242", StringComparison.Ordinal) ? phone[4..] : phone.TrimStart('+');

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var token = await TokenAsync(ct);
        var message = new HttpRequestMessage(method, settings.BaseUrl + path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("X-Country", settings.Country);
        message.Headers.Add("X-Currency", settings.Currency);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    private Task<string> TokenAsync(CancellationToken ct) => tokens.GetAsync(CacheKey, async () =>
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl + "/auth/oauth2/token")
        {
            Content = OperatorHttp.Json(new { client_id = settings.ClientId, client_secret = settings.ClientSecret, grant_type = "client_credentials" }),
        };
        var (status, body) = await OperatorHttp.SendAsync(http, message, Name, ct);
        if (status != HttpStatusCode.OK)
            throw new PaymentException($"{Name} refused the keys: HTTP {(int)status} {OperatorHttp.Short(body)}");
        var json = OperatorHttp.Parse(body, Name);
        return (OperatorHttp.Text(json, "access_token") ?? throw new PaymentException($"{Name} sent no access token."), OperatorHttp.Seconds(json, "expires_in"));
    });

    private string CacheKey => $"airtel|{settings.BaseUrl}|{settings.ClientId}";
    private void Forget() => tokens.Forget(CacheKey);
}

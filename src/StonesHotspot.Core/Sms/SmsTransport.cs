using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Core.Sms;

public enum SmsTransportKind { None, Modem, Http }

/// <summary>How SMS leave the hotspot. <see cref="HttpUrl"/> and <see cref="HttpBody"/> are decrypted here (they may hold an API key).</summary>
public sealed record SmsSettings(SmsTransportKind Kind, string ModemPort, int ModemBaud, string? HttpUrl, string? HttpBody, string? HttpSuccessText)
{
    public bool Configured => Kind switch
    {
        SmsTransportKind.Modem => ModemPort.Length > 0,
        SmsTransportKind.Http => !string.IsNullOrWhiteSpace(HttpUrl),
        _ => false,
    };

    public static async Task<SmsSettings> LoadAsync(SettingsService settings, ISecretProtector secrets, CancellationToken ct = default)
    {
        string? Secret(string? stored) => string.IsNullOrEmpty(stored) ? null : secrets.Unprotect(stored);
        return new SmsSettings(
            (await settings.GetAsync(SettingKeys.SmsTransport, ct)) switch { "modem" => SmsTransportKind.Modem, "http" => SmsTransportKind.Http, _ => SmsTransportKind.None },
            (await settings.GetAsync(SettingKeys.SmsModemPort, ct) ?? "").Trim().ToUpperInvariant(),
            await settings.GetIntAsync(SettingKeys.SmsModemBaud, 115200, ct),
            Secret(await settings.GetAsync(SettingKeys.SmsHttpUrl, ct)),
            Secret(await settings.GetAsync(SettingKeys.SmsHttpBody, ct)),
            await settings.GetAsync(SettingKeys.SmsHttpSuccessText, ct));
    }
}

/// <summary>Sends one SMS. Throws <see cref="SmsException"/> with a message staff can act on.</summary>
public interface ISmsTransport
{
    Task SendAsync(string phone, string text, CancellationToken ct);
}

/// <summary>Builds the transport for the settings; the modem (serial port) one lives in the Network project.</summary>
public interface ISmsTransportFactory
{
    ISmsTransport Create(SmsSettings settings);

    /// <summary>COM ports present on the PC, for the settings page.</summary>
    IReadOnlyList<string> SerialPorts();
}

public sealed class SmsException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// An HTTP SMS provider described by a URL template: <c>{phone}</c> (+242…), <c>{phone_digits}</c> (242…) and
/// <c>{message}</c>. With a body template the request is a POST (JSON when the body starts with "{"); values are
/// escaped for where they go. Success = HTTP 2xx and, if set, the answer contains <paramref name="successText"/>.
/// </summary>
public sealed class HttpSmsTransport(HttpClient http, string urlTemplate, string? bodyTemplate, string? successText) : ISmsTransport
{
    public async Task SendAsync(string phone, string text, CancellationToken ct)
    {
        var digits = phone.TrimStart('+');
        string Fill(string template, Func<string, string> escape) => template
            .Replace("{phone}", escape(phone), StringComparison.Ordinal)
            .Replace("{phone_digits}", escape(digits), StringComparison.Ordinal)
            .Replace("{message}", escape(text), StringComparison.Ordinal);

        var url = Fill(urlTemplate.Trim(), Uri.EscapeDataString);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new SmsException("The SMS provider address is not a valid http(s) URL.");

        using var request = new HttpRequestMessage(string.IsNullOrWhiteSpace(bodyTemplate) ? HttpMethod.Get : HttpMethod.Post, uri);
        if (!string.IsNullOrWhiteSpace(bodyTemplate))
        {
            var json = bodyTemplate.TrimStart().StartsWith('{');
            var body = json
                ? Fill(bodyTemplate, v => JsonEncodedText.Encode(v, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString())
                : Fill(bodyTemplate, WebUtility.UrlEncode);
            request.Content = new StringContent(body, Encoding.UTF8, json ? "application/json" : "application/x-www-form-urlencoded");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsException($"Cannot reach the SMS provider ({uri.Host}): {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new SmsException($"The SMS provider ({uri.Host}) did not answer in time.", ex);
        }

        using (response)
        {
            var answer = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new SmsException(string.Create(CultureInfo.InvariantCulture, $"The SMS provider refused the message: HTTP {(int)response.StatusCode} {Short(answer)}"));
            if (!string.IsNullOrWhiteSpace(successText) && !answer.Contains(successText.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new SmsException($"The SMS provider did not confirm the message: {Short(answer)}");
        }
    }

    private static string Short(string text) => text.Length > 150 ? text[..150] + "…" : text;
}

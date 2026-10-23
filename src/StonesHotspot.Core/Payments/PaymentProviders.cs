using System.Globalization;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Core.Payments;

public enum OperatorPaymentState { Pending, Successful, Failed }

/// <param name="Amount">Amount the operator says was paid, when it tells.</param>
public sealed record OperatorPaymentStatus(OperatorPaymentState State, string? OperatorTransactionId = null, string? Reason = null, decimal? Amount = null);

/// <param name="CallbackUrl">Where the operator may notify us; null without a public address.</param>
public sealed record PaymentRequest(string Reference, string Phone, Xaf Amount, string Description, string? CallbackUrl);

/// <summary>The operator refused or could not be reached; the message is for staff (and logs), never contains keys.</summary>
public sealed class PaymentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One Mobile Money operator's collection API.</summary>
public interface IPaymentProvider
{
    PaymentMethod Method { get; }

    /// <summary>A new reference in the form this operator accepts.</summary>
    string NewReference();

    /// <summary>Asks the customer to pay; the answer comes later (<see cref="GetStatusAsync"/> or a callback).</summary>
    Task RequestToPayAsync(PaymentRequest request, CancellationToken ct);

    Task<OperatorPaymentStatus> GetStatusAsync(string reference, CancellationToken ct);

    /// <summary>Gets an access token: proves the keys and the address work.</summary>
    Task TestConnectionAsync(CancellationToken ct);
}

public interface IPaymentProviderFactory
{
    /// <summary>The provider of <paramref name="method"/>, or null when that operator is off or not set up.</summary>
    IPaymentProvider? Create(PaymentMethod method, PaymentSettings settings);
}

public sealed record MtnSettings(bool Enabled, string BaseUrl, string TargetEnvironment, string Currency, string? SubscriptionKey, string? ApiUser, string? ApiKey, IReadOnlyList<string> Prefixes)
{
    public bool Configured => Enabled && !string.IsNullOrWhiteSpace(SubscriptionKey) && !string.IsNullOrWhiteSpace(ApiUser) && !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed record AirtelSettings(bool Enabled, string BaseUrl, string? ClientId, string? ClientSecret, string Country, string Currency, IReadOnlyList<string> Prefixes)
{
    public bool Configured => Enabled && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <param name="TariffIds">Plans sold online; empty = every active plan with a price.</param>
/// <param name="CallbackBaseUrl">Public https address of this hotspot for operator notifications; null = polling only.</param>
public sealed record PaymentSettings(MtnSettings Mtn, AirtelSettings Airtel, IReadOnlyList<int> TariffIds, string? CallbackBaseUrl, TimeSpan Timeout)
{
    public bool AnyConfigured => Mtn.Configured || Airtel.Configured;

    public IReadOnlyList<string> PrefixesFor(PaymentMethod method) => method == PaymentMethod.MtnMoMo ? Mtn.Prefixes : Airtel.Prefixes;

    public static async Task<PaymentSettings> LoadAsync(SettingsService settings, ISecretProtector secrets, CancellationToken ct = default)
    {
        async Task<string> Get(string key) => (await settings.GetAsync(key, ct) ?? "").Trim();
        async Task<string?> Secret(string key) => await Get(key) is { Length: > 0 } stored ? secrets.Unprotect(stored) : null;

        var mtn = new MtnSettings(
            await settings.GetBoolAsync(SettingKeys.MtnEnabled, ct), (await Get(SettingKeys.MtnBaseUrl)).TrimEnd('/'),
            await Get(SettingKeys.MtnTargetEnvironment), (await Get(SettingKeys.MtnCurrency)).ToUpperInvariant(),
            await Secret(SettingKeys.MtnSubscriptionKey), await Get(SettingKeys.MtnApiUser) is { Length: > 0 } user ? user : null,
            await Secret(SettingKeys.MtnApiKey), PhoneNumber.ParsePrefixes(await Get(SettingKeys.MtnPrefixes)));
        var airtel = new AirtelSettings(
            await settings.GetBoolAsync(SettingKeys.AirtelEnabled, ct), (await Get(SettingKeys.AirtelBaseUrl)).TrimEnd('/'),
            await Get(SettingKeys.AirtelClientId) is { Length: > 0 } id ? id : null, await Secret(SettingKeys.AirtelClientSecret),
            (await Get(SettingKeys.AirtelCountry)).ToUpperInvariant(), (await Get(SettingKeys.AirtelCurrency)).ToUpperInvariant(),
            PhoneNumber.ParsePrefixes(await Get(SettingKeys.AirtelPrefixes)));
        var tariffs = (await Get(SettingKeys.PayTariffs)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).ToList();
        var callback = await Get(SettingKeys.PayCallbackUrl);
        return new PaymentSettings(mtn, airtel, tariffs, callback.Length > 0 ? callback.TrimEnd('/') : null,
            TimeSpan.FromSeconds(Math.Clamp(await settings.GetIntAsync(SettingKeys.PayTimeoutSeconds, 180, ct), 60, 900)));
    }
}

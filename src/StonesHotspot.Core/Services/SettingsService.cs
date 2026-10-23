using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

public static class SettingKeys
{
    public const string BusinessName = "Business.Name";
    public const string WifiName = "Business.WifiName";
    public const string DefaultLanguage = "Ui.DefaultLanguage";
    public const string DefaultIdleTimeoutMinutes = "Sessions.DefaultIdleTimeoutMinutes";
    public const string LastReceiptNumber = "Receipts.LastNumber";
    public const string PortalWelcomeUrl = "Portal.WelcomeUrl";
    public const string PortalAccentColor = "Portal.AccentColor";
    public const string PortalTermsFr = "Portal.TermsFr";
    public const string PortalTermsEn = "Portal.TermsEn";
    public const string FairShareEnabled = "Bandwidth.FairShare";
    public const string FairShareTotalDownKbps = "Bandwidth.TotalDownKbps";
    public const string FairShareTotalUpKbps = "Bandwidth.TotalUpKbps";
    public const string FairShareMinKbps = "Bandwidth.MinKbps";
    public const string FairShareMaxKbps = "Bandwidth.MaxKbps";
    public const string FilterFamilyDns = "Filter.FamilyDns";
    public const string NetworkMode = "Network.Mode";
    public const string NetworkWanAdapterId = "Network.WanAdapterId";
    public const string NetworkLanAdapterId = "Network.LanAdapterId";
    public const string FreeEnabled = "Free.Terms.Enabled";
    public const string FreeTariffId = "Free.Terms.TariffId";
    public const string FreePerDay = "Free.Terms.PerDevicePerDay";
    public const string PasswordEnabled = "Free.Password.Enabled";
    public const string PasswordTariffId = "Free.Password.TariffId";
    public const string PasswordPerDay = "Free.Password.PerDevicePerDay";
    public const string PasswordValue = "Free.Password.Value";
    public const string PasswordDaily = "Free.Password.Daily";
    public const string PasswordSecret = "Free.Password.Secret";
    public const string SmsEnabled = "Sms.Enabled";
    public const string SmsTariffId = "Sms.TariffId";
    public const string SmsPerDay = "Sms.LoginsPerDevicePerDay";
    public const string SmsPerNumberPerDay = "Sms.CodesPerNumberPerDay";
    public const string SmsPerDevicePerDay = "Sms.CodesPerDevicePerDay";
    public const string SmsTotalPerDay = "Sms.TotalPerDay";
    public const string SmsPrefixes = "Sms.AllowedPrefixes";
    public const string SmsTransport = "Sms.Transport";
    public const string SmsModemPort = "Sms.ModemPort";
    public const string SmsModemBaud = "Sms.ModemBaud";
    public const string SmsHttpUrl = "Sms.HttpUrl";
    public const string SmsHttpBody = "Sms.HttpBody";
    public const string SmsHttpSuccessText = "Sms.HttpSuccessText";
    public const string PayTariffs = "Pay.TariffIds";
    public const string PayCallbackUrl = "Pay.CallbackBaseUrl";
    public const string PayTimeoutSeconds = "Pay.TimeoutSeconds";
    public const string MtnEnabled = "Pay.Mtn.Enabled";
    public const string MtnBaseUrl = "Pay.Mtn.BaseUrl";
    public const string MtnTargetEnvironment = "Pay.Mtn.TargetEnvironment";
    public const string MtnCurrency = "Pay.Mtn.Currency";
    public const string MtnSubscriptionKey = "Pay.Mtn.SubscriptionKey";
    public const string MtnApiUser = "Pay.Mtn.ApiUser";
    public const string MtnApiKey = "Pay.Mtn.ApiKey";
    public const string MtnPrefixes = "Pay.Mtn.Prefixes";
    public const string AirtelEnabled = "Pay.Airtel.Enabled";
    public const string AirtelBaseUrl = "Pay.Airtel.BaseUrl";
    public const string AirtelClientId = "Pay.Airtel.ClientId";
    public const string AirtelClientSecret = "Pay.Airtel.ClientSecret";
    public const string AirtelCountry = "Pay.Airtel.Country";
    public const string AirtelCurrency = "Pay.Airtel.Currency";
    public const string AirtelPrefixes = "Pay.Airtel.Prefixes";
    public const string ReceiptHeader = "Receipts.Header";
    public const string ReceiptFooter = "Receipts.Footer";

    /// <summary>Values written on first start; existing values are never overwritten.</summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>
    {
        [BusinessName] = "Stone's Hotspot",
        [DefaultLanguage] = "fr",
        [DefaultIdleTimeoutMinutes] = "10",
        [FairShareEnabled] = "false",
        [FairShareMinKbps] = "256",
        [FairShareMaxKbps] = "4096",
        [FilterFamilyDns] = "false",
        [FreePerDay] = "1",
        [PasswordPerDay] = "3",
        [SmsPerDay] = "1",
        [SmsPerNumberPerDay] = "3",
        [SmsPerDevicePerDay] = "3",
        [SmsTotalPerDay] = "200",
        [SmsPrefixes] = "+24206, +24205, +24204",
        [SmsModemBaud] = "115200",
        [PayTimeoutSeconds] = "180",
        [MtnBaseUrl] = "https://sandbox.momodeveloper.mtn.com",
        [MtnTargetEnvironment] = "sandbox",
        [MtnCurrency] = "EUR",
        [MtnPrefixes] = "+24206",
        [AirtelBaseUrl] = "https://openapiuat.airtel.africa",
        [AirtelCountry] = "CG",
        [AirtelCurrency] = "XAF",
        [AirtelPrefixes] = "+24205, +24204",
    };
}

/// <summary>Key/value application settings stored in the database.</summary>
public sealed class SettingsService(HotspotDbContext db, AuditService audit)
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        (await db.Settings.FindAsync([key], ct))?.Value ?? SettingKeys.Defaults.GetValueOrDefault(key);

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default) =>
        int.TryParse(await GetAsync(key, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default) =>
        bool.TryParse(await GetAsync(key, ct), out var value) && value;

    public async Task SetAsync(string key, string value, Staff? actor, CancellationToken ct = default) =>
        await SetAsync(key, value, actor, secret: false, ct);

    /// <summary>Saves a value; for a secret the audit log only says it changed.</summary>
    public async Task SetAsync(string key, string value, Staff? actor, bool secret, CancellationToken ct = default)
    {
        var setting = await db.Settings.FindAsync([key], ct);
        var old = setting?.Value;
        if (setting is null)
            db.Settings.Add(new Setting { Key = key, Value = value });
        else
            setting.Value = value;

        audit.Record(actor, "Setting.Changed", secret ? $"{key}: (secret changed)" : $"{key}: '{old}' → '{value}'");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds missing default settings (does not save).</summary>
    internal async Task AddMissingDefaultsAsync(CancellationToken ct)
    {
        var existing = await db.Settings.Select(s => s.Key).ToListAsync(ct);
        foreach (var (key, value) in SettingKeys.Defaults.Where(d => !existing.Contains(d.Key)))
            db.Settings.Add(new Setting { Key = key, Value = value });
    }
}

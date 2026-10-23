using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Core.Sms;

/// <summary>Sends SMS through the configured modem or provider and keeps a log of every attempt (without the text).</summary>
public sealed class SmsService(
    HotspotDbContext db, SettingsService settings, ISecretProtector secrets, ISmsTransportFactory transports,
    TimeProvider clock, ILogger<SmsService> logger)
{
    public Task<SmsSettings> GetSettingsAsync(CancellationToken ct = default) => SmsSettings.LoadAsync(settings, secrets, ct);

    /// <returns>Null when sent, otherwise why it failed.</returns>
    public async Task<string?> SendAsync(string phone, string text, SmsPurpose purpose, int? deviceId, CancellationToken ct = default)
    {
        var config = await GetSettingsAsync(ct);
        string? error = null;
        if (!config.Configured)
        {
            error = "No SMS modem or provider is configured.";
        }
        else
        {
            try
            {
                await transports.Create(config).SendAsync(phone, text, ct);
            }
            catch (SmsException ex)
            {
                error = ex.Message;
            }
        }

        db.SmsMessages.Add(new SmsMessage
        {
            Phone = phone, Purpose = purpose, DeviceId = deviceId, CreatedAt = clock.GetUtcNow().UtcDateTime,
            Sent = error is null, Error = error?.Length > 300 ? error[..300] : error,
        });
        await db.SaveChangesAsync(ct);

        if (error is null)
            logger.LogInformation("SMS ({Purpose}) sent to {Phone} via {Transport}", purpose, Mask(phone), config.Kind);
        else
            logger.LogWarning("SMS ({Purpose}) to {Phone} failed via {Transport}: {Error}", purpose, Mask(phone), config.Kind, error);
        return error;
    }

    public Task<List<SmsMessage>> RecentAsync(int take = 50, CancellationToken ct = default) =>
        db.SmsMessages.AsNoTracking().OrderByDescending(m => m.Id).Take(take).ToListAsync(ct);

    /// <summary>Stores the provider URL and body template encrypted; empty clears them.</summary>
    public async Task SaveProviderAsync(string? url, string? body, Staff? actor, CancellationToken ct = default)
    {
        async Task Save(string key, string? value)
        {
            value = value?.Trim() ?? "";
            var current = await settings.GetAsync(key, ct);
            var currentPlain = string.IsNullOrEmpty(current) ? "" : secrets.Unprotect(current) ?? "";
            if (currentPlain != value)
                await settings.SetAsync(key, value.Length == 0 ? "" : secrets.Protect(value), actor, secret: true, ct);
        }
        await Save(SettingKeys.SmsHttpUrl, url);
        await Save(SettingKeys.SmsHttpBody, body);
    }

    /// <summary>"+242061234567" → "+24206****567" for logs.</summary>
    public static string Mask(string phone) => phone.Length > 9 ? phone[..6] + new string('*', phone.Length - 9) + phone[^3..] : "***";
}

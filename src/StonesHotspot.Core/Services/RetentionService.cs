using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Sms;

namespace StonesHotspot.Core.Services;

/// <param name="Sessions">Sessions whose IP address was removed.</param>
/// <param name="Devices">Devices whose MAC, IP and name were replaced by <c>ANON-n</c>.</param>
/// <param name="Phones">SMS records, codes and Mobile Money payments whose phone number was masked.</param>
/// <param name="SmsChallenges">Old SMS login challenges deleted.</param>
public sealed record RetentionResult(int Sessions, int Devices, int Phones, int SmsChallenges)
{
    public int Total => Sessions + Devices + Phones + SmsChallenges;
}

/// <summary>
/// Keeps the connection logs (who, when, MAC, IP, phone) only for the number of days set in
/// <see cref="SettingKeys.LogRetentionDays"/>. Older records lose what identifies a person; amounts, plans, dates,
/// receipts and reports stay, so sales and cash are never affected. Devices the admin whitelisted, blocked or wrote a
/// note on are kept as they are (they are rules, not logs).
/// </summary>
public sealed class RetentionService(HotspotDbContext db, SettingsService settings, AuditService audit, TimeProvider clock)
{
    public const int DefaultDays = 365, MinDays = 30, MaxDays = 3650;
    public const string AnonymousPrefix = "ANON-";

    public async Task<int> DaysAsync(CancellationToken ct = default) =>
        Math.Clamp(await settings.GetIntAsync(SettingKeys.LogRetentionDays, DefaultDays, ct), MinDays, MaxDays);

    public async Task<RetentionResult> ApplyAsync(CancellationToken ct = default)
    {
        var days = await DaysAsync(ct);
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-days);

        var sessions = await db.Sessions
            .Where(s => s.EndedAt != null && s.EndedAt < cutoff && s.Ip != null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Ip, (string?)null), ct);

        var devices = await db.Devices
            .Where(d => d.LastSeen < cutoff && !d.Whitelisted && !d.Blocked && d.Note == null && !d.Mac.StartsWith(AnonymousPrefix)
                        && !db.Sessions.Any(s => s.DeviceId == d.Id && s.EndedAt == null))
            .ToListAsync(ct);
        foreach (var device in devices)
        {
            device.Mac = AnonymousPrefix + device.Id;
            device.LastIp = null;
            device.Hostname = null;
        }

        var phones = 0;
        foreach (var message in await db.SmsMessages.Where(m => m.CreatedAt < cutoff && !m.Phone.Contains("*")).ToListAsync(ct))
        {
            message.Phone = SmsService.Mask(message.Phone);
            phones++;
        }
        foreach (var voucher in await db.Vouchers.Where(v => v.CreatedAt < cutoff && v.Phone != null && !v.Phone.Contains("*")).ToListAsync(ct))
        {
            voucher.Phone = SmsService.Mask(voucher.Phone!);
            phones++;
        }
        foreach (var payment in await db.MobilePayments
                     .Where(p => p.CreatedAt < cutoff && !p.Phone.Contains("*")
                                 && p.Status != MobilePaymentStatus.Pending && p.Status != MobilePaymentStatus.Processing)
                     .ToListAsync(ct))
        {
            payment.Phone = SmsService.Mask(payment.Phone);
            phones++;
        }

        var challenges = await db.SmsChallenges.Where(c => c.CreatedAt < cutoff).ExecuteDeleteAsync(ct);

        var result = new RetentionResult(sessions, devices.Count, phones, challenges);
        if (result.Total > 0)
            audit.Record(null, "Privacy.Retention",
                $"Older than {days} days: {sessions} session IPs removed, {devices.Count} devices anonymised, {phones} phone numbers masked, {challenges} SMS challenges deleted");
        await db.SaveChangesAsync(ct);
        return result;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Sms;

namespace StonesHotspot.Core.Services;

public enum SmsCodeStatus
{
    Sent, ModeDisabled, InvalidNumber, PrefixNotAllowed, TooSoon, NumberLimit, DeviceLimit, TotalLimit, DailyLoginLimit,
    DeviceBlocked, LockedOut, SendFailed,
}

/// <param name="Phone">The normalized number (+242…).</param>
/// <param name="WaitSeconds">With <see cref="SmsCodeStatus.TooSoon"/>: seconds before a new code may be asked.</param>
public sealed record SmsCodeResult(SmsCodeStatus Status, string? Phone = null, int? WaitSeconds = null, string? Error = null);

/// <summary>SMS login: a 6-digit code sent to a Congo number, typed back on the portal within 10 minutes.</summary>
public sealed class SmsLoginService(
    HotspotDbContext db, SettingsService settings, DeviceService devices, SessionService sessions, FreeAccessService free,
    SmsService sms, TimeProvider clock)
{
    public const int CodeLength = 6;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    public async Task<SmsCodeResult> RequestCodeAsync(string mac, string? ip, string? phoneText, CancellationToken ct = default)
    {
        var now = Now;
        var device = await devices.TouchAsync(mac, ip, null, ct);
        if (device.Id == 0)
            await db.SaveChangesAsync(ct);   // a new device needs its id for the vouchers, codes and SMS log
        if (await sessions.CheckDeviceAsync(device, now, ct) is { } refused)
            return new(refused.Status == LoginStatus.LockedOut ? SmsCodeStatus.LockedOut : SmsCodeStatus.DeviceBlocked);
        if (!(await free.GetSettingsAsync(ct)).Sms.Usable)
            return new(SmsCodeStatus.ModeDisabled);

        var phone = PhoneNumber.Normalize(phoneText);
        if (phone is null)
            return new(SmsCodeStatus.InvalidNumber);
        if (!PhoneNumber.IsAllowed(phone, PhoneNumber.ParsePrefixes(await settings.GetAsync(SettingKeys.SmsPrefixes, ct))))
            return new(SmsCodeStatus.PrefixNotAllowed, phone);
        if (!await free.CanGrantAsync(device.Id, FreeMode.Sms, phone, ct))
            return new(SmsCodeStatus.DailyLoginLimit, phone);

        var last = await db.SmsChallenges.Where(c => c.Phone == phone).OrderByDescending(c => c.Id).Select(c => (DateTime?)c.CreatedAt).FirstOrDefaultAsync(ct);
        if (last is { } previous && now - previous < Cooldown)
            return new(SmsCodeStatus.TooSoon, phone, (int)Math.Ceiling((Cooldown - (now - previous)).TotalSeconds));

        var since = now.AddDays(-1);
        var sent = db.SmsMessages.Where(m => m.Purpose == SmsPurpose.LoginCode && m.Sent && m.CreatedAt >= since);
        if (await sent.CountAsync(m => m.Phone == phone, ct) >= await settings.GetIntAsync(SettingKeys.SmsPerNumberPerDay, 3, ct))
            return new(SmsCodeStatus.NumberLimit, phone);
        if (await sent.CountAsync(m => m.DeviceId == device.Id, ct) >= await settings.GetIntAsync(SettingKeys.SmsPerDevicePerDay, 3, ct))
            return new(SmsCodeStatus.DeviceLimit, phone);
        if (await sent.CountAsync(ct) >= await settings.GetIntAsync(SettingKeys.SmsTotalPerDay, 200, ct))
            return new(SmsCodeStatus.TotalLimit, phone);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var business = await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "";
        var text = string.Format(CultureInfo.CurrentUICulture, Strings.Sms_LoginCode, business, code, (int)CodeLifetime.TotalMinutes);
        var error = await sms.SendAsync(phone, text, SmsPurpose.LoginCode, device.Id, ct);
        if (error is not null)
            return new(SmsCodeStatus.SendFailed, phone, Error: error);

        db.SmsChallenges.Add(new SmsChallenge
        {
            Phone = phone, Device = device, CodeHash = Hash(code), CreatedAt = now, ExpiresAt = now + CodeLifetime,
        });
        await db.SaveChangesAsync(ct);
        return new(SmsCodeStatus.Sent, phone);
    }

    /// <summary>Checks the code typed for <paramref name="phoneText"/> on this device and logs it in on the SMS plan.</summary>
    public async Task<LoginResult> VerifyAsync(string mac, string? ip, string? phoneText, string? code, CancellationToken ct = default)
    {
        var now = Now;
        var device = await devices.TouchAsync(mac, ip, null, ct);
        if (device.Id == 0)
            await db.SaveChangesAsync(ct);   // a new device needs its id for the vouchers, codes and SMS log
        if (await sessions.CheckDeviceAsync(device, now, ct) is { } refused)
            return refused;

        var phone = PhoneNumber.Normalize(phoneText);
        var challenge = phone is null ? null
            : await db.SmsChallenges.Where(c => c.Phone == phone && c.DeviceId == device.Id && c.UsedAt == null)
                .OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);
        if (challenge is null)
            return await sessions.FailAsync(device, LoginStatus.InvalidCode, now, ct);
        if (challenge.ExpiresAt <= now || challenge.Attempts >= MaxAttempts)
            return await sessions.RefuseAsync(LoginStatus.CodeExpired, ct);

        var typed = new string((code ?? "").Where(char.IsAsciiDigit).ToArray());
        if (!Matches(typed, challenge.CodeHash))
        {
            challenge.Attempts++;
            return await sessions.FailAsync(device, LoginStatus.InvalidCode, now, ct);
        }

        challenge.UsedAt = now;
        LoginAttemptLimiter.RegisterSuccess(device);
        return await free.GrantAsync(device, ip, FreeMode.Sms, phone, ct);
    }

    /// <summary>"salt:SHA-256(salt + code)" — the code itself is never stored.</summary>
    internal static string Hash(string code)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return salt + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + code)));
    }

    internal static bool Matches(string code, string stored)
    {
        var parts = stored.Split(':');
        return parts.Length == 2 && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(parts[1]), SHA256.HashData(Encoding.UTF8.GetBytes(parts[0] + code)));
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}

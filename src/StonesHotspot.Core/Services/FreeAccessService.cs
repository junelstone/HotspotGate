using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Rules;

namespace StonesHotspot.Core.Services;

public enum FreeMode { Terms, Password, Sms }

/// <param name="TariffId">Plan given to the device; null = not chosen yet (the mode cannot be used).</param>
/// <param name="PerDevicePerDay">New free logins one device may get per local day.</param>
public sealed record FreeModeSettings(bool Enabled, int? TariffId, int PerDevicePerDay)
{
    public bool Usable => Enabled && TariffId is not null && PerDevicePerDay > 0;
}

public sealed record FreeAccessSettings(FreeModeSettings Terms, FreeModeSettings Password, bool PasswordDaily, FreeModeSettings Sms)
{
    public FreeModeSettings For(FreeMode mode) => mode switch
    {
        FreeMode.Terms => Terms,
        FreeMode.Password => Password,
        _ => Sms,
    };
}

/// <summary>
/// Free logins: "I accept the terms", the shared password of the day, and (through <see cref="SmsLoginService"/>)
/// SMS codes. Each login gives the device a hidden voucher on the mode's plan, so time and data are counted like any
/// voucher; at most N new ones per device per local day.
/// </summary>
public sealed class FreeAccessService(
    HotspotDbContext db, SettingsService settings, DeviceService devices, SessionService sessions, VoucherService vouchers,
    TimeProvider clock)
{
    public const int DailyPasswordLength = 6;

    public async Task<FreeAccessSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        async Task<FreeModeSettings> Mode(string enabled, string tariff, string perDay) => new(
            await settings.GetBoolAsync(enabled, ct),
            await settings.GetIntAsync(tariff, 0, ct) is > 0 and var id ? id : null,
            await settings.GetIntAsync(perDay, 1, ct));

        return new FreeAccessSettings(
            await Mode(SettingKeys.FreeEnabled, SettingKeys.FreeTariffId, SettingKeys.FreePerDay),
            await Mode(SettingKeys.PasswordEnabled, SettingKeys.PasswordTariffId, SettingKeys.PasswordPerDay),
            await settings.GetBoolAsync(SettingKeys.PasswordDaily, ct),
            await Mode(SettingKeys.SmsEnabled, SettingKeys.SmsTariffId, SettingKeys.SmsPerDay));
    }

    public async Task<LoginResult> LoginWithTermsAsync(string mac, string? ip, CancellationToken ct = default)
    {
        var device = await devices.TouchAsync(mac, ip, null, ct);
        if (device.Id == 0)
            await db.SaveChangesAsync(ct);   // a new device needs its id for the vouchers, codes and SMS log
        if (await sessions.CheckDeviceAsync(device, Now, ct) is { } refused)
            return refused;
        return await GrantAsync(device, ip, FreeMode.Terms, null, ct);
    }

    public async Task<LoginResult> LoginWithPasswordAsync(string mac, string? ip, string password, CancellationToken ct = default)
    {
        var device = await devices.TouchAsync(mac, ip, null, ct);
        if (device.Id == 0)
            await db.SaveChangesAsync(ct);   // a new device needs its id for the vouchers, codes and SMS log
        if (await sessions.CheckDeviceAsync(device, Now, ct) is { } refused)
            return refused;
        var config = await GetSettingsAsync(ct);
        if (!config.Password.Usable)
            return await sessions.RefuseAsync(LoginStatus.ModeDisabled, ct);

        var expected = await PasswordOfTheDayAsync(ct);
        if (string.IsNullOrEmpty(expected) || !SamePassword(password, expected))
            return await sessions.FailAsync(device, LoginStatus.InvalidPassword, Now, ct);
        LoginAttemptLimiter.RegisterSuccess(device);
        return await GrantAsync(device, ip, FreeMode.Password, null, ct);
    }

    /// <summary>Today's shared password: the fixed one, or the one derived from the date when "new every day" is on.</summary>
    public async Task<string?> PasswordOfTheDayAsync(CancellationToken ct = default)
    {
        if (!await settings.GetBoolAsync(SettingKeys.PasswordDaily, ct))
            return (await settings.GetAsync(SettingKeys.PasswordValue, ct))?.Trim() is { Length: > 0 } fixedPassword ? fixedPassword : null;

        var secret = await settings.GetAsync(SettingKeys.PasswordSecret, ct);
        if (string.IsNullOrEmpty(secret))
        {
            secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await settings.SetAsync(SettingKeys.PasswordSecret, secret, null, secret: true, ct);
        }
        return DailyPassword(secret, LocalToday);
    }

    /// <summary>6 easy-to-read characters (no 0/O, 1/I/L) from HMAC(secret, date): the same all day, new at midnight.</summary>
    public static string DailyPassword(string secret, DateOnly day)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var chars = new char[DailyPasswordLength];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = VoucherCodeGenerator.Alphabet[mac[i] % VoucherCodeGenerator.Alphabet.Length];
        return new string(chars);
    }

    /// <summary>New free logins this device got today in <paramref name="mode"/>.</summary>
    public async Task<int> CountTodayAsync(int deviceId, FreeMode mode, CancellationToken ct = default)
    {
        var since = LocalMidnightUtc;
        var source = SourceOf(mode);
        return await db.Vouchers.CountAsync(v => v.Source == source && v.IssuedToDeviceId == deviceId && v.CreatedAt >= since, ct);
    }

    /// <summary>
    /// Logs the device in with its still-valid free voucher of this mode (logging out and in again costs nothing), or
    /// gives it a new one when the daily limit allows.
    /// </summary>
    internal async Task<LoginResult> GrantAsync(Device device, string? ip, FreeMode mode, string? phone, CancellationToken ct)
    {
        var config = (await GetSettingsAsync(ct)).For(mode);
        var tariff = config.Usable ? await db.Tariffs.FirstOrDefaultAsync(t => t.Id == config.TariffId, ct) : null;
        if (tariff is null)
            return await sessions.RefuseAsync(LoginStatus.ModeDisabled, ct);

        var voucher = await FindReusableAsync(device.Id, mode, phone, ct);
        if (voucher is null)
        {
            if (await CountTodayAsync(device.Id, mode, ct) >= config.PerDevicePerDay)
                return await sessions.RefuseAsync(LoginStatus.DailyLimitReached, ct);
            voucher = await vouchers.CreateForDeviceAsync(tariff, SourceOf(mode), device.Id, phone, ct);
        }
        return await sessions.LoginWithVoucherAsync(device.Mac, ip, voucher.Code, MethodOf(mode), ct);
    }

    /// <summary>True when the device may log in with this mode now: it still has a valid free voucher, or is under the daily limit.</summary>
    internal async Task<bool> CanGrantAsync(int deviceId, FreeMode mode, string? phone, CancellationToken ct)
    {
        var config = (await GetSettingsAsync(ct)).For(mode);
        return await FindReusableAsync(deviceId, mode, phone, ct) is not null || await CountTodayAsync(deviceId, mode, ct) < config.PerDevicePerDay;
    }

    private async Task<Voucher?> FindReusableAsync(int deviceId, FreeMode mode, string? phone, CancellationToken ct)
    {
        var source = SourceOf(mode);
        var now = Now;
        return (await db.Vouchers.Where(v => v.Source == source && v.IssuedToDeviceId == deviceId
                    && (v.Status == VoucherStatus.New || v.Status == VoucherStatus.Active) && (phone == null || v.Phone == phone))
                .OrderByDescending(v => v.Id).Take(5).ToListAsync(ct))
            .FirstOrDefault(v => AllowanceRules.Check(v.Allowance, now) is null);
    }

    private static bool SamePassword(string? typed, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes((typed ?? "").Trim().ToUpperInvariant()), Encoding.UTF8.GetBytes(expected.Trim().ToUpperInvariant()));

    private static VoucherSource SourceOf(FreeMode mode) => mode switch
    {
        FreeMode.Terms => VoucherSource.Free,
        FreeMode.Password => VoucherSource.SharedPassword,
        _ => VoucherSource.Sms,
    };

    private static LoginMethod MethodOf(FreeMode mode) => mode switch
    {
        FreeMode.Terms => LoginMethod.Free,
        FreeMode.Password => LoginMethod.SharedPassword,
        _ => LoginMethod.Sms,
    };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private DateOnly LocalToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, clock.LocalTimeZone));

    private DateTime LocalMidnightUtc => TimeZoneInfo.ConvertTimeToUtc(LocalToday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), clock.LocalTimeZone);
}

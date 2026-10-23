using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Rules;

namespace StonesHotspot.Core.Services;

public enum LoginStatus
{
    Success,
    InvalidCode,
    InvalidCredentials,
    VoucherDisabled,
    VoucherExpired,
    QuotaUsed,
    TooManyDevices,
    LockedOut,
    DeviceBlocked,
    AccountDisabled,
    AccountExpired,
    InsufficientBalance,

    /// <summary>The free/password/SMS mode is off (or has no plan).</summary>
    ModeDisabled,

    /// <summary>The device already used its free logins of the day for this mode.</summary>
    DailyLimitReached,

    InvalidPassword,
    CodeExpired,
}

/// <param name="LockedUntil">Set with <see cref="LoginStatus.LockedOut"/>: when the device may try again (UTC).</param>
public sealed record LoginResult(LoginStatus Status, Session? Session = null, DateTime? LockedUntil = null)
{
    public bool Succeeded => Status == LoginStatus.Success;
}

/// <summary>What the customer's status page shows about their running session.</summary>
/// <param name="SecondsLeft">Countdown shown to the customer: time quota and/or validity, whichever ends first; null when neither applies.</param>
/// <param name="BytesLeft">Data left for data tariffs, else null.</param>
/// <param name="EndsAt">When the voucher/account stops being valid (UTC), if it has an end.</param>
public sealed record SessionStatus(
    Session Session,
    Tariff Tariff,
    string? VoucherCode,
    string? Username,
    long? SecondsLeft,
    long? BytesLeft,
    DateTime? EndsAt,
    Money.Xaf? Balance);

/// <summary>Bytes seen by the packet engine for one session since the previous tick.</summary>
public readonly record struct TrafficDelta(long BytesUp, long BytesDown)
{
    public long Total => BytesUp + BytesDown;
}

/// <summary>
/// Starts, counts and ends internet sessions. <see cref="TickAsync"/> is called about once per second by the
/// service with the traffic counted by the packet engine.
/// </summary>
public sealed class SessionService(
    HotspotDbContext db,
    DeviceService devices,
    AccountService accounts,
    SettingsService settings,
    AuditService audit,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<List<Session>> GetActiveSessionsAsync(CancellationToken ct = default) =>
        ActiveSessions().AsNoTracking().ToListAsync(ct);

    public Task<Session?> GetActiveSessionForDeviceAsync(string mac, CancellationToken ct = default)
    {
        var normalized = MacAddress.Normalize(mac);
        return ActiveSessions().FirstOrDefaultAsync(s => s.Device.Mac == normalized, ct);
    }

    /// <summary>The running session of a device with what is left of it, computed up to this second.</summary>
    public async Task<SessionStatus?> GetStatusForDeviceAsync(string mac, CancellationToken ct = default)
    {
        if (!MacAddress.TryNormalize(mac, out var normalized))
            return null;
        var session = await ActiveSessions().AsNoTracking().FirstOrDefaultAsync(s => s.Device.Mac == normalized, ct);
        return session is null ? null : BuildStatus(session, Now);
    }

    /// <summary>All running sessions with what is left of them (dashboard live view), newest first.</summary>
    public async Task<IReadOnlyList<SessionStatus>> GetActiveStatusesAsync(CancellationToken ct = default)
    {
        var now = Now;
        var sessions = await ActiveSessions().AsNoTracking().OrderByDescending(s => s.StartedAt).ToListAsync(ct);
        return sessions.Select(s => BuildStatus(s, now)).ToList();
    }

    private static SessionStatus BuildStatus(Session session, DateTime now)
    {
        var allowance = session.Voucher?.Allowance ?? session.UserAccount?.Allowance ?? new Allowance();
        var tariff = session.Voucher?.Tariff ?? session.UserAccount?.Tariff ?? session.Tariff;

        DateTime? endsAt = allowance.ExpiresAt;
        if (session.UserAccount?.ExpiresAt is { } accountEnd && (endsAt is null || accountEnd < endsAt))
            endsAt = accountEnd;
        long? untilEnd = endsAt is { } end ? Math.Max(0, (long)Math.Ceiling((end - now).TotalSeconds)) : null;

        long? quotaLeft = null;
        if (tariff.Type == TariffType.TimeBased && tariff.CountdownMode == CountdownMode.UsageOnly && allowance.RemainingSeconds is { } remaining)
        {
            // Time taken since the engine last charged it (less than a second normally).
            var pending = allowance.LastConsumedAt is { } last && now > last ? (long)(now - last).TotalSeconds : 0;
            quotaLeft = Math.Max(0, remaining - pending);
        }

        long? secondsLeft = (quotaLeft, untilEnd) switch
        {
            ({ } q, { } u) => Math.Min(q, u),
            ({ } q, null) => q,
            (null, { } u) => u,
            _ => null,
        };

        return new SessionStatus(session, tariff, session.Voucher?.Code, session.UserAccount?.Username,
            secondsLeft, allowance.RemainingBytes, endsAt, session.UserAccount?.Balance);
    }

    /// <summary>
    /// Staff gives a running session <paramref name="minutes"/> more: added to a time quota, and the end date (if
    /// any) moves by the same amount. Returns false when there is nothing time-based to extend.
    /// </summary>
    public async Task<bool> AddTimeAsync(long sessionId, int minutes, Staff? actor, CancellationToken ct = default)
    {
        if (minutes is < 1 or > 7 * 24 * 60)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Minutes must be between 1 and 10080.");
        var session = await ActiveSessions().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return false;

        var allowance = session.Voucher?.Allowance ?? session.UserAccount?.Allowance;
        var tariff = session.Voucher?.Tariff ?? session.UserAccount?.Tariff;
        if (allowance is null || tariff is null)
            return false;

        var extra = TimeSpan.FromMinutes(minutes);
        var changed = false;
        if (allowance.ExpiresAt is { } expires)
        {
            allowance.ExpiresAt = expires + extra;
            changed = true;
        }
        if (tariff.Type == TariffType.TimeBased && allowance.RemainingSeconds is { } remaining)
        {
            allowance.RemainingSeconds = remaining + (long)extra.TotalSeconds;
            changed = true;
        }
        if (!changed)
            return false;

        audit.Record(actor, "Session.TimeAdded", $"{session.Device.Mac}: +{minutes} min (session {session.Id})");
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The device's last session if it ended within <paramref name="within"/> (to tell the customer why).</summary>
    public async Task<Session?> GetRecentlyEndedAsync(string mac, TimeSpan within, CancellationToken ct = default)
    {
        if (!MacAddress.TryNormalize(mac, out var normalized))
            return null;
        var since = Now - within;
        if (await db.Sessions.AnyAsync(s => s.EndedAt == null && s.Device.Mac == normalized, ct))
            return null;
        return await db.Sessions.AsNoTracking()
            .Where(s => s.Device.Mac == normalized && s.EndedAt != null && s.EndedAt >= since)
            .OrderByDescending(s => s.EndedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<LoginResult> LoginWithVoucherAsync(string mac, string? ip, string code, CancellationToken ct = default) =>
        LoginWithVoucherAsync(mac, ip, code, LoginMethod.Voucher, ct);

    /// <summary>Voucher login; free, password and SMS logins use their own hidden voucher and say so in <paramref name="method"/>.</summary>
    internal async Task<LoginResult> LoginWithVoucherAsync(string mac, string? ip, string code, LoginMethod method, CancellationToken ct)
    {
        var now = Now;
        var device = await devices.TouchAsync(mac, ip, null, ct);
        if (await CheckDeviceAsync(device, now, ct) is { } refused)
            return refused;

        var normalized = VoucherCodeGenerator.Normalize(code);
        var voucher = normalized.Length == 0 ? null
            : await db.Vouchers.Include(v => v.Tariff).FirstOrDefaultAsync(v => v.Code == normalized, ct);
        if (voucher is null)
            return await FailAsync(device, LoginStatus.InvalidCode, now, ct);
        LoginAttemptLimiter.RegisterSuccess(device);

        switch (voucher.Status)
        {
            case VoucherStatus.Disabled:
                return await RefuseAsync(LoginStatus.VoucherDisabled, ct);
            case VoucherStatus.Expired:
                return await RefuseAsync(AllowanceRules.Check(voucher.Allowance, now) == SessionEndReason.QuotaUsed
                    ? LoginStatus.QuotaUsed : LoginStatus.VoucherExpired, ct);
        }

        if (AllowanceRules.Check(voucher.Allowance, now) is { } ended)
        {
            voucher.Status = VoucherStatus.Expired;
            return await RefuseAsync(ended == SessionEndReason.QuotaUsed ? LoginStatus.QuotaUsed : LoginStatus.VoucherExpired, ct);
        }

        var others = await db.Sessions.Where(s => s.EndedAt == null && s.VoucherId == voucher.Id).ToListAsync(ct);
        if (others.FirstOrDefault(s => s.DeviceId == device.Id) is { } existing)
            return await ReuseAsync(existing, ip, ct);
        if (others.Count >= voucher.Tariff.MaxDevices)
            return await RefuseAsync(LoginStatus.TooManyDevices, ct);

        if (voucher.Status != VoucherStatus.Active)
        {
            voucher.Status = VoucherStatus.Active;
            voucher.FirstUsedAt = now;
            AllowanceRules.Start(voucher.Allowance, voucher.Tariff, now);
        }
        else if (others.Count == 0)
        {
            // Usage-only time was paused while nobody was logged in: restart the clock now.
            AllowanceRules.Resume(voucher.Allowance, now);
        }

        var session = await StartAsync(device, method, voucher.TariffId, ip, now, ct);
        session.VoucherId = voucher.Id;
        await db.SaveChangesAsync(ct);
        return new LoginResult(LoginStatus.Success, session);
    }

    public async Task<LoginResult> LoginWithAccountAsync(string mac, string? ip, string username, string password, CancellationToken ct = default)
    {
        var now = Now;
        var device = await devices.TouchAsync(mac, ip, null, ct);
        if (await CheckDeviceAsync(device, now, ct) is { } refused)
            return refused;

        var account = await accounts.FindByUsernameAsync(username ?? "", ct);
        if (account is null || !accounts.VerifyPassword(account, password ?? ""))
            return await FailAsync(device, LoginStatus.InvalidCredentials, now, ct);
        LoginAttemptLimiter.RegisterSuccess(device);

        if (!account.Enabled)
            return await RefuseAsync(LoginStatus.AccountDisabled, ct);
        if (account.ExpiresAt <= now)
            return await RefuseAsync(LoginStatus.AccountExpired, ct);

        var others = await db.Sessions.Where(s => s.EndedAt == null && s.UserAccountId == account.Id).ToListAsync(ct);
        if (others.FirstOrDefault(s => s.DeviceId == device.Id) is { } existing)
            return await ReuseAsync(existing, ip, ct);
        if (others.Count >= account.Tariff.MaxDevices)
            return await RefuseAsync(LoginStatus.TooManyDevices, ct);

        if (!account.Allowance.IsStarted || AllowanceRules.Check(account.Allowance, now) is not null)
        {
            if (!await accounts.TryBuyPackageAsync(account, ct))
                return await RefuseAsync(LoginStatus.InsufficientBalance, ct);
        }
        else if (others.Count == 0)
        {
            AllowanceRules.Resume(account.Allowance, now);
        }

        var session = await StartAsync(device, LoginMethod.Account, account.TariffId, ip, now, ct);
        session.UserAccountId = account.Id;
        await db.SaveChangesAsync(ct);
        return new LoginResult(LoginStatus.Success, session);
    }

    /// <summary>Ends a session on request: the customer logs out, or staff kicks the device.</summary>
    public async Task<bool> EndAsync(long sessionId, SessionEndReason reason, Staff? actor = null, CancellationToken ct = default)
    {
        var session = await ActiveSessions().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return false;

        End(session, reason, Now);
        if (reason == SessionEndReason.KickedByStaff)
            audit.Record(actor, "Session.Kicked", $"{session.Device.Mac} (session {session.Id})");
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Counts time and traffic for every active session and ends those whose time, data, validity or idle
    /// timeout has run out. Returns the sessions that ended in this tick (the packet gate blocks them).
    /// </summary>
    public async Task<IReadOnlyList<Session>> TickAsync(IReadOnlyDictionary<long, TrafficDelta> traffic, CancellationToken ct = default)
    {
        var now = Now;
        var active = await ActiveSessions().ToListAsync(ct);
        if (active.Count == 0)
            return [];

        var ended = new List<Session>();

        foreach (var session in active)
        {
            var delta = traffic.GetValueOrDefault(session.Id);
            session.BytesUp += delta.BytesUp;
            session.BytesDown += delta.BytesDown;
            if (delta.Total > 0)
                session.LastActivityAt = now;

            if (now > session.LastAccountedAt)
            {
                var elapsed = (long)(now - session.LastAccountedAt).TotalSeconds;
                session.SecondsUsed += elapsed;
                session.LastAccountedAt = session.LastAccountedAt.AddSeconds(elapsed);
            }
        }

        // Time and data are taken once per voucher/account, however many of its devices are online.
        foreach (var group in active.Where(s => s.Voucher is not null).GroupBy(s => s.Voucher!))
        {
            var voucher = group.Key;
            AllowanceRules.Consume(voucher.Allowance, voucher.Tariff, group.Sum(s => traffic.GetValueOrDefault(s.Id).Total), now);
            if (AllowanceRules.Check(voucher.Allowance, now) is { } reason)
            {
                voucher.Status = VoucherStatus.Expired;
                EndAll(group, reason, now, ended);
            }
        }

        foreach (var group in active.Where(s => s.UserAccount is not null).GroupBy(s => s.UserAccount!))
        {
            var account = group.Key;
            if (!account.Enabled)
            {
                EndAll(group, SessionEndReason.KickedByStaff, now, ended);
                continue;
            }
            if (account.ExpiresAt <= now)
            {
                EndAll(group, SessionEndReason.Expired, now, ended);
                continue;
            }

            AllowanceRules.Consume(account.Allowance, account.Tariff, group.Sum(s => traffic.GetValueOrDefault(s.Id).Total), now);
            if (AllowanceRules.Check(account.Allowance, now) is { } reason && !await accounts.TryBuyPackageAsync(account, ct))
                EndAll(group, reason, now, ended);
        }

        var defaultIdle = await settings.GetIntAsync(SettingKeys.DefaultIdleTimeoutMinutes, 10, ct);
        foreach (var session in active.Where(s => s.IsActive))
        {
            if (session.Device.Blocked)
            {
                End(session, SessionEndReason.KickedByStaff, now);
                ended.Add(session);
                continue;
            }

            var idle = TimeSpan.FromMinutes(session.Tariff.IdleTimeoutMinutes ?? defaultIdle);
            if (now - session.LastActivityAt >= idle)
            {
                End(session, SessionEndReason.Idle, now);
                ended.Add(session);
            }
        }

        await db.SaveChangesAsync(ct);
        return ended;
    }

    /// <summary>
    /// Called when the service starts: sessions that were active when it stopped carry on, and the time the
    /// service was down is not charged to usage-only tariffs. Expired ones end at the next tick.
    /// </summary>
    public async Task<IReadOnlyList<Session>> ResumeAfterRestartAsync(CancellationToken ct = default)
    {
        var now = Now;
        var active = await ActiveSessions().ToListAsync(ct);
        foreach (var session in active)
        {
            session.LastAccountedAt = now;
            session.LastActivityAt = now;
            if (session.Voucher is not null)
                AllowanceRules.Resume(session.Voucher.Allowance, now);
            if (session.UserAccount is not null)
                AllowanceRules.Resume(session.UserAccount.Allowance, now);
        }
        await db.SaveChangesAsync(ct);
        return active;
    }

    private IQueryable<Session> ActiveSessions() =>
        db.Sessions
            .Include(s => s.Device)
            .Include(s => s.Tariff)
            .Include(s => s.Voucher).ThenInclude(v => v!.Tariff)
            .Include(s => s.UserAccount).ThenInclude(a => a!.Tariff)
            .Where(s => s.EndedAt == null);

    internal async Task<LoginResult?> CheckDeviceAsync(Device device, DateTime now, CancellationToken ct)
    {
        if (device.Blocked)
            return await RefuseAsync(LoginStatus.DeviceBlocked, ct);
        if (LoginAttemptLimiter.IsLocked(device, now))
            return await RefuseAsync(LoginStatus.LockedOut, ct, device.LockedUntil);
        return null;
    }

    internal async Task<LoginResult> FailAsync(Device device, LoginStatus status, DateTime now, CancellationToken ct) =>
        LoginAttemptLimiter.RegisterFailure(device, now)
            ? await RefuseAsync(LoginStatus.LockedOut, ct, device.LockedUntil)
            : await RefuseAsync(status, ct);

    /// <summary>Saves what changed so far (device seen, failed attempts, voucher marked expired) and refuses.</summary>
    internal async Task<LoginResult> RefuseAsync(LoginStatus status, CancellationToken ct, DateTime? lockedUntil = null)
    {
        await db.SaveChangesAsync(ct);
        return new LoginResult(status, LockedUntil: lockedUntil);
    }

    private async Task<LoginResult> ReuseAsync(Session existing, string? ip, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(ip))
            existing.Ip = ip;
        await db.SaveChangesAsync(ct);
        return new LoginResult(LoginStatus.Success, existing);
    }

    /// <summary>Creates the session; a session of the same device on another voucher/account is logged out first.</summary>
    private async Task<Session> StartAsync(Device device, LoginMethod method, int tariffId, string? ip, DateTime now, CancellationToken ct)
    {
        var previous = await ActiveSessions().Where(s => s.DeviceId == device.Id).ToListAsync(ct);
        foreach (var old in previous)
            End(old, SessionEndReason.Logout, now);

        var session = new Session
        {
            Device = device,
            Method = method,
            TariffId = tariffId,
            Ip = ip,
            StartedAt = now,
            LastActivityAt = now,
            LastAccountedAt = now,
        };
        db.Sessions.Add(session);
        return session;
    }

    private static void EndAll(IEnumerable<Session> sessions, SessionEndReason reason, DateTime now, List<Session> ended)
    {
        foreach (var session in sessions.Where(s => s.IsActive))
        {
            End(session, reason, now);
            ended.Add(session);
        }
    }

    private static void End(Session session, SessionEndReason reason, DateTime now)
    {
        session.EndedAt = now;
        session.EndReason = reason;
        if (session.Voucher is { SingleUse: true } voucher)
            voucher.Status = VoucherStatus.Expired;
    }
}

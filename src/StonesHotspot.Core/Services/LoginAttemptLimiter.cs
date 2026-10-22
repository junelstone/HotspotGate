using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

/// <summary>
/// Wrong voucher codes / passwords per device: after <see cref="MaxAttempts"/> failures within
/// <see cref="Window"/> the device is locked for <see cref="Lockout"/>. Stored on the device row.
/// </summary>
public static class LoginAttemptLimiter
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);

    public static bool IsLocked(Device device, DateTime now) => device.LockedUntil > now;

    /// <returns>True when this failure locked the device.</returns>
    public static bool RegisterFailure(Device device, DateTime now)
    {
        if (device.FailedLoginWindowStart is not { } start || now - start > Window)
        {
            device.FailedLoginWindowStart = now;
            device.FailedLoginCount = 0;
        }

        device.FailedLoginCount++;
        if (device.FailedLoginCount < MaxAttempts)
            return false;

        device.LockedUntil = now + Lockout;
        device.FailedLoginCount = 0;
        device.FailedLoginWindowStart = null;
        return true;
    }

    public static void RegisterSuccess(Device device)
    {
        device.FailedLoginCount = 0;
        device.FailedLoginWindowStart = null;
        device.LockedUntil = null;
    }
}

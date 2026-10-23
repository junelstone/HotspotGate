using System.Collections.Concurrent;

namespace StonesHotspot.Web.Auth;

/// <summary>Staff sign-in: 5 wrong passwords from one address within 15 minutes → 5 minutes wait.</summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);

    private sealed record Entry(int Failures, DateTimeOffset WindowStart, DateTimeOffset? LockedUntil);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    /// <summary>When the address may try again, or null if it may try now.</summary>
    public DateTimeOffset? LockedUntil(string key)
    {
        var now = clock.GetUtcNow();
        return _entries.TryGetValue(key, out var e) && e.LockedUntil > now ? e.LockedUntil : null;
    }

    public void Failure(string key)
    {
        var now = clock.GetUtcNow();
        _entries.AddOrUpdate(key,
            _ => new Entry(1, now, null),
            (_, e) =>
            {
                var entry = now - e.WindowStart > Window || e.LockedUntil <= now ? new Entry(0, now, null) : e;
                var failures = entry.Failures + 1;
                return failures >= MaxFailures ? new Entry(0, now, now + Lockout) : entry with { Failures = failures };
            });
    }

    public void Success(string key) => _entries.TryRemove(key, out _);
}

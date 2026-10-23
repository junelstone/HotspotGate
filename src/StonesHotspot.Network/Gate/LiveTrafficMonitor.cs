using System.Collections.Concurrent;

namespace StonesHotspot.Network.Gate;

/// <summary>Current download/upload speed of each session (bits per second, smoothed), for the status page.</summary>
public sealed class LiveTrafficMonitor
{
    private const double Smoothing = 0.5;
    private readonly ConcurrentDictionary<long, (double DownBps, double UpBps)> _speeds = new();

    /// <summary>Called by the engine with the bytes counted over <paramref name="elapsed"/>.</summary>
    public void Record(IReadOnlyDictionary<long, (long Up, long Down)> bytesBySession, IEnumerable<long> activeSessions, TimeSpan elapsed)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.05);
        var active = activeSessions.ToHashSet();
        foreach (var id in active)
        {
            var (up, down) = bytesBySession.GetValueOrDefault(id);
            var current = (Down: down * 8 / seconds, Up: up * 8 / seconds);
            _speeds.AddOrUpdate(id, current, (_, old) =>
                (old.DownBps + Smoothing * (current.Down - old.DownBps), old.UpBps + Smoothing * (current.Up - old.UpBps)));
        }
        foreach (var id in _speeds.Keys.Where(id => !active.Contains(id)))
            _speeds.TryRemove(id, out _);
    }

    public (double DownBps, double UpBps) For(long sessionId) => _speeds.GetValueOrDefault(sessionId);
}

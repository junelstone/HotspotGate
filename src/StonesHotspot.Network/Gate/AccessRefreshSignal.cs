namespace StonesHotspot.Network.Gate;

/// <summary>
/// Wakes the engine before its next 1-second tick, so a login or logout reaches the gate within milliseconds
/// (the portal redirects the customer right after logging in).
/// </summary>
public sealed class AccessRefreshSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _rulesChanged;

    /// <summary>Web-filter or bandwidth settings changed: reload them at the next (immediate) tick.</summary>
    public void RequestRulesReload()
    {
        Interlocked.Exchange(ref _rulesChanged, 1);
        Request();
    }

    /// <summary>True once after <see cref="RequestRulesReload"/>.</summary>
    public bool TakeRulesReload() => Interlocked.Exchange(ref _rulesChanged, 0) == 1;

    public void Request()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // already requested
        }
    }

    /// <summary>Waits until a refresh is requested or <paramref name="timeout"/> passes. True when requested.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

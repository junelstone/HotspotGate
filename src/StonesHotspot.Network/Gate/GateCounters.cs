namespace StonesHotspot.Network.Gate;

/// <summary>Packet statistics for the diagnostics page (since the gate started).</summary>
public sealed class GateCounters
{
    private long _passedUp, _passedDown, _dropped, _resets, _redirected, _dnsQueries, _dnsBlocked, _freeAccess, _injectErrors;

    public long PassedUp => Interlocked.Read(ref _passedUp);
    public long PassedDown => Interlocked.Read(ref _passedDown);
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Resets => Interlocked.Read(ref _resets);
    public long Redirected => Interlocked.Read(ref _redirected);
    public long DnsQueries => Interlocked.Read(ref _dnsQueries);
    public long DnsBlocked => Interlocked.Read(ref _dnsBlocked);
    public long FreeAccess => Interlocked.Read(ref _freeAccess);
    public long InjectErrors => Interlocked.Read(ref _injectErrors);

    internal void PassUp() => Interlocked.Increment(ref _passedUp);
    internal void PassDown() => Interlocked.Increment(ref _passedDown);
    internal void Drop() => Interlocked.Increment(ref _dropped);
    internal void Reset() => Interlocked.Increment(ref _resets);
    internal void Redirect() => Interlocked.Increment(ref _redirected);
    internal void DnsQuery() => Interlocked.Increment(ref _dnsQueries);
    internal void DnsBlock() => Interlocked.Increment(ref _dnsBlocked);
    internal void Free() => Interlocked.Increment(ref _freeAccess);
    public void InjectError() => Interlocked.Increment(ref _injectErrors);
}

/// <summary>Bytes of one authorised client IP since the engine last collected them.</summary>
public sealed class ByteCounter
{
    private long _up, _down;

    internal void AddUp(int bytes) => Interlocked.Add(ref _up, bytes);
    internal void AddDown(int bytes) => Interlocked.Add(ref _down, bytes);

    internal (long Up, long Down) Take() => (Interlocked.Exchange(ref _up, 0), Interlocked.Exchange(ref _down, 0));
}

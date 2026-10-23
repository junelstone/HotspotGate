using System.Collections.Concurrent;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Gate.Packets;

namespace StonesHotspot.Network.Gate;

/// <summary>Which WinDivert handle captured the packet.</summary>
public enum PacketSource
{
    /// <summary>NETWORK_FORWARD layer: traffic between customers and the internet.</summary>
    Forward,

    /// <summary>NETWORK layer on the LAN interface: traffic to/from the PC itself.</summary>
    Local,
}

public enum GateVerdict
{
    /// <summary>Re-inject the packet unchanged.</summary>
    Pass,

    /// <summary>Re-inject the packet, which was rewritten in place (portal reply going back to the device).</summary>
    PassModified,

    /// <summary>Do not re-inject.</summary>
    Drop,

    /// <summary>Drop it and send the device a TCP reset (<see cref="Ipv4Packet.BuildTcpReset"/>).</summary>
    Reset,

    /// <summary>The packet was rewritten to the portal: inject it inbound on the LAN interface (local delivery).</summary>
    RedirectToPortal,

    /// <summary>A DNS query: drop it and hand a copy to the DNS responder, which injects the answer.</summary>
    Dns,

    /// <summary>Over the device's speed limit: re-inject unchanged after the returned delay (traffic shaping).</summary>
    Delay,
}

/// <summary>
/// The access rules of the hotspot (see docs/phase-3-plan.md) as a pure function: packet in, verdict out. Holds
/// the current <see cref="GateSnapshot"/>, the HTTP-redirect (destination NAT) table, the free-access IPs learnt
/// from DNS and the byte counters. Thread-safe; WinDivert receive loops call <see cref="Process"/>.
/// </summary>
public sealed class PacketProcessor(TimeProvider clock)
{
    private const ushort HttpPort = 80;
    private const ushort DnsPort = 53;
    private const ushort DhcpServerPort = 67;
    public static readonly TimeSpan RedirectIdleTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MinLearnedTtl = TimeSpan.FromMinutes(5);

    private volatile GateSnapshot? _snapshot;
    private readonly ConcurrentDictionary<ulong, RedirectFlow> _redirects = new();
    private readonly ConcurrentDictionary<uint, long> _learnedFree = new();
    private readonly ConcurrentDictionary<uint, ByteCounter> _bytes = new();
    private readonly ConcurrentDictionary<uint, ShaperState> _shapers = new();

    /// <summary>How far ahead of its rate a device may send at once.</summary>
    public static readonly TimeSpan BurstTolerance = TimeSpan.FromMilliseconds(20);

    /// <summary>Packets that would wait longer than this are dropped, so TCP slows down instead of queueing.</summary>
    public static readonly TimeSpan MaxQueueDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>GCRA "theoretical arrival time" per direction, in UTC ticks.</summary>
    private sealed class ShaperState
    {
        public long TatUp;
        public long TatDown;
    }

    private sealed class RedirectFlow(uint originalAddress, ushort originalPort, long lastUsed)
    {
        public uint OriginalAddress { get; } = originalAddress;
        public ushort OriginalPort { get; } = originalPort;
        public long LastUsed = lastUsed;
    }

    public GateCounters Counters { get; private set; } = new();

    public GateSnapshot? Snapshot => _snapshot;

    public int RedirectFlowCount => _redirects.Count;
    public int LearnedFreeCount => _learnedFree.Count;

    /// <summary>Swaps in a new rule snapshot; the next packet uses it.</summary>
    public void Update(GateSnapshot snapshot) => _snapshot = snapshot;

    /// <summary>Clears all state (new LAN subnet, gate restarted).</summary>
    public void Reset(GateSnapshot snapshot)
    {
        _redirects.Clear();
        _learnedFree.Clear();
        _bytes.Clear();
        _shapers.Clear();
        Counters = new GateCounters();
        _snapshot = snapshot;
    }

    public GateVerdict Process(Span<byte> packet, PacketSource source, bool outbound) => Process(packet, source, outbound, out _);

    /// <param name="delay">With <see cref="GateVerdict.Delay"/>: how long to hold the packet before re-injecting it.</param>
    public GateVerdict Process(Span<byte> packet, PacketSource source, bool outbound, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        var snapshot = _snapshot;
        if (snapshot is null || !Ipv4Packet.TryParse(packet, out var p))
            return Drop();

        return source == PacketSource.Forward
            ? ProcessForward(packet, p, snapshot, out delay)
            : ProcessLocal(packet, p, snapshot, outbound);
    }

    private GateVerdict ProcessForward(Span<byte> packet, in PacketInfo p, GateSnapshot s, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        if (s.IsClient(p.Source))
        {
            if (IsBroadcastOrMulticast(p.Destination, s))
                return GateVerdict.Pass;

            // Every DNS query goes through our responder, whatever server the device asked.
            if (p.IsUdp && p.DestinationPort == DnsPort)
            {
                Counters.DnsQuery();
                return GateVerdict.Dns;
            }

            if (s.Rules.BlockedIps.Contains(p.Destination))
                return Refuse(p);

            if (s.Authorized.TryGetValue(p.Source, out var sender))
                return Authorized(p.Source, upload: true, p.TotalLength, sender.UpKbps, out delay);

            if (IsFree(p.Destination, s))
            {
                Counters.Free();
                return GateVerdict.Pass;
            }

            if (p.IsTcp && p.DestinationPort == HttpPort)
                return RedirectToPortal(packet, p, s);

            return Refuse(p);
        }

        if (s.IsClient(p.Destination))
        {
            if (s.Authorized.TryGetValue(p.Destination, out var receiver) && !s.Rules.BlockedIps.Contains(p.Source))
                return Authorized(p.Destination, upload: false, p.TotalLength, receiver.DownKbps, out delay);
            if (IsFree(p.Source, s))
            {
                Counters.Free();
                return GateVerdict.Pass;
            }
            return Drop();
        }

        return GateVerdict.Pass; // not customer traffic (the filter should not capture it)
    }

    /// <summary>Traffic of an authorised device: shaped to its limit, counted when it is delivered.</summary>
    private GateVerdict Authorized(uint client, bool upload, int bytes, int? kbps, out TimeSpan delay)
    {
        var verdict = Shape(client, upload, bytes, kbps, out delay);
        if (verdict == GateVerdict.Drop)
        {
            Counters.ShapedDrop();
            return GateVerdict.Drop;
        }

        var counter = CounterFor(client);
        counter.Touch(clock.GetUtcNow().UtcTicks);
        if (upload)
        {
            counter.AddUp(bytes);
            Counters.PassUp();
        }
        else
        {
            counter.AddDown(bytes);
            Counters.PassDown();
        }
        if (verdict == GateVerdict.Delay)
            Counters.Shaped();
        return verdict;
    }

    /// <summary>
    /// GCRA shaper (virtual scheduling): a packet within the rate passes, a packet up to <see cref="MaxQueueDelay"/>
    /// over is delayed, anything later is dropped. Long-run throughput equals the limit.
    /// </summary>
    internal GateVerdict Shape(uint client, bool upload, int bytes, int? kbps, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        if (kbps is not > 0)
            return GateVerdict.Pass;

        var state = _shapers.GetOrAdd(client, static _ => new ShaperState());
        var now = clock.GetUtcNow().UtcTicks;
        var cost = bytes * 8L * TimeSpan.TicksPerSecond / (kbps.Value * 1000L);
        lock (state)
        {
            ref var tat = ref upload ? ref state.TatUp : ref state.TatDown;
            if (tat < now)
                tat = now;
            var wait = tat - now - BurstTolerance.Ticks;
            if (wait > MaxQueueDelay.Ticks)
                return GateVerdict.Drop;
            tat += cost;
            if (wait <= 0)
                return GateVerdict.Pass;
            delay = TimeSpan.FromTicks(wait);
            return GateVerdict.Delay;
        }
    }

    /// <summary>Authorised devices that sent or received traffic within <paramref name="within"/> (fair share).</summary>
    public IReadOnlyCollection<uint> RecentlyActive(TimeSpan within)
    {
        var since = clock.GetUtcNow().UtcTicks - within.Ticks;
        return _bytes.Where(kv => kv.Value.LastActivity >= since).Select(kv => kv.Key).ToList();
    }

    private GateVerdict ProcessLocal(Span<byte> packet, in PacketInfo p, GateSnapshot s, bool outbound)
    {
        if (!outbound)
        {
            // Device → gateway: DHCP and the portal pass; DNS is answered by us (not by Windows' ICS DNS proxy).
            if (p.IsUdp && p.DestinationPort == DnsPort && s.IsClient(p.Source))
            {
                Counters.DnsQuery();
                return GateVerdict.Dns;
            }
            return GateVerdict.Pass;
        }

        // Gateway → device: the portal's half of a redirected HTTP connection must look like it comes from the
        // web server the device asked for.
        if (p.IsTcp && p.Source == s.Gateway && p.SourcePort == s.PortalPort
            && _redirects.TryGetValue(FlowKey(p.Destination, p.DestinationPort), out var flow))
        {
            flow.LastUsed = Now;
            Ipv4Packet.SetSource(packet, flow.OriginalAddress, flow.OriginalPort);
            return GateVerdict.PassModified;
        }
        return GateVerdict.Pass;
    }

    private GateVerdict RedirectToPortal(Span<byte> packet, in PacketInfo p, GateSnapshot s)
    {
        var destination = p.Destination;
        var destinationPort = p.DestinationPort;
        var now = Now;
        _redirects.AddOrUpdate(FlowKey(p.Source, p.SourcePort),
            _ => new RedirectFlow(destination, destinationPort, now),
            (_, existing) =>
            {
                // Same client port reused for another server: start a new flow.
                if (existing.OriginalAddress != destination)
                    return new RedirectFlow(destination, destinationPort, now);
                existing.LastUsed = now;
                return existing;
            });

        Ipv4Packet.SetDestination(packet, s.Gateway, s.PortalPort);
        if (p.HasFlag(TcpFlags.Syn))
            Counters.Redirect();
        return GateVerdict.RedirectToPortal;
    }

    private GateVerdict Refuse(in PacketInfo p)
    {
        // A reset makes the device give up at once; never answer a reset with a reset.
        if (p.IsTcp && !p.HasFlag(TcpFlags.Rst))
        {
            Counters.Reset();
            return GateVerdict.Reset;
        }
        return Drop();
    }

    private GateVerdict Drop()
    {
        Counters.Drop();
        return GateVerdict.Drop;
    }

    private bool IsFree(uint address, GateSnapshot s) =>
        s.Rules.FreeIps.Contains(address)
        || (_learnedFree.TryGetValue(address, out var expires) && expires > Now);

    private static bool IsBroadcastOrMulticast(uint address, GateSnapshot s) =>
        address == 0xFFFFFFFF || address == s.Broadcast || (address & 0xF0000000) == 0xE0000000;

    /// <summary>Called by the DNS responder for each A record of a free-access domain.</summary>
    public void LearnFreeAddress(uint address, TimeSpan ttl)
    {
        var expires = Now + (long)(ttl < MinLearnedTtl ? MinLearnedTtl : ttl).TotalMilliseconds;
        _learnedFree.AddOrUpdate(address, expires, (_, old) => Math.Max(old, expires));
    }

    /// <summary>Bytes per client IP since the previous call (the engine charges them to sessions).</summary>
    public IReadOnlyDictionary<uint, (long Up, long Down)> TakeByteCounts()
    {
        var result = new Dictionary<uint, (long, long)>();
        foreach (var (ip, counter) in _bytes)
        {
            var taken = counter.Take();
            if (taken.Up > 0 || taken.Down > 0)
                result[ip] = taken;
        }
        return result;
    }

    /// <summary>Forgets idle redirect flows, expired learnt IPs and counters of IPs no longer authorised.</summary>
    public void Maintain()
    {
        var now = Now;
        foreach (var (key, flow) in _redirects)
        {
            if (now - flow.LastUsed > (long)RedirectIdleTimeout.TotalMilliseconds)
                _redirects.TryRemove(key, out _);
        }
        foreach (var (ip, expires) in _learnedFree)
        {
            if (expires <= now)
                _learnedFree.TryRemove(ip, out _);
        }
        var authorized = _snapshot?.Authorized;
        foreach (var ip in _bytes.Keys)
        {
            if (authorized is null || !authorized.ContainsKey(ip))
                _bytes.TryRemove(ip, out _);
        }
        foreach (var ip in _shapers.Keys)
        {
            if (authorized is null || !authorized.ContainsKey(ip))
                _shapers.TryRemove(ip, out _);
        }
    }

    private ByteCounter CounterFor(uint ip) => _bytes.GetOrAdd(ip, static _ => new ByteCounter());

    private static ulong FlowKey(uint address, ushort port) => (ulong)address << 16 | port;

    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();

    public static uint ToUInt32(System.Net.IPAddress address) => Ipv4Subnet.ToUInt32(address);
}

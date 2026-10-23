using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Dns;
using StonesHotspot.Network.Gate.Packets;
using static StonesHotspot.Network.Gate.WinDivert.WinDivertNative;

namespace StonesHotspot.Network.Gate.WinDivert;

/// <summary>
/// The packet gate on Windows: two WinDivert handles (forward layer for customer ↔ internet traffic, network
/// layer on the LAN interface for traffic to the PC) feeding <see cref="PacketProcessor"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinDivertPacketGate(
    PacketProcessor processor, DnsResponder dns, TimeProvider clock, ILogger<WinDivertPacketGate> logger) : IPacketGate, IDisposable
{
    private const int ForwardThreads = 2;

    private ILogger Logger { get; } = logger;
    private DnsResponder Dns { get; } = dns;

    private readonly object _lock = new();
    private Session? _session;

    public GateStatus Status { get; private set; } = GateStatus.Stopped;

    public PacketProcessor Processor => processor;

    public bool Start(GateConfig config)
    {
        lock (_lock)
        {
            if (_session is not null && _session.Config == config)
                return true;

            var lanChanged = _session?.Config.Lan != config.Lan;
            Session next;
            try
            {
                // The new handles are opened before the old ones close, so there is never a moment without filter.
                next = Session.Open(config, this);
            }
            catch (Exception ex) when (ex is GateStartException or DllNotFoundException or EntryPointNotFoundException)
            {
                var message = ex is GateStartException ? ex.Message : DescribeOpenError(2);
                logger.LogError(ex, "Packet gate could not start: {Reason}", message);
                _session?.Dispose();
                _session = null;
                Status = new GateStatus(GateState.Failed, config, message, clock.GetUtcNow().UtcDateTime);
                return false;
            }

            if (lanChanged || processor.Snapshot is null)
                processor.Reset(GateSnapshot.Closed(config.Lan, config.PortalPort));

            var old = _session;
            _session = next;
            next.StartLoops();
            old?.Dispose();

            Status = new GateStatus(GateState.Running, config, null, clock.GetUtcNow().UtcDateTime);
            logger.LogInformation("Packet gate running on interface {IfIdx}, network {Lan}, portal port {Port}",
                config.LanInterfaceIndex, config.Lan, config.PortalPort);
            return true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_session is null)
                return;
            _session.Dispose();
            _session = null;
            Status = GateStatus.Stopped;
            logger.LogInformation("Packet gate stopped");
        }
    }

    public void Dispose() => Stop();

    private sealed class GateStartException(string message) : Exception(message);

    /// <summary>One set of open handles and their threads.</summary>
    private sealed class Session : IPacketInjector, IDisposable
    {
        private readonly WinDivertPacketGate _gate;
        private readonly IntPtr _forward;
        private readonly IntPtr _local;
        private readonly uint _gateway;
        private readonly List<Thread> _threads = [];
        private readonly CancellationTokenSource _stopping = new();

        public GateConfig Config { get; }

        private Session(GateConfig config, WinDivertPacketGate gate, IntPtr forward, IntPtr local)
        {
            Config = config;
            _gate = gate;
            _forward = forward;
            _local = local;
            _gateway = Ipv4Subnet.ToUInt32(config.Lan.Address);
        }

        public static Session Open(GateConfig config, WinDivertPacketGate gate)
        {
            var (forwardFilter, localFilter) = GateFilters.Build(config);
            var forward = OpenHandle(forwardFilter, LayerNetworkForward);
            IntPtr local;
            try
            {
                local = OpenHandle(localFilter, LayerNetwork);
            }
            catch
            {
                WinDivertClose(forward);
                throw;
            }
            return new Session(config, gate, forward, local);
        }

        private static IntPtr OpenHandle(string filter, byte layer)
        {
            var handle = WinDivertOpen(filter, layer, 0, 0);
            if (handle == InvalidHandle)
                throw new GateStartException(DescribeOpenError(Marshal.GetLastPInvokeError()));
            WinDivertSetParam(handle, ParamQueueLength, 8192);
            WinDivertSetParam(handle, ParamQueueTime, 2000);
            WinDivertSetParam(handle, ParamQueueSize, 8 * 1024 * 1024);
            return handle;
        }

        public void StartLoops()
        {
            for (var i = 0; i < ForwardThreads; i++)
                StartThread($"Gate forward {i + 1}", () => Loop(_forward, PacketSource.Forward));
            StartThread("Gate local", () => Loop(_local, PacketSource.Local));
        }

        private void StartThread(string name, ThreadStart body)
        {
            var thread = new Thread(body) { Name = name, IsBackground = true, Priority = ThreadPriority.AboveNormal };
            _threads.Add(thread);
            thread.Start();
        }

        private unsafe void Loop(IntPtr handle, PacketSource source)
        {
            var buffer = new byte[0xFFFF];
            fixed (byte* packet = buffer)
            {
                while (!_stopping.IsCancellationRequested)
                {
                    if (!WinDivertRecv(handle, packet, (uint)buffer.Length, out var length, out var address))
                    {
                        var error = Marshal.GetLastPInvokeError();
                        if (error == ErrorNoData || _stopping.IsCancellationRequested)
                            return;
                        if (error != ErrorInsufficientBuffer)
                            _gate.Logger.LogWarning("WinDivert receive failed: {Error}", new Win32Exception(error).Message);
                        continue;
                    }

                    var span = new Span<byte>(packet, (int)length);
                    GateVerdict verdict;
                    try
                    {
                        verdict = _gate.Processor.Process(span, source, address.Outbound);
                    }
                    catch (Exception ex)
                    {
                        _gate.Logger.LogError(ex, "Packet processing failed; packet dropped");
                        continue;
                    }

                    switch (verdict)
                    {
                        case GateVerdict.Pass:
                            Send(handle, packet, length, address);
                            break;
                        case GateVerdict.PassModified:
                            address.MarkChecksumsValid();
                            Send(handle, packet, length, address);
                            break;
                        case GateVerdict.RedirectToPortal:
                            // Deliver locally: inbound on the LAN interface, now addressed to the portal.
                            Send(_local, packet, length, WinDivertAddress.For(LayerNetwork, outbound: false, (uint)Config.LanInterfaceIndex));
                            break;
                        case GateVerdict.Reset:
                            InjectToClient(Ipv4Packet.BuildTcpReset(span));
                            break;
                        case GateVerdict.Dns:
                            _gate.Dns.Enqueue(span.ToArray(), this, _stopping.Token);
                            break;
                        case GateVerdict.Drop:
                            break;
                    }
                }
            }
        }

        private unsafe bool Send(IntPtr handle, byte* packet, uint length, in WinDivertAddress address)
        {
            if (WinDivertSend(handle, packet, length, out _, in address))
                return true;
            _gate.Processor.Counters.InjectError();
            return false;
        }

        /// <summary>
        /// Sends a packet we built (DNS answer, TCP reset) to a customer device. From the gateway's own address it
        /// leaves through the local stack; "from the internet" it is injected as forwarded traffic, falling back to
        /// the outbound path.
        /// </summary>
        public unsafe void InjectToClient(byte[] built)
        {
            if (_stopping.IsCancellationRequested)
                return;
            var source = BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(12));
            fixed (byte* packet = built)
            {
                var outbound = WinDivertAddress.For(LayerNetwork, outbound: true, (uint)Config.LanInterfaceIndex);
                if (source == _gateway)
                {
                    Send(_local, packet, (uint)built.Length, outbound);
                    return;
                }
                var forwarded = WinDivertAddress.For(LayerNetworkForward, outbound: true, (uint)Config.LanInterfaceIndex);
                if (!Send(_forward, packet, (uint)built.Length, forwarded))
                    Send(_local, packet, (uint)built.Length, outbound);
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            WinDivertShutdown(_forward, ShutdownBoth);
            WinDivertShutdown(_local, ShutdownBoth);
            foreach (var thread in _threads)
                thread.Join(TimeSpan.FromSeconds(2));
            WinDivertClose(_forward);
            WinDivertClose(_local);
            // _stopping is not disposed: DNS answers still in flight may check it after the handles closed.
        }
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StonesHotspot.Network.Gate.WinDivert;

/// <summary>WINDIVERT_ADDRESS (windivert.h, version 2.2): 80 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct WinDivertAddress
{
    [FieldOffset(0)] public long Timestamp;

    /// <summary>Bits 0-7 Layer, 8-15 Event, 16 Sniffed, 17 Outbound, 18 Loopback, 19 Impostor, 20 IPv6,
    /// 21 IPChecksum, 22 TCPChecksum, 23 UDPChecksum.</summary>
    [FieldOffset(8)] public uint Bits;

    [FieldOffset(16)] public uint IfIdx;
    [FieldOffset(20)] public uint SubIfIdx;

    private const int OutboundBit = 17, ImpostorBit = 19;
    private const uint ChecksumBits = 0b111u << 21;

    public byte Layer
    {
        readonly get => (byte)(Bits & 0xFF);
        set => Bits = (Bits & ~0xFFu) | value;
    }

    public bool Outbound
    {
        readonly get => (Bits & (1u << OutboundBit)) != 0;
        set => Bits = value ? Bits | (1u << OutboundBit) : Bits & ~(1u << OutboundBit);
    }

    public readonly bool Impostor => (Bits & (1u << ImpostorBit)) != 0;

    /// <summary>Declares IP/TCP/UDP checksums valid (we always compute them in full before sending).</summary>
    public void MarkChecksumsValid() => Bits |= ChecksumBits;

    public static WinDivertAddress For(byte layer, bool outbound, uint ifIdx)
    {
        var address = new WinDivertAddress { IfIdx = ifIdx, Layer = layer, Outbound = outbound };
        address.MarkChecksumsValid();
        return address;
    }
}

[SupportedOSPlatform("windows")]
internal static partial class WinDivertNative
{
    public const byte LayerNetwork = 0;
    public const byte LayerNetworkForward = 1;

    public const int ParamQueueLength = 0;
    public const int ParamQueueTime = 1;
    public const int ParamQueueSize = 2;
    public const int ShutdownBoth = 3;

    public const int ErrorNoData = 232;          // handle shut down
    public const int ErrorInsufficientBuffer = 122;

    public static readonly IntPtr InvalidHandle = new(-1);

    [LibraryImport("WinDivert.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [LibraryImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool WinDivertRecv(IntPtr handle, byte* packet, uint packetLength, out uint receivedLength, out WinDivertAddress address);

    [LibraryImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool WinDivertSend(IntPtr handle, byte* packet, uint packetLength, out uint sentLength, in WinDivertAddress address);

    [LibraryImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinDivertShutdown(IntPtr handle, int how);

    [LibraryImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinDivertClose(IntPtr handle);

    [LibraryImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinDivertSetParam(IntPtr handle, int param, ulong value);

    /// <summary>Plain-language reason for a WinDivertOpen failure (shown on the dashboard).</summary>
    public static string DescribeOpenError(int error) => error switch
    {
        2 => "WinDivert driver files (WinDivert.dll / WinDivert64.sys) are missing next to the service.",
        5 => "Access denied: the service must run with administrator rights.",
        87 => "Invalid WinDivert filter or parameter (please report this).",
        577 => "Windows refused the WinDivert driver signature (Secure Boot / driver signing policy).",
        654 => "Another, incompatible WinDivert version is already loaded. Restart the PC.",
        1060 => "The WinDivert driver service is not installed or was removed. Restart the service.",
        1275 => "The WinDivert driver was blocked, usually by antivirus software. Allow it, then restart the service.",
        1753 => "The Windows 'Base Filtering Engine' service is not running.",
        _ => $"WinDivert could not start (Windows error {error}).",
    };
}

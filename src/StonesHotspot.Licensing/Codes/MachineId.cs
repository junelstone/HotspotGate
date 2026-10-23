using System.Security.Cryptography;
using System.Text;

namespace StonesHotspot.Licensing.Codes;

/// <summary>
/// Identifies a PC as four 15-bit fingerprints (Windows MachineGuid, board/BIOS serial, system disk serial, CPU id),
/// written <c>MID-XXXX-XXXX-XXXX</c>. A PC matches this ID when at least 3 of its readable parts are equal, so
/// replacing one part keeps the licence. A part that could not be read is 0 and never matches; on a PC where fewer
/// than 3 parts can be read (placeholder serial numbers on cheap boards), all the readable ones must match.
/// </summary>
public readonly record struct MachineId(ushort Windows, ushort Board, ushort Disk, ushort Cpu)
{
    public const int MinMatchingParts = 3;
    private const string Prefix = "MID-";

    public static MachineId Empty => new(0, 0, 0, 0);

    public IReadOnlyList<ushort> Parts => [Windows, Board, Disk, Cpu];

    public int MatchingParts(MachineId other) => Parts.Zip(other.Parts).Count(p => p.First != 0 && p.First == p.Second);

    public int ReadableParts => Parts.Count(p => p != 0);

    /// <summary>True when <paramref name="other"/> (this PC now) is the PC this ID (from a licence) was made for.</summary>
    public bool Matches(MachineId other) => ReadableParts > 0 && MatchingParts(other) >= Math.Min(MinMatchingParts, ReadableParts);

    /// <summary>Hashes the raw values (salted, so the ID reveals nothing about the hardware).</summary>
    public static MachineId FromRaw(string? windowsId, string? boardSerial, string? diskSerial, string? cpuId)
    {
        static ushort Part(int index, string? value)
        {
            value = value?.Trim();
            if (string.IsNullOrEmpty(value) || IsPlaceholder(value))
                return 0;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"StonesHotspot-mid-v1|{index}|{value.ToUpperInvariant()}"));
            var part = (ushort)(((hash[0] << 8) | hash[1]) & 0x7FFF);
            return part == 0 ? (ushort)1 : part;
        }
        return new MachineId(Part(0, windowsId), Part(1, boardSerial), Part(2, diskSerial), Part(3, cpuId));
    }

    /// <summary>Serial numbers that many PCs share ("To be filled by O.E.M.", "Default string"…) say nothing.</summary>
    private static bool IsPlaceholder(string value)
    {
        var v = value.ToUpperInvariant();
        return v.Contains("O.E.M", StringComparison.Ordinal) || v.Contains("DEFAULT STRING", StringComparison.Ordinal)
               || v is "0" or "NONE" or "N/A" or "NOT APPLICABLE" or "SYSTEM SERIAL NUMBER" or "SERIAL"
               || v.All(c => c is '0' or ' ' or '-' or '.' or 'F');
    }

    /// <summary>The 60 bits as 8 bytes (the last 4 bits are zero).</summary>
    public byte[] ToBytes()
    {
        ulong bits = ((ulong)Windows << 45) | ((ulong)Board << 30) | ((ulong)Disk << 15) | Cpu;
        bits <<= 4;
        var bytes = new byte[8];
        for (var i = 0; i < 8; i++)
            bytes[i] = (byte)(bits >> (56 - 8 * i));
        return bytes;
    }

    public static MachineId FromBytes(ReadOnlySpan<byte> bytes)
    {
        ulong bits = 0;
        for (var i = 0; i < 8; i++)
            bits = (bits << 8) | bytes[i];
        bits >>= 4;
        return new MachineId((ushort)((bits >> 45) & 0x7FFF), (ushort)((bits >> 30) & 0x7FFF), (ushort)((bits >> 15) & 0x7FFF), (ushort)(bits & 0x7FFF));
    }

    /// <summary>"MID-7K3Q-9XPA-2LMD" (12 characters = 60 bits).</summary>
    public override string ToString() => Prefix + Base32.Group(Base32.Encode(ToBytes())[..12], 4);

    public static bool TryParse(string? text, out MachineId id)
    {
        id = Empty;
        text = text?.Trim().ToUpperInvariant();
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var chars = text[Prefix.Length..].Replace("-", "").Replace(" ", "");
        if (chars.Length != 12)
            return false;
        var bytes = Base32.Decode(chars + "AAAA");   // pad to 16 characters = 10 bytes
        if (bytes is null)
            return false;
        id = FromBytes(bytes);
        return true;
    }
}

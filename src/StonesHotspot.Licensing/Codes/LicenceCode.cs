using System.Buffers.Binary;
using System.Text;

namespace StonesHotspot.Licensing.Codes;

/// <summary>What a licence says. Dates are days (UTC); the licence is valid until the end of <see cref="ExpiresOn"/>.</summary>
public sealed record LicenceData(uint Id, Edition Edition, DateOnly IssuedOn, DateOnly ExpiresOn, MachineId MachineId, string Customer)
{
    public const int MaxCustomerBytes = 32;

    /// <summary>First instant after the licence (UTC midnight after the expiry day).</summary>
    public DateTimeOffset EndsAt => new(ExpiresOn.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

/// <summary>
/// The licence code: <c>SHS1-</c> + base32 groups of the licence data and its Ed25519 signature. The same text is the
/// content of a <c>.lic</c> file. Any changed character makes the signature fail.
/// </summary>
public static class LicenceCode
{
    public const string Prefix = "SHS1";
    private const byte FormatVersion = 1;
    private static readonly DateOnly Epoch = new(2024, 1, 1);

    public static string Create(LicenceData licence, byte[] privateKey)
    {
        var payload = Payload(licence);
        var signature = Ed25519.Sign(privateKey, payload);
        return Prefix + "-" + Base32.Group(Base32.Encode([.. payload, .. signature]));
    }

    /// <summary>Reads and checks a code. Null when it is not a well-formed code signed with <paramref name="publicKey"/>.</summary>
    public static LicenceData? Verify(string? text, byte[] publicKey)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        // A .lic file has comment lines (#); a code pasted from WhatsApp may be cut over several lines, in lower case,
        // with spaces: keep the other lines, drop every space.
        var joined = string.Concat(text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')).SelectMany(l => l.Where(c => !char.IsWhiteSpace(c))));
        if (!joined.StartsWith(Prefix + "-", StringComparison.OrdinalIgnoreCase))
            return null;
        var bytes = Base32.Decode(joined[(Prefix.Length + 1)..]);
        if (bytes is null || bytes.Length < 20 + Ed25519.SignatureSize)
            return null;

        // Base32 may add one padding byte at the end; the payload says its own length.
        var payloadLength = 19 + 1 + bytes[19];
        if (bytes[0] != FormatVersion || bytes.Length < payloadLength + Ed25519.SignatureSize || payloadLength - 20 > LicenceData.MaxCustomerBytes)
            return null;
        var payload = bytes.AsSpan(0, payloadLength);
        if (!Ed25519.Verify(publicKey, payload, bytes.AsSpan(payloadLength, Ed25519.SignatureSize)))
            return null;
        return Read(payload);
    }

    private static byte[] Payload(LicenceData l)
    {
        var customer = Encoding.UTF8.GetBytes(l.Customer.Trim());
        if (customer.Length > LicenceData.MaxCustomerBytes)
            customer = Encoding.UTF8.GetBytes(Truncate(l.Customer.Trim(), LicenceData.MaxCustomerBytes));
        var payload = new byte[20 + customer.Length];
        payload[0] = FormatVersion;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(1), l.Id);
        payload[5] = (byte)l.Edition;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), Days(l.IssuedOn));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), Days(l.ExpiresOn));
        l.MachineId.ToBytes().CopyTo(payload, 10);
        payload[18] = 0;   // reserved
        payload[19] = (byte)customer.Length;
        customer.CopyTo(payload, 20);
        return payload;
    }

    private static LicenceData? Read(ReadOnlySpan<byte> p)
    {
        var edition = (Edition)p[5];
        if (edition is not (Edition.Basic or Edition.Pro))
            return null;
        return new LicenceData(
            BinaryPrimitives.ReadUInt32BigEndian(p[1..]),
            edition,
            Epoch.AddDays(BinaryPrimitives.ReadUInt16BigEndian(p[6..])),
            Epoch.AddDays(BinaryPrimitives.ReadUInt16BigEndian(p[8..])),
            MachineId.FromBytes(p.Slice(10, 8)),
            Encoding.UTF8.GetString(p[20..]));
    }

    private static ushort Days(DateOnly day) => (ushort)Math.Clamp(day.DayNumber - Epoch.DayNumber, 0, ushort.MaxValue);

    private static string Truncate(string text, int maxBytes)
    {
        while (Encoding.UTF8.GetByteCount(text) > maxBytes)
            text = text[..^1];
        return text;
    }
}

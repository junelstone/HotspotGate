using System.Buffers.Binary;
using System.Text;

namespace StonesHotspot.Network.Dns;

public static class DnsType
{
    public const ushort A = 1;
    public const ushort Cname = 5;
    public const ushort Aaaa = 28;
}

public static class DnsRcode
{
    public const byte NoError = 0;
    public const byte ServFail = 2;
    public const byte NxDomain = 3;
    public const byte Refused = 5;
}

/// <summary>The question of a DNS query (only single-question queries are handled, as every OS sends).</summary>
public sealed record DnsQuestion(ushort Id, string Name, ushort Type, ushort Class, int QuestionEnd);

/// <summary>An A record from an answer, with its time-to-live.</summary>
public readonly record struct DnsARecord(uint Address, uint Ttl);

/// <summary>Minimal DNS wire-format reading and writing (RFC 1035).</summary>
public static class DnsMessage
{
    private const int HeaderLength = 12;

    public static bool TryParseQuery(ReadOnlySpan<byte> message, out DnsQuestion question)
    {
        question = null!;
        if (message.Length < HeaderLength)
            return false;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(message[2..]);
        var isResponse = (flags & 0x8000) != 0;
        var opcode = (flags >> 11) & 0x0F;
        var questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
        if (isResponse || opcode != 0 || questions != 1)
            return false;

        var offset = HeaderLength;
        if (!TryReadName(message, ref offset, allowPointers: false, out var name) || offset + 4 > message.Length)
            return false;

        question = new DnsQuestion(
            BinaryPrimitives.ReadUInt16BigEndian(message),
            name,
            BinaryPrimitives.ReadUInt16BigEndian(message[offset..]),
            BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 2)..]),
            offset + 4);
        return true;
    }

    /// <summary>Answer with one A record (for an A question) or no record (any other type), NOERROR.</summary>
    public static byte[] BuildAddressAnswer(ReadOnlySpan<byte> query, DnsQuestion question, uint address, uint ttl)
    {
        var withRecord = question.Type == DnsType.A;
        var response = BuildHeaderAndQuestion(query, question, DnsRcode.NoError, answers: withRecord ? 1 : 0, extra: withRecord ? 16 : 0);
        if (withRecord)
        {
            var answer = response.AsSpan(question.QuestionEnd);
            BinaryPrimitives.WriteUInt16BigEndian(answer, 0xC00C);              // name = pointer to the question
            BinaryPrimitives.WriteUInt16BigEndian(answer[2..], DnsType.A);
            BinaryPrimitives.WriteUInt16BigEndian(answer[4..], 1);              // class IN
            BinaryPrimitives.WriteUInt32BigEndian(answer[6..], ttl);
            BinaryPrimitives.WriteUInt16BigEndian(answer[10..], 4);
            BinaryPrimitives.WriteUInt32BigEndian(answer[12..], address);
        }
        return response;
    }

    /// <summary>An answer without records carrying <paramref name="rcode"/> (SERVFAIL, REFUSED…).</summary>
    public static byte[] BuildError(ReadOnlySpan<byte> query, DnsQuestion question, byte rcode) =>
        BuildHeaderAndQuestion(query, question, rcode, answers: 0, extra: 0);

    private static byte[] BuildHeaderAndQuestion(ReadOnlySpan<byte> query, DnsQuestion question, byte rcode, int answers, int extra)
    {
        var response = new byte[question.QuestionEnd + extra];
        query[..question.QuestionEnd].CopyTo(response);
        var requestFlags = BinaryPrimitives.ReadUInt16BigEndian(query[2..]);
        // QR=1, keep opcode and RD, RA=1, rcode.
        var flags = (ushort)(0x8000 | (requestFlags & 0x7900) | 0x0080 | rcode);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), flags);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(6), (ushort)answers);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10), 0);
        return response;
    }

    /// <summary>A records in the answer section of a response (follows CNAME chains as listed by the server).</summary>
    public static IReadOnlyList<DnsARecord> ReadARecords(ReadOnlySpan<byte> response)
    {
        var records = new List<DnsARecord>();
        if (response.Length < HeaderLength)
            return records;

        var questions = BinaryPrimitives.ReadUInt16BigEndian(response[4..]);
        var answers = BinaryPrimitives.ReadUInt16BigEndian(response[6..]);
        var offset = HeaderLength;

        for (var i = 0; i < questions; i++)
        {
            if (!TryReadName(response, ref offset, allowPointers: true, out _) || offset + 4 > response.Length)
                return records;
            offset += 4;
        }

        for (var i = 0; i < answers; i++)
        {
            if (!TryReadName(response, ref offset, allowPointers: true, out _) || offset + 10 > response.Length)
                return records;
            var type = BinaryPrimitives.ReadUInt16BigEndian(response[offset..]);
            var ttl = BinaryPrimitives.ReadUInt32BigEndian(response[(offset + 4)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(response[(offset + 8)..]);
            offset += 10;
            if (offset + length > response.Length)
                return records;
            if (type == DnsType.A && length == 4)
                records.Add(new DnsARecord(BinaryPrimitives.ReadUInt32BigEndian(response[offset..]), ttl));
            offset += length;
        }
        return records;
    }

    /// <summary>Puts the client's transaction ID on an upstream answer.</summary>
    public static void SetId(Span<byte> message, ushort id) => BinaryPrimitives.WriteUInt16BigEndian(message, id);

    private static bool TryReadName(ReadOnlySpan<byte> message, ref int offset, bool allowPointers, out string name)
    {
        name = "";
        var sb = new StringBuilder();
        var position = offset;
        var jumped = false;
        var jumps = 0;

        while (true)
        {
            if (position >= message.Length)
                return false;
            var length = message[position];

            if ((length & 0xC0) == 0xC0)
            {
                if (!allowPointers || position + 1 >= message.Length || ++jumps > 16)
                    return false;
                if (!jumped)
                    offset = position + 2;
                jumped = true;
                position = ((length & 0x3F) << 8) | message[position + 1];
                continue;
            }
            if ((length & 0xC0) != 0)
                return false;

            position++;
            if (length == 0)
                break;
            if (position + length > message.Length || sb.Length + length > 253)
                return false;
            if (sb.Length > 0)
                sb.Append('.');
            foreach (var b in message.Slice(position, length))
                sb.Append(char.ToLowerInvariant((char)b));
            position += length;
        }

        if (!jumped)
            offset = position;
        name = sb.ToString();
        return true;
    }
}

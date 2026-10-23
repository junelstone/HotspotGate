using System.Globalization;
using System.Text;

namespace StonesHotspot.Core.Printing;

public enum PrinterCharset
{
    /// <summary>Code page 850: French accents. Most ESC/POS printers (Epson, Xprinter, Rongta…) know it.</summary>
    Pc850,

    /// <summary>Plain ASCII, accents removed ("é" → "e"): for printers that print garbage with PC850.</summary>
    Ascii,
}

public enum EscPosAlign { Left = 0, Center = 1, Right = 2 }

/// <summary>
/// Builds the bytes of an ESC/POS print job (the command language of nearly all receipt printers): text in the
/// chosen character set, alignment, bold, double size, QR codes, paper cut.
/// </summary>
public sealed class EscPosBuilder
{
    private const byte Esc = 0x1B, Gs = 0x1D;
    private static readonly Encoding Cp850;

    private readonly MemoryStream _bytes = new();
    private readonly PrinterCharset _charset;

    static EscPosBuilder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp850 = Encoding.GetEncoding(850, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    }

    /// <param name="columns">Characters per line in the normal font: 32 on 58 mm paper, 48 on 80 mm.</param>
    public EscPosBuilder(int columns, PrinterCharset charset)
    {
        Columns = columns;
        _charset = charset;
        Raw(Esc, (byte)'@');                                                 // reset
        Raw(Esc, (byte)'t', charset == PrinterCharset.Pc850 ? (byte)2 : (byte)0); // code page 2 = PC850
    }

    public int Columns { get; }

    public EscPosBuilder Align(EscPosAlign align) => Raw(Esc, (byte)'a', (byte)align);

    public EscPosBuilder Bold(bool on) => Raw(Esc, (byte)'E', on ? (byte)1 : (byte)0);

    /// <summary>Double width and/or height (characters per line halve with double width).</summary>
    public EscPosBuilder Size(bool doubleWidth, bool doubleHeight) =>
        Raw(Gs, (byte)'!', (byte)((doubleWidth ? 0x10 : 0) | (doubleHeight ? 0x01 : 0)));

    public EscPosBuilder Text(string text)
    {
        _bytes.Write(Encode(text));
        return this;
    }

    public EscPosBuilder Line(string text = "") => Text(text).Raw((byte)'\n');

    /// <summary>Lines of at most <see cref="Columns"/> characters, broken between words.</summary>
    public EscPosBuilder Wrapped(string text, int? columns = null)
    {
        foreach (var line in Wrap(text, columns ?? Columns))
            Line(line);
        return this;
    }

    /// <summary>Left text and right text on one line ("Coca 33 cl ......... 500 FCFA"); a long left text wraps above.</summary>
    public EscPosBuilder Columns2(string left, string right)
    {
        var room = Columns - right.Length - 1;
        if (room < 4)
            return Line(left).Align(EscPosAlign.Right).Line(right).Align(EscPosAlign.Left);
        var lines = Wrap(left, room);
        for (var i = 0; i < lines.Count - 1; i++)
            Line(lines[i]);
        var last = lines.Count > 0 ? lines[^1] : "";
        return Line(last + new string(' ', Columns - last.Length - right.Length) + right);
    }

    public EscPosBuilder Separator(char c = '-') => Line(new string(c, Columns));

    public EscPosBuilder Feed(int lines = 1) => Raw(Esc, (byte)'d', (byte)Math.Clamp(lines, 0, 255));

    /// <summary>
    /// A QR code printed by the printer itself (GS ( k, model 2, error correction M). <paramref name="moduleSize"/> is
    /// the dot size of one module, 1–16.
    /// </summary>
    public EscPosBuilder Qr(string data, int moduleSize = 6)
    {
        var payload = Encoding.ASCII.GetBytes(data);
        if (payload.Length > 700)
            throw new ArgumentException("QR data too long for a receipt.", nameof(data));
        QrFunction(65, 50, 0);                       // model 2
        QrFunction(67, (byte)Math.Clamp(moduleSize, 1, 16));
        QrFunction(69, 49);                          // error correction M
        var length = payload.Length + 3;
        Raw(Gs, (byte)'(', (byte)'k', (byte)(length % 256), (byte)(length / 256), 49, 80, 48);
        _bytes.Write(payload);
        return QrFunction(81, 48);                   // print
    }

    /// <summary>Feeds the paper past the cutter and cuts (partial cut where the printer has one).</summary>
    public EscPosBuilder Cut() => Raw(Gs, (byte)'V', 66, 3);

    public EscPosBuilder Raw(params byte[] bytes)
    {
        _bytes.Write(bytes);
        return this;
    }

    public byte[] ToBytes() => _bytes.ToArray();

    /// <summary>The bytes of <paramref name="text"/> in the printer's character set.</summary>
    public byte[] Encode(string text)
    {
        text = Simplify(text);
        return _charset == PrinterCharset.Ascii ? Encoding.ASCII.GetBytes(ToAscii(text)) : Cp850.GetBytes(text);
    }

    /// <summary>Splits text into lines of at most <paramref name="width"/> characters, between words when possible.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var rest = word;
                while (rest.Length > width)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }
                    lines.Add(rest[..width]);
                    rest = rest[width..];
                }
                if (current.Length > 0 && current.Length + 1 + rest.Length > width)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0)
                    current.Append(' ');
                current.Append(rest);
            }
            lines.Add(current.ToString());
        }
        return lines;
    }

    private EscPosBuilder QrFunction(byte function, params byte[] parameters)
    {
        var length = parameters.Length + 2;
        Raw(Gs, (byte)'(', (byte)'k', (byte)length, 0, 49, function);
        return Raw(parameters);
    }

    /// <summary>Characters that neither PC850 nor ASCII have, replaced by close ones.</summary>
    private static string Simplify(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                ' ' or ' ' or ' ' or ' ' => " ",
                '’' or '‘' or '‚' => "'",
                '“' or '”' or '„' or '«' or '»' => "\"",
                '–' or '—' or '−' => "-",
                '…' => "...",
                '•' => "*",
                '€' => "EUR",
                'œ' => "oe",
                'Œ' => "OE",
                '↓' => "v",
                '↑' => "^",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    private static string ToAscii(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(c switch
            {
                'ß' => "ss",
                'æ' => "ae",
                'Æ' => "AE",
                'ø' => "o",
                'Ø' => "O",
                '°' => "o",
                '·' => "-",
                _ => c is >= ' ' and <= '~' or '\n' ? c.ToString() : "?",
            });
        }
        return sb.ToString();
    }
}

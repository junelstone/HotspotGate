using System.Text;

namespace StonesHotspot.Core.Documents;

/// <summary>Text encoding (WinAnsi) and widths of the standard Helvetica fonts.</summary>
internal static class PdfText
{
    // Advance widths (1/1000 em) of characters 32–126, from the Adobe Helvetica and Helvetica-Bold metrics.
    private static readonly short[] Regular =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly short[] Bold =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    // WinAnsi codes 0x80–0x9F that differ from Latin-1.
    private static readonly Dictionary<char, byte> WinAnsiExtras = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C,
        ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
        ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    /// <summary>Text width in points.</summary>
    public static float Measure(string text, float size, bool bold) => text.Sum(c => Width(c, bold)) * size / 1000f;

    /// <summary>Shortens <paramref name="text"/> with "…" until it fits.</summary>
    public static string Fit(string text, float size, float maxWidth, bool bold)
    {
        if (Measure(text, size, bold) <= maxWidth)
            return text;
        var cut = text;
        while (cut.Length > 0 && Measure(cut + "…", size, bold) > maxWidth)
            cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }

    /// <summary>A PDF string literal <c>(…)</c> in WinAnsi; characters outside it become "?".</summary>
    public static byte[] Literal(string text)
    {
        var bytes = new List<byte>(text.Length + 2) { (byte)'(' };
        foreach (var c in text)
        {
            var b = Encode(c);
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
            {
                bytes.Add((byte)'\\');
                bytes.Add(b);
            }
            else if (b is < 32 or > 126)
            {
                bytes.AddRange(Encoding.ASCII.GetBytes("\\" + Convert.ToString(b, 8).PadLeft(3, '0')));
            }
            else
            {
                bytes.Add(b);
            }
        }
        bytes.Add((byte)')');
        return [.. bytes];
    }

    internal static byte Encode(char c) => c switch
    {
        ' ' or ' ' or ' ' => 0xA0,
        >= ' ' and <= '~' => (byte)c,
        >= ' ' and <= 'ÿ' => (byte)c,
        _ when WinAnsiExtras.TryGetValue(c, out var b) => b,
        _ => (byte)'?',
    };

    private static int Width(char c, bool bold)
    {
        var table = bold ? Bold : Regular;
        if (c is >= ' ' and <= '~')
            return table[c - ' '];
        switch (c)
        {
            case ' ' or ' ' or ' ' or ' ':
                return 278;
            case '…' or '—' or '‰' or 'Œ' or 'Æ' or '™':
                return 1000;
            case 'œ' or 'æ':
                return bold ? 944 : 889;
            case '–' or '€' or '«' or '»' or '•':
                return 556;
            case '‘' or '’' or '‚':
                return bold ? 278 : 222;
            case '“' or '”' or '„' or '°':
                return bold ? 500 : 333;
        }
        // Accented letters are as wide as their base letter (é = e).
        var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        return decomposed.Length > 0 && decomposed[0] is >= ' ' and <= '~' ? table[decomposed[0] - ' '] : 556;
    }
}

/// <summary>Text widths for laying out PDF pages.</summary>
public static class PdfMetrics
{
    /// <summary>Width in points of <paramref name="text"/> in Helvetica (or Helvetica-Bold).</summary>
    public static float Measure(string text, float size, bool bold = false) => PdfText.Measure(text, size, bold);
}

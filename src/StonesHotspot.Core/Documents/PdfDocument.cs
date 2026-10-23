using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace StonesHotspot.Core.Documents;

/// <summary>
/// A small PDF 1.4 writer: pages with text in the two standard Helvetica fonts (no font files to embed, French
/// accents through WinAnsiEncoding), lines and filled rectangles. Enough for receipts and report tables, without
/// a third-party library. Coordinates are in points (1/72 inch) from the <b>top-left</b> corner.
/// </summary>
public sealed class PdfDocument
{
    public const float A4Width = 595.28f;
    public const float A4Height = 841.89f;
    public const float PointsPerMm = 72f / 25.4f;

    private readonly List<PdfPage> _pages = [];

    public string? Title { get; init; }

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage(float width = A4Width, float height = A4Height)
    {
        var page = new PdfPage(width, height);
        _pages.Add(page);
        return page;
    }

    public byte[] ToBytes()
    {
        if (_pages.Count == 0)
            AddPage();

        var output = new MemoryStream();
        var offsets = new List<long>();

        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void BeginObject(int number)
        {
            while (offsets.Count < number)
                offsets.Add(0);
            offsets[number - 1] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n"));
        }

        // Objects: 1 catalog, 2 page tree, 3 regular font, 4 bold font, 5 info, then a page and its content per page.
        const int firstPage = 6;
        output.Write([.. "%PDF-1.4\n%"u8, 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        var kids = string.Join(" ", _pages.Select((_, i) => string.Create(CultureInfo.InvariantCulture, $"{firstPage + 2 * i} 0 R")));
        Write(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>\nendobj\n"));

        BeginObject(3);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        BeginObject(4);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        BeginObject(5);
        output.Write("<< /Producer "u8);
        output.Write(PdfText.Literal(ProductInfo.DisplayName));
        if (Title is not null)
        {
            output.Write(" /Title "u8);
            output.Write(PdfText.Literal(Title));
        }
        Write(" >>\nendobj\n");

        for (var i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            var pageNumber = firstPage + 2 * i;
            BeginObject(pageNumber);
            Write(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PdfPage.Num(page.Width)} {PdfPage.Num(page.Height)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {pageNumber + 1} 0 R >>\nendobj\n"));

            var compressed = Deflate(page.Content);
            BeginObject(pageNumber + 1);
            Write(string.Create(CultureInfo.InvariantCulture, $"<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n"));
            output.Write(compressed);
            Write("\nendstream\nendobj\n");
        }

        var xref = output.Position;
        Write(string.Create(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets)
            Write(string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n"));
        Write(string.Create(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }
}

public enum PdfAlign { Left, Center, Right }

/// <summary>One page. Its drawing operators are kept uncompressed until the document is written.</summary>
public sealed class PdfPage(float width, float height)
{
    private readonly MemoryStream _content = new();

    public float Width { get; } = width;
    public float Height { get; } = height;

    internal byte[] Content => _content.ToArray();

    /// <summary>Writes one line of text; <paramref name="y"/> is its baseline, from the top of the page.</summary>
    public void Text(float x, float y, string text, float size, bool bold = false, PdfAlign align = PdfAlign.Left)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var width = PdfText.Measure(text, size, bold);
        var left = align switch
        {
            PdfAlign.Right => x - width,
            PdfAlign.Center => x - width / 2,
            _ => x,
        };
        Append($"BT /{(bold ? "F2" : "F1")} {Num(size)} Tf {Num(left)} {Num(Height - y)} Td ");
        _content.Write(PdfText.Literal(text));
        Append(" Tj ET\n");
    }

    /// <summary>Text cut with "…" so that it fits in <paramref name="maxWidth"/>.</summary>
    public void TextFit(float x, float y, string text, float size, float maxWidth, bool bold = false, PdfAlign align = PdfAlign.Left) =>
        Text(x, y, PdfText.Fit(text, size, maxWidth, bold), size, bold, align);

    public void Line(float x1, float y1, float x2, float y2, float width = 0.5f, float gray = 0)
    {
        Append($"q {Num(gray)} G {Num(width)} w {Num(x1)} {Num(Height - y1)} m {Num(x2)} {Num(Height - y2)} l S Q\n");
    }

    /// <summary>A filled rectangle; <paramref name="gray"/> 0 = black, 1 = white.</summary>
    public void FillRect(float x, float y, float w, float h, float gray = 0)
    {
        Append($"q {Num(gray)} g {Num(x)} {Num(Height - y - h)} {Num(w)} {Num(h)} re f Q\n");
    }

    /// <summary>Black squares for the true cells of a module matrix (a QR code), <paramref name="module"/> points each.</summary>
    public void Modules(float x, float y, bool[,] matrix, float module)
    {
        var sb = new StringBuilder("q 0 g\n");
        for (var row = 0; row < matrix.GetLength(0); row++)
        {
            for (var col = 0; col < matrix.GetLength(1); col++)
            {
                if (matrix[row, col])
                    sb.Append(CultureInfo.InvariantCulture,
                        $"{Num(x + col * module)} {Num(Height - y - (row + 1) * module)} {Num(module)} {Num(module)} re\n");
            }
        }
        sb.Append("f Q\n");
        Append(sb.ToString());
    }

    private void Append(string operators) => _content.Write(Encoding.ASCII.GetBytes(operators));

    internal static string Num(float value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}

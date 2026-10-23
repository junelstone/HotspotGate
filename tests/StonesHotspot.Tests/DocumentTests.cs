using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using StonesHotspot.Core.Documents;
using StonesHotspot.Core.Money;

namespace StonesHotspot.Tests;

public class DocumentTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>The uncompressed drawing operators of every page.</summary>
    private static List<string> PageContents(byte[] pdf)
    {
        var text = Latin1(pdf);
        var contents = new List<string>();
        foreach (Match m in Regex.Matches(text, @"/Length (\d+) /Filter /FlateDecode >>\nstream\n"))
        {
            var start = m.Index + m.Length;
            var length = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            using var zlib = new ZLibStream(new MemoryStream(pdf, start, length), CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, Encoding.Latin1);
            contents.Add(reader.ReadToEnd());
        }
        return contents;
    }

    [Fact]
    public void Pdf_has_a_valid_cross_reference_table_and_winansi_text()
    {
        var doc = new PdfDocument { Title = "Reçu" };
        var page = doc.AddPage();
        page.Text(50, 50, "Reçu n° 2026-000001 (test) \\ é à ç œ €", 12, bold: true);
        page.Line(10, 10, 100, 10);
        page.Modules(10, 100, new bool[,] { { true, false }, { false, true } }, 2);
        var pdf = doc.ToBytes();
        var text = Latin1(pdf);

        Assert.StartsWith("%PDF-1.4\n", text);
        Assert.EndsWith("%%EOF\n", text);
        var startXref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.StartsWith("xref\n0 8\n", text[startXref..]);
        var offsets = Regex.Matches(text[startXref..], @"(\d{10}) 00000 n ").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(7, offsets.Count);
        for (var i = 0; i < offsets.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj", text[offsets[i]..]);

        var content = PageContents(pdf).Single();
        // ç = \347, ° = \260, é = \351, à = \340, œ = \234, € = \200 in WinAnsi; ( ) \ escaped.
        Assert.Contains(@"(Re\347u n\260 2026-000001 \(test\) \\ \351 \340 \347 \234 \200) Tj", content);
        Assert.Contains("/F2 12 Tf 50 791.89 Td", content);   // y from the top: 841.89 − 50
        Assert.Contains("re\n", content);
    }

    [Fact]
    public void Pdf_text_width_and_fitting()
    {
        Assert.Equal(PdfMetrics.Measure("e", 10), PdfMetrics.Measure("é", 10));
        Assert.Equal(5.56f, PdfMetrics.Measure("0", 10), 3);
        Assert.True(PdfMetrics.Measure("W", 10, bold: true) > PdfMetrics.Measure("i", 10, bold: true));
        var doc = new PdfDocument();
        doc.AddPage().TextFit(0, 10, new string('M', 200), 10, 100);
        var content = PageContents(doc.ToBytes()).Single();
        Assert.Contains(@"\205) Tj", content);   // ends with "…"
        var shown = Regex.Match(content, @"\((M+)\\205\)").Groups[1].Value;
        Assert.True(PdfMetrics.Measure(shown + "…", 10) <= 100);
    }

    [Fact]
    public void Long_reports_go_over_several_pages_with_numbered_footers()
    {
        var table = new ReportTable("Opérations", new ReportColumn("Reçu"), new ReportColumn("Montant", Numeric: true));
        for (var i = 0; i < 150; i++)
            table.Add($"2026-{i:D6}", ReportCell.Money(new Xaf(i * 100)));
        table.Totals = ["Total", ReportCell.Money(new Xaf(1_117_500))];
        var pdf = ReportExport.Pdf(new ReportDocument("Opérations", "du 1 au 31", "Stone's Hotspot", "Imprimé le …", [table], "(suite)"));

        var pages = PageContents(pdf);
        Assert.True(pages.Count >= 3);
        Assert.Contains($"(1 / {pages.Count}) Tj", pages[0]);
        Assert.Contains($"({pages.Count} / {pages.Count}) Tj", pages[^1]);
        Assert.Contains(@"(Op\351rations \(suite\)) Tj", pages[1]);
        Assert.Contains("(2026-000149) Tj", string.Concat(pages));
        Assert.Contains("(1 117 500 FCFA) Tj".Replace(" ", @"\240"), pages[^1]);
    }

    [Fact]
    public void Csv_is_excel_friendly_and_safe()
    {
        var table = new ReportTable("T", new ReportColumn("Nom"), new ReportColumn("Montant", Numeric: true));
        table.Add("Coca; \"33 cl\"", ReportCell.Money(new Xaf(1500)));
        table.Add("=HYPERLINK(\"x\")", ReportCell.Money(new Xaf(-500)));
        table.Add("-cmd", ReportCell.Number(1234, French));
        var csv = ReportExport.Csv([table]);

        Assert.Equal(Encoding.UTF8.GetPreamble(), csv[..3]);
        var lines = Encoding.UTF8.GetString(csv[3..]).Split('\n');
        Assert.Equal("Nom;Montant", lines[0]);
        Assert.Equal("\"Coca; \"\"33 cl\"\"\";1500", lines[1]);
        Assert.Equal("\"'=HYPERLINK(\"\"x\"\")\";-500", lines[2]);
        Assert.Equal("'-cmd;1234", lines[3]);
        Assert.Equal("1 234", ReportCell.Number(1234, French).Display);
    }

    [Fact]
    public void Pdf_text_wraps_between_words()
    {
        var lines = PdfMetrics.Wrap("Connectez-vous au Wi-Fi, ouvrez une page web\net entrez ce code", 10, 120);
        Assert.Equal("et entrez ce code", lines[^1]);   // the explicit line break is kept
        Assert.All(lines, l => Assert.True(PdfMetrics.Measure(l, 10) <= 120));
        Assert.Equal("Connectez-vous au Wi-Fi, ouvrez une page web et entrez ce code", string.Join(" ", lines).Replace("  ", " "));
        Assert.All(PdfMetrics.Wrap(new string('M', 50), 10, 60), l => Assert.True(PdfMetrics.Measure(l, 10) <= 60));
    }
}

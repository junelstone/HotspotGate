using System.Globalization;
using System.Text;
using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Documents;

/// <summary>A table cell: what is shown on screen and in PDF, and the plain value written to CSV.</summary>
public readonly record struct ReportCell(string Display, string Csv)
{
    public static implicit operator ReportCell(string text) => new(text, text);

    public static ReportCell Money(Xaf amount) =>
        new(amount.ToString(), amount.Amount.ToString(CultureInfo.InvariantCulture));

    public static ReportCell Money(Xaf? amount) => amount is { } a ? Money(a) : "";

    public static ReportCell Number(long value, CultureInfo culture) =>
        new(value.ToString("N0", culture).Replace(' ', ' '), value.ToString(CultureInfo.InvariantCulture));

    public static ReportCell Decimal(double value, int decimals, CultureInfo culture) =>
        new(value.ToString("N" + decimals, culture).Replace(' ', ' '), value.ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>A formatted value (bytes, durations…) with its raw number for CSV.</summary>
    public static ReportCell Formatted(string display, long raw) => new(display, raw.ToString(CultureInfo.InvariantCulture));

    public static ReportCell Date(DateTime local, CultureInfo culture) =>
        new(local.ToString("g", culture), local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
}

public sealed record ReportColumn(string Header, bool Numeric = false, float Weight = 1);

/// <summary>One table of a report: shown on the page, exported as CSV, laid out in the PDF.</summary>
public sealed class ReportTable(string title, params ReportColumn[] columns)
{
    public string Title { get; } = title;
    public IReadOnlyList<ReportColumn> Columns { get; } = columns;
    public List<ReportCell[]> Rows { get; } = [];

    /// <summary>Bold last line (totals); null when none.</summary>
    public ReportCell[]? Totals { get; set; }

    public ReportTable Add(params ReportCell[] cells)
    {
        if (cells.Length != Columns.Count)
            throw new ArgumentException($"Expected {Columns.Count} cells, got {cells.Length}.", nameof(cells));
        Rows.Add(cells);
        return this;
    }
}

/// <param name="Subtitle">Usually the dates of the report.</param>
/// <param name="Footer">Small text at the bottom of each PDF page, e.g. "Printed on … by …".</param>
/// <param name="Continued">Added to a table title repeated on the next page.</param>
public sealed record ReportDocument(
    string Title, string? Subtitle, string BusinessName, string Footer, IReadOnlyList<ReportTable> Tables, string Continued = "(suite)");

public static class ReportExport
{
    /// <summary>
    /// CSV for Excel in French: ";" separators, UTF-8 with BOM, raw numbers (amounts in francs, bytes, seconds).
    /// Several tables are separated by a blank line and their title.
    /// </summary>
    public static byte[] Csv(IReadOnlyList<ReportTable> tables)
    {
        var sb = new StringBuilder();
        foreach (var table in tables)
        {
            if (tables.Count > 1)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(Escape(table.Title)).Append('\n');
            }
            sb.AppendJoin(';', table.Columns.Select(c => Escape(c.Header))).Append('\n');
            foreach (var row in table.Rows.Concat(table.Totals is null ? [] : [table.Totals]))
                sb.AppendJoin(';', row.Select(c => Escape(c.Csv))).Append('\n');
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>Escapes a CSV value; values that a spreadsheet would run as a formula get a leading quote.</summary>
    public static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '@' or '\t' or '\r'
            || value.Length > 1 && value[0] == '-' && !char.IsDigit(value[1]))
            value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    /// <summary>A4 portrait PDF: header, then each table with its column titles repeated on every page.</summary>
    public static byte[] Pdf(ReportDocument report)
    {
        var pdf = new PdfDocument { Title = report.Title };
        const float margin = 36, rowHeight = 14, fontSize = 8.5f, titleSize = 15;
        const float bottom = PdfDocument.A4Height - margin - 20;
        var usable = PdfDocument.A4Width - 2 * margin;

        PdfPage page = null!;
        float y = 0;

        void NewPage()
        {
            page = pdf.AddPage();
            y = margin + 10;
            page.Text(margin, y, report.BusinessName, 9, bold: true);
            page.Text(PdfDocument.A4Width - margin, y, report.Title, 9, align: PdfAlign.Right);
            y += 6;
            page.Line(margin, y, PdfDocument.A4Width - margin, y, 0.5f, 0.6f);
            y += 16;
        }

        NewPage();
        page.Text(margin, y + 4, report.Title, titleSize, bold: true);
        y += 22;
        if (!string.IsNullOrEmpty(report.Subtitle))
        {
            page.Text(margin, y, report.Subtitle, 10);
            y += 18;
        }

        foreach (var table in report.Tables)
        {
            var total = table.Columns.Sum(c => c.Weight);
            var widths = table.Columns.Select(c => usable * c.Weight / total).ToArray();

            void Header(bool continued)
            {
                page.Text(margin, y, continued ? $"{table.Title} {report.Continued}" : table.Title, 11, bold: true);
                y += 14;
                page.FillRect(margin, y - 10, usable, rowHeight, 0.92f);
                Row(table.Columns.Select(c => new ReportCell(c.Header, c.Header)).ToArray(), bold: true);
            }

            void Row(ReportCell[] cells, bool bold)
            {
                var x = margin;
                for (var i = 0; i < cells.Length; i++)
                {
                    var cellWidth = widths[i] - 6;
                    if (table.Columns[i].Numeric)
                        page.TextFit(x + widths[i] - 3, y, cells[i].Display, fontSize, cellWidth, bold, PdfAlign.Right);
                    else
                        page.TextFit(x + 3, y, cells[i].Display, fontSize, cellWidth, bold);
                    x += widths[i];
                }
                y += rowHeight;
            }

            if (y + 3 * rowHeight > bottom)
                NewPage();
            Header(continued: false);
            if (table.Rows.Count == 0)
            {
                page.Text(margin + 3, y, "—", fontSize);
                y += rowHeight;
            }
            foreach (var row in table.Rows)
            {
                if (y > bottom)
                {
                    NewPage();
                    Header(continued: true);
                }
                Row(row, bold: false);
                page.Line(margin, y - rowHeight + 4, margin + usable, y - rowHeight + 4, 0.3f, 0.85f);
            }
            if (table.Totals is not null)
            {
                if (y > bottom)
                    NewPage();
                page.Line(margin, y - rowHeight + 3.5f, margin + usable, y - rowHeight + 3.5f, 0.8f);
                Row(table.Totals, bold: true);
            }
            y += 14;
        }

        for (var i = 0; i < pdf.Pages.Count; i++)
        {
            var p = pdf.Pages[i];
            p.Text(margin, PdfDocument.A4Height - margin + 8, report.Footer, 7.5f);
            p.Text(PdfDocument.A4Width - margin, PdfDocument.A4Height - margin + 8,
                string.Create(CultureInfo.InvariantCulture, $"{i + 1} / {pdf.Pages.Count}"), 7.5f, align: PdfAlign.Right);
        }
        return pdf.ToBytes();
    }
}

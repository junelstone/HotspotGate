using System.Globalization;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Documents;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Printing;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Web.Admin;

namespace StonesHotspot.Web.Documents;

/// <summary>What every receipt and report prints at the top and bottom.</summary>
/// <param name="PortalAddress">http://192.168.137.1/portal, printed under the Wi-Fi codes.</param>
public sealed record ReceiptBranding(string BusinessName, string? WifiName, string Header, string Footer, string PortalAddress, Func<string, string> CodeLink);

/// <summary>Receipts, shift reports and the printer test page, for the thermal printer (ESC/POS) and as PDF.</summary>
public static class ReceiptLayouts
{
    public static byte[] ReceiptEscPos(Receipt receipt, ReceiptBranding brand, PrinterSettings printer, CultureInfo culture)
    {
        var p = new EscPosBuilder(printer.Columns, printer.Charset);
        Heading(p, brand);
        p.Line($"{Strings.Print_Receipt} {receipt.Number}")
            .Line(AdminText.Date(receipt.Timestamp, culture));
        if (receipt.Seller is not null)
            p.Line($"{Strings.Print_Seller} : {receipt.Seller}");
        p.Separator();

        foreach (var line in receipt.Lines)
        {
            p.Columns2(LineLabel(line), Money(line.Amount));
            if (line.Quantity > 1)
                p.Line($"  {line.Quantity} x {Money(line.UnitPrice)}");
        }
        p.Separator()
            .Bold(true).Size(false, true).Columns2(Strings.Print_Total.ToUpper(culture), Money(receipt.Total)).Size(false, false).Bold(false)
            .Line($"{Strings.Sell_Payment} : {AdminText.Payment(receipt.Method)}");
        foreach (var vat in receipt.Vat)
            p.Columns2(string.Format(culture, Strings.Receipt_VatIncluded, Percent(vat.Rate, culture)), Money(vat.Vat));

        foreach (var voucher in receipt.Vouchers)
        {
            p.Separator('=').Align(EscPosAlign.Center);
            if (!string.IsNullOrEmpty(brand.WifiName))
                p.Line($"Wi-Fi : {brand.WifiName}");
            p.Bold(true).Line(voucher.Tariff.Name).Bold(false)
                .Line(AdminText.Describe(voucher.Tariff, culture))
                .Feed(1)
                .Bold(true).Size(true, true).Line(VoucherCodeGenerator.Format(voucher.Code)).Size(false, false).Bold(false)
                .Feed(1);
            if (printer.PrintQr)
                p.Qr(brand.CodeLink(voucher.Code), printer.Columns > 40 ? 6 : 5).Feed(1);
            p.Wrapped(Strings.Print_HowTo).Line(brand.PortalAddress).Align(EscPosAlign.Left);
        }

        Ending(p, brand, Strings.Print_Thanks);
        return p.ToBytes();
    }

    public static byte[] ShiftEscPos(ShiftReport report, IReadOnlyList<Transaction> transactions, ReceiptBranding brand, PrinterSettings printer, CultureInfo culture)
    {
        var p = new EscPosBuilder(printer.Columns, printer.Charset);
        var s = report.Shift;
        Heading(p, brand);
        p.Align(EscPosAlign.Center).Bold(true).Line(Strings.ShiftReport_Title).Bold(false).Align(EscPosAlign.Left)
            .Line($"{Strings.Col_Staff} : {s.Staff?.DisplayName ?? s.Staff?.Username}")
            .Line($"{Strings.Shift_OpenedAt} {AdminText.Date(s.OpenedAt, culture)}");
        if (s.ClosedAt is { } closed)
            p.Line($"{Strings.Shift_ClosedAt} {AdminText.Date(closed, culture)}");
        p.Separator();
        foreach (var (label, value) in SummaryLines(report))
            p.Columns2(label, value);
        p.Separator();
        foreach (var (type, total) in report.TotalsByType.OrderBy(t => t.Key))
            p.Columns2(AdminText.TransactionType(type), Money(total));
        p.Separator().Line(string.Format(culture, Strings.Home_Transactions, report.TransactionCount));
        foreach (var receipt in transactions.GroupBy(t => t.ReceiptNumber).OrderBy(g => g.Min(t => t.Timestamp)))
        {
            var first = receipt.First();
            p.Columns2($"{first.Timestamp.ToLocalTime():HH:mm} {receipt.Key} {PaymentShort(first.PaymentMethod)}", Money(Xaf.Sum(receipt.Select(t => t.Amount))));
        }
        Ending(p, brand, null);
        return p.ToBytes();
    }

    public static byte[] TestPageEscPos(ReceiptBranding brand, PrinterSettings printer, CultureInfo culture)
    {
        var p = new EscPosBuilder(printer.Columns, printer.Charset);
        Heading(p, brand);
        p.Align(EscPosAlign.Center).Bold(true).Line(Strings.Printer_TestTitle).Bold(false).Align(EscPosAlign.Left)
            .Line(AdminText.Date(DateTime.UtcNow, culture))
            .Line(string.Format(culture, Strings.Printer_TestPaper, printer.PaperWidthMm, printer.Columns))
            .Line(new string('1', 10) + new string('2', 10) + new string('3', 10) + new string('4', Math.Max(0, printer.Columns - 30)))
            .Line("àâçéèêëîïôùûü ÀÇÉÈ œ « » 1 000 FCFA")
            .Columns2(Strings.Col_Name, Money(new Xaf(1000)))
            .Bold(true).Line(Strings.Printer_TestBold).Bold(false)
            .Size(true, true).Line("ABCD-1234").Size(false, false);
        if (printer.PrintQr)
            p.Align(EscPosAlign.Center).Qr(brand.PortalAddress, 5).Line(brand.PortalAddress).Align(EscPosAlign.Left);
        Ending(p, brand, Strings.Printer_TestEnd);
        return p.ToBytes();
    }

    /// <summary>A receipt as a PDF ticket 80 mm wide, for sending by WhatsApp or e-mail.</summary>
    public static byte[] ReceiptPdf(Receipt receipt, ReceiptBranding brand, CultureInfo culture)
    {
        const float width = 80 * PdfDocument.PointsPerMm, margin = 12, size = 8.5f, lineHeight = 11.5f;
        var right = width - margin;
        var usable = width - 2 * margin;

        // Height: drawn once on a very long page to measure, then again at the right height.
        float Draw(PdfPage page)
        {
            var y = margin + 12f;
            void Center(string text, float s = size, bool bold = false)
            {
                foreach (var line in EscPosBuilder.Wrap(text, Math.Max(10, (int)(usable / (s * 0.5f)))))
                {
                    page.TextFit(width / 2, y, line, s, usable, bold, PdfAlign.Center);
                    y += s + 3.5f;
                }
            }
            void Pair(string left, string amount, bool bold = false, float s = size)
            {
                var amountWidth = PdfTextWidth(amount, s, bold);
                page.TextFit(margin, y, left, s, usable - amountWidth - 6, bold);
                page.Text(right, y, amount, s, bold, PdfAlign.Right);
                y += lineHeight;
            }
            void Rule()
            {
                y += 2;
                page.Line(margin, y - 7, right, y - 7, 0.4f, 0.4f);
                y += 4;
            }

            Center(brand.BusinessName, 12, bold: true);
            if (!string.IsNullOrWhiteSpace(brand.Header))
                Center(brand.Header);
            y += 4;
            page.Text(margin, y, $"{Strings.Print_Receipt} {receipt.Number}", size, bold: true);
            y += lineHeight;
            page.Text(margin, y, AdminText.Date(receipt.Timestamp, culture), size);
            y += lineHeight;
            if (receipt.Seller is not null)
            {
                page.Text(margin, y, $"{Strings.Print_Seller} : {receipt.Seller}", size);
                y += lineHeight;
            }
            Rule();
            foreach (var line in receipt.Lines)
            {
                Pair(LineLabel(line), line.Amount.ToString());
                if (line.Quantity > 1)
                {
                    page.Text(margin + 8, y - 2, $"{line.Quantity} × {line.UnitPrice}", size - 1.5f);
                    y += lineHeight - 2;
                }
            }
            Rule();
            Pair(Strings.Print_Total.ToUpper(culture), receipt.Total.ToString(), bold: true, s: 11);
            page.Text(margin, y, $"{Strings.Sell_Payment} : {AdminText.Payment(receipt.Method)}", size);
            y += lineHeight;
            foreach (var vat in receipt.Vat)
                Pair(string.Format(culture, Strings.Receipt_VatIncluded, Percent(vat.Rate, culture)), vat.Vat.ToString());

            foreach (var voucher in receipt.Vouchers)
            {
                y += 4;
                Rule();
                if (!string.IsNullOrEmpty(brand.WifiName))
                    Center($"Wi-Fi : {brand.WifiName}");
                Center($"{voucher.Tariff.Name} — {AdminText.Describe(voucher.Tariff, culture)}", bold: true);
                y += 6;
                Center(VoucherCodeGenerator.Format(voucher.Code), 16, bold: true);
                var matrix = QrSvg.Matrix(brand.CodeLink(voucher.Code));
                var module = 90f / matrix.GetLength(0);
                page.Modules((width - 90) / 2, y - 12, matrix, module);
                y += 86;
                Center(Strings.Print_HowTo, size - 1);
                Center(brand.PortalAddress, size - 1);
            }

            y += 6;
            if (!string.IsNullOrWhiteSpace(brand.Footer))
                Center(brand.Footer);
            Center(Strings.Print_Thanks);
            return y + margin;
        }

        var measure = new PdfDocument();
        var height = Draw(measure.AddPage(width, 20_000));
        var pdf = new PdfDocument { Title = $"{Strings.Print_Receipt} {receipt.Number}" };
        Draw(pdf.AddPage(width, height));
        return pdf.ToBytes();
    }

    /// <summary>The shift report as an A4 report (summary, totals by type, every transaction).</summary>
    public static ReportDocument ShiftReportDocument(ShiftReport report, IReadOnlyList<Transaction> transactions, ReceiptBranding brand, string footer, CultureInfo culture)
    {
        var s = report.Shift;
        var summary = new ReportTable(Strings.ShiftReport_Summary, new ReportColumn(""), new ReportColumn("", Numeric: true));
        foreach (var (label, value) in SummaryLines(report))
            summary.Add(label, value);

        var byType = new ReportTable(Strings.ShiftReport_ByType, new ReportColumn(Strings.Col_Type), new ReportColumn(Strings.Col_Amount, Numeric: true));
        foreach (var (type, total) in report.TotalsByType.OrderBy(t => t.Key))
            byType.Add(AdminText.TransactionType(type), ReportCell.Money(total));

        var lines = new ReportTable(string.Format(culture, Strings.Home_Transactions, report.TransactionCount),
            new ReportColumn(Strings.Col_Time, Weight: 1.3f), new ReportColumn(Strings.Col_Receipt, Weight: 1.1f), new ReportColumn(Strings.Col_Type, Weight: 1.3f),
            new ReportColumn(Strings.Col_Description, Weight: 1.6f), new ReportColumn(Strings.Sell_Payment), new ReportColumn(Strings.Col_Amount, Numeric: true));
        foreach (var t in transactions.OrderBy(t => t.Timestamp).ThenBy(t => t.Id))
        {
            lines.Add(ReportCell.Date(t.Timestamp.ToLocalTime(), culture), t.ReceiptNumber, AdminText.TransactionType(t.Type),
                TransactionText(t), AdminText.Payment(t.PaymentMethod), ReportCell.Money(t.Amount));
        }
        lines.Totals = ["", "", "", "", Strings.Print_Total, ReportCell.Money(Xaf.Sum(transactions.Select(t => t.Amount)))];

        var subtitle = $"{s.Staff?.DisplayName ?? s.Staff?.Username} · {Strings.Shift_OpenedAt} {AdminText.Date(s.OpenedAt, culture)}"
                       + (s.ClosedAt is { } closed ? $" · {Strings.Shift_ClosedAt} {AdminText.Date(closed, culture)}" : $" · {Strings.Shift_Open}");
        return new ReportDocument($"{Strings.ShiftReport_Title} #{s.Id}", subtitle, brand.BusinessName, footer, [summary, byType, lines], Strings.Report_Continued);
    }

    /// <summary>"Coca 33 cl" or "1 heure (code Wi-Fi)"; the quantity is printed on its own line ("3 x 500 FCFA").</summary>
    public static string LineLabel(ReceiptLine line) =>
        line.Type == TransactionType.VoucherSale ? $"{line.Description} ({Strings.Receipt_WifiCode})" : line.Description;

    /// <summary>Product name, Wi-Fi code or description of a transaction.</summary>
    public static string TransactionText(Transaction t) =>
        t.Voucher is not null ? $"{t.Description} {VoucherCodeGenerator.Format(t.Voucher.Code)}"
        : t.Quantity > 1 ? $"{t.Quantity} × {t.Description}" : t.Description ?? "";

    public static string Percent(decimal rate, CultureInfo culture) => (rate * 100).ToString("0.##", culture) + " %";

    private static IEnumerable<(string Label, string Value)> SummaryLines(ShiftReport report)
    {
        yield return (Strings.Shift_OpeningCash, Money(report.OpeningCash));
        yield return (Strings.Shift_CashIn, Money(report.CashTransactionsTotal));
        yield return (Strings.Shift_Expected, Money(report.ExpectedCash));
        if (report.CountedCash is { } counted)
        {
            yield return (Strings.Shift_Counted, Money(counted));
            yield return (Strings.Shift_Difference, Money(report.Difference ?? Xaf.Zero));
        }
        foreach (var (method, total) in report.TotalsByPaymentMethod.Where(t => t.Key != PaymentMethod.Cash).OrderBy(t => t.Key))
            yield return (AdminText.Payment(method), Money(total));
    }

    private static void Heading(EscPosBuilder p, ReceiptBranding brand)
    {
        p.Align(EscPosAlign.Center).Bold(true).Size(true, true).Wrapped(brand.BusinessName, p.Columns / 2).Size(false, false).Bold(false);
        if (!string.IsNullOrWhiteSpace(brand.Header))
            p.Wrapped(brand.Header);
        p.Align(EscPosAlign.Left).Feed(1);
    }

    private static void Ending(EscPosBuilder p, ReceiptBranding brand, string? thanks)
    {
        p.Separator().Align(EscPosAlign.Center);
        if (!string.IsNullOrWhiteSpace(brand.Footer))
            p.Wrapped(brand.Footer);
        if (thanks is not null)
            p.Line(thanks);
        p.Align(EscPosAlign.Left).Feed(4).Cut();
    }

    private static string PaymentShort(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "",
        PaymentMethod.MtnMoMo => "MTN",
        PaymentMethod.AirtelMoney => "Airtel",
        _ => "Cpt",
    };

    private static string Money(Xaf amount) => amount.ToString(XafStyle.Ascii);

    private static float PdfTextWidth(string text, float size, bool bold) => PdfMetrics.Measure(text, size, bold);
}

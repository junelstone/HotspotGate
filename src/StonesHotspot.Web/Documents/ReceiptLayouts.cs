using System.Globalization;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Documents;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Admin;

namespace StonesHotspot.Web.Documents;

/// <summary>What every receipt and report prints at the top and bottom.</summary>
/// <param name="PortalAddress">http://192.168.137.1/portal, printed under the Wi-Fi codes.</param>
public sealed record ReceiptBranding(string BusinessName, string? WifiName, string Header, string Footer, string PortalAddress, Func<string, string> CodeLink);

public sealed class ReceiptBrandingProvider(SettingsService settings, PortalLocation portal)
{
    public async Task<ReceiptBranding> GetAsync(CancellationToken ct = default) => new(
        await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "",
        await settings.GetAsync(SettingKeys.WifiName, ct),
        await settings.GetAsync(SettingKeys.ReceiptHeader, ct) ?? "",
        await settings.GetAsync(SettingKeys.ReceiptFooter, ct) ?? "",
        PortalLinks.Address(portal),
        code => PortalLinks.ForCode(portal, code));
}

/// <summary>Receipts and shift reports as A4 PDF documents.</summary>
public static class ReceiptLayouts
{
    /// <summary>
    /// A receipt on one A4 page (more if it has many lines or codes): business, receipt number, the lines, totals and
    /// VAT, then each Wi-Fi code as a card to cut out, with its QR code.
    /// </summary>
    public static byte[] ReceiptPdf(Receipt receipt, ReceiptBranding brand, CultureInfo culture)
    {
        const float margin = 48, size = 10, row = 16;
        const float width = PdfDocument.A4Width, bottom = PdfDocument.A4Height - margin;
        var right = width - margin;
        var pdf = new PdfDocument { Title = $"{Strings.Print_Receipt} {receipt.Number}" };
        var page = pdf.AddPage();
        var y = margin + 10f;

        void Room(float needed)
        {
            if (y + needed <= bottom)
                return;
            page = pdf.AddPage();
            y = margin + 10;
        }

        // Header: business on the left, receipt details on the right.
        page.Text(margin, y + 6, brand.BusinessName, 18, bold: true);
        page.Text(right, y, $"{Strings.Print_Receipt} {receipt.Number}", 12, bold: true, align: PdfAlign.Right);
        page.Text(right, y + 15, AdminText.Date(receipt.Timestamp, culture), size, align: PdfAlign.Right);
        if (receipt.Seller is not null)
            page.Text(right, y + 29, $"{Strings.Print_Seller} : {receipt.Seller}", size, align: PdfAlign.Right);
        y += 26;
        foreach (var line in PdfMetrics.Wrap(brand.Header, size, 300))
        {
            page.Text(margin, y, line, size);
            y += 13;
        }
        y = Math.Max(y, margin + 50) + 14;

        // Lines.
        float[] columns = [margin + 4, right - 190, right - 95, right - 4];   // item, qty (right edge), unit price, amount
        page.FillRect(margin, y - 12, right - margin, row + 2, 0.92f);
        page.Text(columns[0], y, Strings.Col_Description, size, bold: true);
        page.Text(columns[1], y, Strings.Receipt_Quantity, size, bold: true, align: PdfAlign.Right);
        page.Text(columns[2], y, Strings.Receipt_UnitPrice, size, bold: true, align: PdfAlign.Right);
        page.Text(columns[3], y, Strings.Col_Amount, size, bold: true, align: PdfAlign.Right);
        y += row + 2;
        foreach (var line in receipt.Lines)
        {
            Room(row);
            page.TextFit(columns[0], y, LineLabel(line), size, columns[1] - columns[0] - 50);
            page.Text(columns[1], y, line.Quantity.ToString(culture), size, align: PdfAlign.Right);
            page.Text(columns[2], y, line.UnitPrice.ToString(), size, align: PdfAlign.Right);
            page.Text(columns[3], y, line.Amount.ToString(), size, align: PdfAlign.Right);
            page.Line(margin, y + 5, right, y + 5, 0.3f, 0.8f);
            y += row;
        }

        // Totals.
        Room(row * (3 + receipt.Vat.Count));
        y += 6;
        page.Text(columns[2], y, Strings.Print_Total.ToUpper(culture), 13, bold: true, align: PdfAlign.Right);
        page.Text(columns[3], y, receipt.Total.ToString(), 13, bold: true, align: PdfAlign.Right);
        y += row + 2;
        page.Text(columns[3], y, $"{Strings.Sell_Payment} : {AdminText.Payment(receipt.Method)}", size, align: PdfAlign.Right);
        foreach (var vat in receipt.Vat)
        {
            y += row - 2;
            page.Text(columns[3], y, $"{string.Format(culture, Strings.Receipt_VatIncluded, Percent(vat.Rate, culture))} : {vat.Vat}", size - 1, align: PdfAlign.Right);
        }
        y += row + 8;

        // Wi-Fi codes: cards of 2 per row, dashed so they can be cut out.
        const float cardHeight = 150, gap = 12;
        var cardWidth = (right - margin - gap) / 2;
        for (var i = 0; i < receipt.Vouchers.Count; i++)
        {
            var voucher = receipt.Vouchers[i];
            var column = i % 2;
            if (column == 0)
                Room(cardHeight + gap);
            var x = margin + column * (cardWidth + gap);
            DashedBox(page, x, y, cardWidth, cardHeight);

            var tx = x + 10;
            var ty = y + 18;
            var textWidth = cardWidth - 120;
            if (!string.IsNullOrEmpty(brand.WifiName))
            {
                page.TextFit(tx, ty, $"Wi-Fi : {brand.WifiName}", size - 1, textWidth);
                ty += 13;
            }
            page.TextFit(tx, ty, voucher.Tariff.Name, size + 1, textWidth, bold: true);
            ty += 13;
            page.TextFit(tx, ty, AdminText.Describe(voucher.Tariff, culture), size - 1.5f, textWidth);
            ty += 26;
            page.Text(tx, ty, VoucherCodeGenerator.Format(voucher.Code), 17, bold: true);
            ty += 18;
            foreach (var line in PdfMetrics.Wrap(Strings.Print_HowTo, 7.5f, textWidth))
            {
                page.Text(tx, ty, line, 7.5f);
                ty += 9.5f;
            }
            page.TextFit(tx, ty, brand.PortalAddress, 7.5f, textWidth);

            var matrix = QrSvg.Matrix(brand.CodeLink(voucher.Code));
            const float qrSize = 100;
            page.Modules(x + cardWidth - qrSize - 10, y + (cardHeight - qrSize) / 2, matrix, qrSize / matrix.GetLength(0));

            if (column == 1 || i == receipt.Vouchers.Count - 1)
                y += cardHeight + gap;
        }

        // Footer.
        Room(40);
        y += 8;
        foreach (var line in PdfMetrics.Wrap(brand.Footer, size, right - margin))
        {
            page.Text(width / 2, y, line, size, align: PdfAlign.Center);
            y += 13;
        }
        page.Text(width / 2, y + 4, Strings.Print_Thanks, size, bold: true, align: PdfAlign.Center);
        return pdf.ToBytes();
    }

    /// <summary>The shift report as an A4 report (summary, totals by type, every transaction).</summary>
    public static ReportDocument ShiftReportDocument(ShiftReport report, IReadOnlyList<Transaction> transactions, ReceiptBranding brand, string footer, CultureInfo culture)
    {
        var s = report.Shift;
        var summary = new ReportTable(Strings.ShiftReport_Summary, new ReportColumn(""), new ReportColumn("", Numeric: true));
        foreach (var (label, value) in SummaryLines(report))
            summary.Add(label, ReportCell.Money(value));

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

    /// <summary>"Coca 33 cl" or "1 heure (code Wi-Fi)".</summary>
    public static string LineLabel(ReceiptLine line) =>
        line.Type == TransactionType.VoucherSale ? $"{line.Description} ({Strings.Receipt_WifiCode})" : line.Description;

    /// <summary>Product name, Wi-Fi code or description of a transaction.</summary>
    public static string TransactionText(Transaction t) =>
        t.Voucher is not null ? $"{t.Description} {VoucherCodeGenerator.Format(t.Voucher.Code)}"
        : t.Quantity > 1 ? $"{t.Quantity} × {t.Description}" : t.Description ?? "";

    public static string Percent(decimal rate, CultureInfo culture) => (rate * 100).ToString("0.##", culture) + " %";

    private static IEnumerable<(string Label, Xaf Value)> SummaryLines(ShiftReport report)
    {
        yield return (Strings.Shift_OpeningCash, report.OpeningCash);
        yield return (Strings.Shift_CashIn, report.CashTransactionsTotal);
        yield return (Strings.Shift_Expected, report.ExpectedCash);
        if (report.CountedCash is { } counted)
        {
            yield return (Strings.Shift_Counted, counted);
            yield return (Strings.Shift_Difference, report.Difference ?? Xaf.Zero);
        }
        foreach (var (method, total) in report.TotalsByPaymentMethod.Where(t => t.Key != PaymentMethod.Cash).OrderBy(t => t.Key))
            yield return (AdminText.Payment(method), total);
    }

    private static void DashedBox(PdfPage page, float x, float y, float w, float h)
    {
        const float dash = 4;
        for (var d = 0f; d < w; d += 2 * dash)
        {
            page.Line(x + d, y, x + Math.Min(d + dash, w), y, 0.5f, 0.5f);
            page.Line(x + d, y + h, x + Math.Min(d + dash, w), y + h, 0.5f, 0.5f);
        }
        for (var d = 0f; d < h; d += 2 * dash)
        {
            page.Line(x, y + d, x, y + Math.Min(d + dash, h), 0.5f, 0.5f);
            page.Line(x + w, y + d, x + w, y + Math.Min(d + dash, h), 0.5f, 0.5f);
        }
    }
}

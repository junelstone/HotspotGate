using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Tests.Infrastructure;
using StonesHotspot.Web.Pages.Admin;
using static StonesHotspot.Tests.Infrastructure.StaffSignIn;

namespace StonesHotspot.Tests;

public sealed class BusinessPagesTests(ServiceSmokeTests.Factory factory) : IClassFixture<ServiceSmokeTests.Factory>
{
    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static async Task<string> Html(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private Task<Product> ProductAsync(string name, int price, int stock) =>
        InScope(sp => sp.GetRequiredService<ProductService>().CreateAsync(new Product { Name = name, Price = new Xaf(price), Stock = stock }, null));

    private Task<Tariff> PlanAsync() =>
        InScope(async sp => (await sp.GetRequiredService<TariffService>().ListAsync()).First(t => t.Name == "1 heure"));

    [Fact]
    public async Task Till_sale_shows_the_receipt_and_offers_an_a4_pdf()
    {
        var admin = await AsAsync(factory);
        var juice = await ProductAsync($"Jus {Guid.NewGuid():N}"[..12], 400, 5);
        var plan = await PlanAsync();

        var sale = await PostFormAsync(admin.Http, "/admin/till", "/admin/till", new()
        {
            ["Cart"] = $"p{juice.Id}x2,t{plan.Id}x1", ["Method"] = "Cash", ["Received"] = "2 000",
        });
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        var receiptUrl = sale.Headers.Location!.OriginalString;
        Assert.StartsWith("/admin/receipt?n=", receiptUrl);

        var page = await Html(await admin.Http.GetAsync(receiptUrl));
        Assert.Contains("Vente enregistrée", page);
        Assert.Contains("Monnaie à rendre : 1 000 FCFA", page.Replace('\u00a0', ' '));   // 2 000 − (2 × 400 + 200)
        Assert.Contains(juice.Name, page);
        var voucher = await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Vouchers.OrderByDescending(v => v.Id).FirstAsync());
        Assert.Equal(VoucherStatus.Sold, voucher.Status);
        Assert.Contains(StonesHotspot.Core.Rules.VoucherCodeGenerator.Format(voucher.Code), page);
        Assert.Equal(3, await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Products.Where(p => p.Id == juice.Id).Select(p => p.Stock).SingleAsync()));

        var pdf = await admin.Http.GetAsync(receiptUrl.Replace("?n=", "?handler=Pdf&n="));
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = Encoding.Latin1.GetString(await pdf.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("%PDF-1.4", bytes);
        Assert.Contains("/MediaBox [0 0 595.28 841.89]", bytes);   // A4
    }

    [Fact]
    public async Task Till_refuses_without_stock_or_shift_and_keeps_the_cart()
    {
        var admin = await AsAsync(factory);
        var cake = await ProductAsync($"Gâteau {Guid.NewGuid():N}"[..14], 300, 1);
        var cart = $"p{cake.Id}x2";

        var refused = await PostFormAsync(admin.Http, "/admin/till", "/admin/till", new() { ["Cart"] = cart, ["Method"] = "Cash" });
        var page = await Html(refused);
        Assert.Contains($"Stock insuffisant pour « {cake.Name} »", page);
        Assert.Contains($"value=\"{cart}\"", page);

        var cashier = await AsAsync(factory, StaffRole.Cashier);
        var noShift = await Html(await PostFormAsync(cashier.Http, "/admin/till", "/admin/till", new() { ["Cart"] = $"p{cake.Id}x1", ["Method"] = "Cash" }));
        Assert.Contains("Ouvrez d'abord votre caisse", noShift);
        Assert.Equal(1, await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Products.Where(p => p.Id == cake.Id).Select(p => p.Stock).SingleAsync()));
    }

    [Theory]
    [InlineData("t1x2,p3x10", 2)]
    [InlineData("", 0)]
    public void Till_cart_text_is_parsed(string text, int count) => Assert.Equal(count, TillModel.ParseCart(text)!.Count);

    [Theory]
    [InlineData("x1x2")]
    [InlineData("t1")]
    [InlineData("p-1x2")]
    public void Malformed_cart_text_is_rejected(string text) => Assert.Null(TillModel.ParseCart(text));

    [Fact]
    public async Task Reports_show_and_export_and_pro_ones_are_gated()
    {
        var admin = await AsAsync(factory);
        var water = await ProductAsync($"Eau {Guid.NewGuid():N}"[..12], 250, 50);
        await PostFormAsync(admin.Http, "/admin/till", "/admin/till", new() { ["Cart"] = $"p{water.Id}x4", ["Method"] = "AirtelMoney" });

        foreach (var kind in ReportsModel.Kinds)
            Assert.Contains(ReportsModel.Title(kind), await Html(await admin.Http.GetAsync($"/admin/reports?r={kind}")));

        var csv = await admin.Http.GetAsync("/admin/reports?handler=Csv&r=transactions&type=ProductSale");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.Contains($";4 × {water.Name};Airtel Money;", text);
        Assert.Contains(";1000\n", text);
        var pdf = await admin.Http.GetAsync("/admin/reports?handler=Pdf&r=turnover&monthly=true");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

        factory.Licence.Edition = Edition.Basic;
        try
        {
            Assert.Contains("édition Pro", await Html(await admin.Http.GetAsync("/admin/reports?r=turnover")));
            Assert.Equal(HttpStatusCode.NotFound, (await admin.Http.GetAsync("/admin/reports?handler=Csv&r=turnover")).StatusCode);
            Assert.DoesNotContain("édition Pro", await Html(await admin.Http.GetAsync("/admin/reports?r=plans")));
            Assert.Contains("édition Pro", await Html(await admin.Http.GetAsync("/admin/till")));
        }
        finally
        {
            factory.Licence.Edition = Edition.Trial;
        }
    }

    [Fact]
    public async Task Products_page_adds_stock_and_cashiers_cannot_open_admin_pages()
    {
        var admin = await AsAsync(factory);
        var soap = await ProductAsync($"Savon {Guid.NewGuid():N}"[..12], 150, 2);

        var added = await PostFormAsync(admin.Http, "/admin/products", "/admin/products?handler=AddStock", new() { ["id"] = $"{soap.Id}", ["quantity"] = "10" });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Contains($"{soap.Name} : stock 12", await Html(await admin.Http.GetAsync("/admin/products")));
        var audit = await Html(await admin.Http.GetAsync($"/admin/audit?q={Uri.EscapeDataString(soap.Name)}"));
        Assert.Contains("Product.StockAdded", audit);
        var auditCsv = await admin.Http.GetAsync($"/admin/audit?handler=Csv&q={Uri.EscapeDataString(soap.Name)}");
        Assert.Contains("Product.StockAdded", await auditCsv.Content.ReadAsStringAsync());

        var cashier = await AsAsync(factory, StaffRole.Cashier);
        foreach (var url in new[] { "/admin/products", "/admin/product", "/admin/reports", "/admin/audit" })
            Assert.Contains("/admin/denied", (await cashier.Http.GetAsync(url)).Headers.Location?.OriginalString ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await cashier.Http.GetAsync("/admin/till")).StatusCode);
    }

    [Fact]
    public async Task Shift_report_is_offered_as_pdf()
    {
        var cashier = await AsAsync(factory, StaffRole.Cashier);
        await PostFormAsync(cashier.Http, "/admin/shift", "/admin/shift?handler=Open", new() { ["Amount"] = "1000" });
        var shift = await InScope(sp => sp.GetRequiredService<CashService>().GetOpenShiftAsync(cashier.Staff.Id));
        var water = await ProductAsync($"Eau {Guid.NewGuid():N}"[..12], 250, 50);
        await PostFormAsync(cashier.Http, "/admin/till", "/admin/till", new() { ["Cart"] = $"p{water.Id}x2", ["Method"] = "Cash" });
        await PostFormAsync(cashier.Http, "/admin/shift", "/admin/shift?handler=Close", new() { ["shiftId"] = $"{shift!.Id}", ["Amount"] = "1500" });

        var pdf = await cashier.Http.GetAsync($"/admin/shift?handler=Pdf&id={shift.Id}");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var page = await Html(await cashier.Http.GetAsync($"/admin/shift?id={shift.Id}"));
        Assert.Contains($"2 × {water.Name}", page);
        Assert.Contains("/admin/receipt?n=", page);
    }
}

using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class BusinessToolsTests
{
    private static readonly TimeZoneInfo Congo = TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "WAT", "WAT");

    private static Task<Product> CreateProductAsync(TestHost host, string name, int price, int stock = 10, bool track = true, decimal vat = 0) =>
        host.Run<ProductService, Product>(s => s.CreateAsync(
            new Product { Name = name, Price = new Xaf(price), Stock = stock, TrackStock = track, VatRate = vat }, null));

    private static Task<string> CheckoutAsync(TestHost host, Staff seller, PaymentMethod method = PaymentMethod.Cash, bool shifts = false, params CartLine[] cart) =>
        host.Run<SalesService, string>(s => s.CheckoutAsync(cart, method, seller, shifts));

    private static Task<int> StockAsync(TestHost host, int productId) =>
        host.Query(db => db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync());

    [Fact]
    public async Task Checkout_sells_products_and_wifi_codes_on_one_receipt()
    {
        await using var host = await TestHost.CreateAsync();
        var admin = await host.AdminAsync();
        var coke = await CreateProductAsync(host, "Coca 33 cl", 500, stock: 5, vat: 0.189m);
        var tariff = await host.CreateTariffAsync(price: 200, name: "Une heure");

        var number = await CheckoutAsync(host, admin, PaymentMethod.Cash, false,
            new CartLine(CartItemKind.Product, coke.Id, 2), new CartLine(CartItemKind.Plan, tariff.Id, 2), new CartLine(CartItemKind.Product, coke.Id, 1));

        Assert.Equal(2, await StockAsync(host, coke.Id));
        var receipt = (await host.Run<SalesService, Receipt?>(s => s.GetReceiptAsync(number)))!;
        Assert.Equal(new Xaf(3 * 500 + 2 * 200), receipt.Total);
        var cokeLine = receipt.Lines.Single(l => l.Type == TransactionType.ProductSale);
        Assert.Equal((3, new Xaf(500), new Xaf(1500)), (cokeLine.Quantity, cokeLine.UnitPrice, cokeLine.Amount));
        var plan = receipt.Lines.Single(l => l.Type == TransactionType.VoucherSale);
        Assert.Equal((2, new Xaf(400)), (plan.Quantity, plan.Amount));
        Assert.Equal(2, receipt.Vouchers.Count);
        Assert.All(receipt.Vouchers, v => Assert.Equal(VoucherStatus.Sold, v.Status));
        Assert.Equal(new Xaf(238), receipt.Vat.Single().Vat);   // 1 500 × 0.189 / 1.189 = 238.4
        var lines = await host.Query(db => db.Transactions.Where(t => t.ReceiptNumber == number).ToListAsync());
        Assert.Equal(3, lines.Count);                                    // the two coke lines merged + 2 codes
        Assert.All(lines.Where(t => t.ProductId != null), t => Assert.Equal(0.189m, t.VatRate));
    }

    [Fact]
    public async Task A_refused_sale_changes_nothing()
    {
        await using var host = await TestHost.CreateAsync();
        var admin = await host.AdminAsync();
        var water = await CreateProductAsync(host, "Eau", 300, stock: 10);
        var coke = await CreateProductAsync(host, "Coca", 500, stock: 1);
        var tariff = await host.CreateTariffAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => CheckoutAsync(host, admin, PaymentMethod.Cash, false,
            new CartLine(CartItemKind.Product, water.Id, 2), new CartLine(CartItemKind.Plan, tariff.Id, 1), new CartLine(CartItemKind.Product, coke.Id, 2)));

        Assert.Equal((ErrorCodes.OutOfStock, "Coca"), (ex.Code, ex.Subject));
        Assert.Equal(10, await StockAsync(host, water.Id));
        Assert.Equal(1, await StockAsync(host, coke.Id));
        Assert.Equal(0, await host.Query(db => db.Transactions.CountAsync()));
        Assert.Equal(0, await host.Query(db => db.Vouchers.CountAsync()));
        // The receipt number was not used up either.
        Assert.Equal("2026-000001", await CheckoutAsync(host, admin, PaymentMethod.Cash, false, new CartLine(CartItemKind.Product, coke.Id, 1)));
        Assert.Equal(0, await StockAsync(host, coke.Id));
    }

    [Fact]
    public async Task Services_without_stock_never_run_out()
    {
        await using var host = await TestHost.CreateAsync();
        var printing = await CreateProductAsync(host, "Impression", 100, stock: 0, track: false);

        await CheckoutAsync(host, await host.AdminAsync(), PaymentMethod.Cash, false, new CartLine(CartItemKind.Product, printing.Id, 40));

        Assert.Equal(0, await StockAsync(host, printing.Id));
    }

    [Fact]
    public async Task Cashiers_need_an_open_shift_when_shifts_are_on()
    {
        await using var host = await TestHost.CreateAsync();
        var cashier = await host.CreateCashierAsync();
        var water = await CreateProductAsync(host, "Eau", 300);
        var line = new CartLine(CartItemKind.Product, water.Id, 1);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => CheckoutAsync(host, cashier, PaymentMethod.Cash, true, line));
        Assert.Equal(ErrorCodes.ShiftRequired, ex.Code);
        await CheckoutAsync(host, cashier, PaymentMethod.Cash, false, line);   // Basic edition: no shifts

        var shift = await host.Run<CashService, Shift>(s => s.OpenShiftAsync(cashier, Xaf.Zero));
        var number = await CheckoutAsync(host, cashier, PaymentMethod.Cash, true, line);
        var report = await host.Run<CashService, ShiftReport>(s => s.GetShiftReportAsync(shift.Id));
        Assert.Equal(new Xaf(300), report.TotalsByType[TransactionType.ProductSale]);
        Assert.Equal(number, (await host.Query(db => db.Transactions.SingleAsync(t => t.ShiftId == shift.Id))).ReceiptNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Bad_carts_are_refused(int quantity)
    {
        await using var host = await TestHost.CreateAsync();
        var admin = await host.AdminAsync();
        var water = await CreateProductAsync(host, "Eau", 300, stock: 5000);
        var inactive = await CreateProductAsync(host, "Ancien", 300);
        await host.Run<ProductService, Product>(s => s.UpdateAsync(inactive.Id, new Product { Name = "Ancien", Price = new Xaf(300), IsActive = false, TrackStock = true }, null));

        var empty = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CheckoutAsync(host, admin, PaymentMethod.Cash, false, quantity == 0 ? [new CartLine(CartItemKind.Product, water.Id, 0)] : []));
        Assert.Equal(ErrorCodes.EmptyCart, empty.Code);
        if (quantity > 0)
            await Assert.ThrowsAsync<BusinessRuleException>(() => CheckoutAsync(host, admin, PaymentMethod.Cash, false, new CartLine(CartItemKind.Product, water.Id, quantity)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => CheckoutAsync(host, admin, PaymentMethod.Cash, false, new CartLine(CartItemKind.Product, inactive.Id, 1)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => CheckoutAsync(host, admin, PaymentMethod.AccountBalance, false, new CartLine(CartItemKind.Product, water.Id, 1)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => CheckoutAsync(host, admin, PaymentMethod.Cash, false, new CartLine(CartItemKind.Plan, 999, 1)));
        Assert.Equal(0, await host.Query(db => db.Transactions.CountAsync()));
    }

    [Fact]
    public async Task Products_and_stock_changes_are_validated_and_audited()
    {
        await using var host = await TestHost.CreateAsync();
        var admin = await host.AdminAsync();
        var group = await host.Run<ProductService, ProductGroup>(s => s.CreateGroupAsync("Boissons", admin));
        var product = await host.Run<ProductService, Product>(s => s.CreateAsync(
            new Product { Name = "Café", GroupId = group.Id, Price = new Xaf(250), Stock = 4 }, admin));

        var dup = await Assert.ThrowsAsync<BusinessRuleException>(() => CreateProductAsync(host, "CAFÉ", 300));
        Assert.Equal(ErrorCodes.DuplicateName, dup.Code);
        await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<ProductService, ProductGroup>(s => s.CreateGroupAsync(" boissons ", admin)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateProductAsync(host, "Négatif", -1));
        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateProductAsync(host, "TVA", 100, vat: 1m));

        await host.Run<ProductService, Product>(s => s.AddStockAsync(product.Id, 20, admin));
        Assert.Equal(24, await StockAsync(host, product.Id));
        await host.Run<ProductService, Product>(s => s.CountStockAsync(product.Id, 21, admin));
        Assert.Equal(21, await StockAsync(host, product.Id));
        // Editing the product never touches the stock.
        await host.Run<ProductService, Product>(s => s.UpdateAsync(product.Id, new Product { Name = "Café noir", Price = new Xaf(300), Stock = 999, TrackStock = true, IsActive = true }, admin));
        Assert.Equal(21, await StockAsync(host, product.Id));

        var audit = await host.Query(db => db.AuditLogs.Where(a => a.Action.StartsWith("Product")).Select(a => a.Action + " " + a.Details).ToListAsync());
        Assert.Contains(audit, a => a.Contains("StockAdded") && a.Contains("+20 (4 → 24)"));
        Assert.Contains(audit, a => a.Contains("StockCounted") && a.Contains("24 → 21 (difference -3)"));
        Assert.Contains(audit, a => a.Contains("Updated") && a.Contains($"price {new Xaf(250)} → {new Xaf(300)}"));

        // Sold products are kept for history; unsold ones can go. Deleting a group keeps its products.
        await CheckoutAsync(host, admin, PaymentMethod.Cash, false, new CartLine(CartItemKind.Product, product.Id, 1));
        var inUse = await Assert.ThrowsAsync<BusinessRuleException>(() => host.Run<ProductService>(s => s.DeleteAsync(product.Id, admin)));
        Assert.Equal(ErrorCodes.InUse, inUse.Code);
        var spare = await CreateProductAsync(host, "Biscuit", 100);
        await host.Run<ProductService>(s => s.DeleteAsync(spare.Id, admin));
        await host.Run<ProductService>(s => s.DeleteGroupAsync(group.Id, admin));
        Assert.Null(await host.Query(db => db.Products.Where(p => p.Id == product.Id).Select(p => p.GroupId).SingleAsync()));
    }

    [Theory]
    [InlineData(1000, "0.189", 159)]   // 158.96
    [InlineData(500, "0.18", 76)]      // 76.27
    [InlineData(118, "0.18", 18)]
    [InlineData(0, "0.189", 0)]
    public void Included_vat_is_rounded_to_the_franc(long amount, string rate, long vat) =>
        Assert.Equal(new Xaf(vat), SalesService.IncludedVat(new Xaf(amount), decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task Turnover_uses_local_days_and_leaves_out_account_charges()
    {
        await using var host = await TestHost.CreateAsync();
        host.Clock.SetLocalTimeZone(Congo);
        var admin = await host.AdminAsync();
        var water = await CreateProductAsync(host, "Eau", 300, stock: 100);
        var tariff = await host.CreateTariffAsync(price: 200, name: "Une heure");

        host.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 2, 22, 30, 0, TimeSpan.Zero));   // 23:30 local, 2 March
        await CheckoutAsync(host, admin, PaymentMethod.Cash, false, new CartLine(CartItemKind.Product, water.Id, 1));
        host.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 2, 23, 30, 0, TimeSpan.Zero));   // 00:30 local, 3 March
        await CheckoutAsync(host, admin, PaymentMethod.MtnMoMo, false, new CartLine(CartItemKind.Plan, tariff.Id, 2));
        await host.Query(async db =>
        {
            db.Transactions.Add(new Transaction
            {
                Type = TransactionType.AccountCharge, Amount = new Xaf(5000), PaymentMethod = PaymentMethod.AccountBalance,
                Timestamp = host.Now, ReceiptNumber = "X",
            });
            return await db.SaveChangesAsync();
        });
        host.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero));
        await CheckoutAsync(host, admin, PaymentMethod.Cash, false, new CartLine(CartItemKind.Product, water.Id, 3));

        var range = new DateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30));
        var days = await host.Run<ReportService, List<TurnoverRow>>(s => s.TurnoverAsync(range, byMonth: false));
        Assert.Equal([new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3), new DateOnly(2026, 4, 10)], days.Select(d => d.Period));
        Assert.Equal([new Xaf(300), new Xaf(400), new Xaf(900)], days.Select(d => d.Total));
        Assert.Equal(new Xaf(400), days[1].ByType[TransactionType.VoucherSale]);

        var months = await host.Run<ReportService, List<TurnoverRow>>(s => s.TurnoverAsync(range, byMonth: true));
        Assert.Equal([new Xaf(700), new Xaf(900)], months.Select(m => m.Total));

        var march3 = await host.Run<ReportService, List<TurnoverRow>>(s => s.TurnoverAsync(new DateRange(new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 3)), false));
        Assert.Equal(new Xaf(400), march3.Single().Total);

        var plans = await host.Run<ReportService, List<PlanSalesRow>>(s => s.VoucherSalesByPlanAsync(range));
        Assert.Equal(new PlanSalesRow("Une heure", 2, new Xaf(400)), plans.Single());

        var momo = await host.Run<ReportService, List<Transaction>>(s => s.TransactionsAsync(range, new TransactionFilter(Method: PaymentMethod.MtnMoMo)));
        Assert.Equal(2, momo.Count);
        Assert.All(momo, t => Assert.NotNull(t.Voucher));
        var products = await host.Run<ReportService, List<Transaction>>(s => s.TransactionsAsync(range, new TransactionFilter(Type: TransactionType.ProductSale, StaffId: admin.Id)));
        Assert.Equal(2, products.Count);

        var byStaff = (await host.Run<ReportService, List<StaffSalesRow>>(s => s.SalesByStaffAsync(range))).Single();
        Assert.Equal((3, new Xaf(1600), new Xaf(1200)), (byStaff.Receipts, byStaff.Total, byStaff.ByMethod[PaymentMethod.Cash]));
    }

    [Fact]
    public async Task Shift_report_rows_match_the_shifts()
    {
        await using var host = await TestHost.CreateAsync();
        var cashier = await host.CreateCashierAsync();
        var water = await CreateProductAsync(host, "Eau", 300, stock: 100);
        var shift = await host.Run<CashService, Shift>(s => s.OpenShiftAsync(cashier, new Xaf(2000)));
        await CheckoutAsync(host, cashier, PaymentMethod.Cash, true, new CartLine(CartItemKind.Product, water.Id, 2));
        await CheckoutAsync(host, cashier, PaymentMethod.AirtelMoney, true, new CartLine(CartItemKind.Product, water.Id, 1));
        await host.Run<CashService, ShiftReport>(s => s.CloseShiftAsync(shift.Id, new Xaf(2500), cashier));

        var rows = await host.Run<ReportService, List<ShiftRow>>(s => s.ShiftsAsync(new DateRange(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2))));

        var row = rows.Single();
        Assert.Equal((new Xaf(600), new Xaf(2600), new Xaf(2500), new Xaf(-100), new Xaf(900), 2),
            (row.CashIn, row.ExpectedCash, row.CountedCash!.Value, row.Difference!.Value, row.Total, row.Count));
        Assert.Equal("caissier", row.Staff);
    }

    [Fact]
    public async Task Sessions_report_counts_devices_methods_and_peak_hours()
    {
        await using var host = await TestHost.CreateAsync();
        host.Clock.SetLocalTimeZone(Congo);
        var tariff = await host.CreateTariffAsync();
        var day = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        await host.Query(async db =>
        {
            var phone = new Device { Mac = TestHost.PhoneMac, FirstSeen = day, LastSeen = day };
            var laptop = new Device { Mac = TestHost.LaptopMac, FirstSeen = day, LastSeen = day };
            db.Devices.AddRange(phone, laptop);
            Session S(Device d, LoginMethod m, int startHour, int startMinute, int minutes, long bytes) => new()
            {
                Device = d, Method = m, TariffId = tariff.Id,
                StartedAt = day.AddHours(startHour).AddMinutes(startMinute), EndedAt = day.AddHours(startHour).AddMinutes(startMinute + minutes),
                LastActivityAt = day, LastAccountedAt = day, BytesDown = bytes, BytesUp = bytes / 10,
            };
            // UTC 9:15–11:05 = local 10:15–12:05: hours 10, 11 and 12. UTC 10:00–10:30 = local 11 h.
            db.Sessions.AddRange(S(phone, LoginMethod.Voucher, 9, 15, 110, 1000), S(laptop, LoginMethod.Account, 10, 0, 30, 3000),
                S(phone, LoginMethod.Voucher, 30, 0, 10, 500));   // next day: outside a one-day range
            return await db.SaveChangesAsync();
        });

        var oneDay = await host.Run<ReportService, SessionsReport>(s => s.SessionsAsync(new DateRange(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2))));
        Assert.Equal((2, 2, 4000L, 400L), (oneDay.Sessions, oneDay.Devices, oneDay.BytesDown, oneDay.BytesUp));
        Assert.Equal(TimeSpan.FromMinutes(70), oneDay.AverageDuration);
        Assert.Equal(1, oneDay.ByMethod[LoginMethod.Account]);
        Assert.Equal([1.0, 2.0, 1.0], oneDay.PeakHours.Skip(10).Take(3));
        Assert.Equal(4.0, oneDay.PeakHours.Sum());

        var twoDays = await host.Run<ReportService, SessionsReport>(s => s.SessionsAsync(new DateRange(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3))));
        Assert.Equal((3, 2), (twoDays.Sessions, twoDays.Devices));
        Assert.Equal(1.0, twoDays.PeakHours[11]);   // 2 sessions over 2 days
    }

    [Fact]
    public async Task Audit_search_ignores_case_and_accents_case()
    {
        await using var host = await TestHost.CreateAsync();
        var admin = await host.AdminAsync();
        var cashier = await host.CreateCashierAsync();
        await host.Run<ProductService, ProductGroup>(s => s.CreateGroupAsync("Électronique", admin));
        await host.Run<CashService, Shift>(s => s.OpenShiftAsync(cashier, Xaf.Zero));
        var from = host.Now.AddHours(-1);
        var to = host.Now.AddHours(1);

        var found = await host.Run<AuditService, List<AuditLog>>(s => s.QueryAsync(from, to, null, "électronique"));
        Assert.Equal("ProductGroup.Created", found.Single().Action);
        var byCashier = await host.Run<AuditService, List<AuditLog>>(s => s.QueryAsync(from, to, cashier.Id, null));
        Assert.Equal("Shift.Opened", byCashier.Single().Action);
        Assert.Empty(await host.Run<AuditService, List<AuditLog>>(s => s.QueryAsync(to, to.AddDays(1), null, null)));
    }
}

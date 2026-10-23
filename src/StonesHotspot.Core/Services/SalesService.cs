using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Services;

public enum CartItemKind { Product, Plan }

/// <summary>One line of the till's cart: <paramref name="Quantity"/> × a product or a Wi-Fi plan (tariff).</summary>
public sealed record CartLine(CartItemKind Kind, int Id, int Quantity);

/// <param name="Lines">Lines of the same item at the same price are merged ("3 × Coca 33 cl").</param>
public sealed record Receipt(
    string Number,
    DateTime Timestamp,
    PaymentMethod Method,
    string? Seller,
    IReadOnlyList<ReceiptLine> Lines,
    IReadOnlyList<Voucher> Vouchers,
    IReadOnlyList<VatLine> Vat)
{
    public Xaf Total => Xaf.Sum(Lines.Select(l => l.Amount));
}

public sealed record ReceiptLine(TransactionType Type, string Description, int Quantity, Xaf UnitPrice, Xaf Amount, decimal VatRate);

/// <summary>VAT included in the lines at one rate: <c>Vat = Amount × rate / (1 + rate)</c>, rounded to the franc.</summary>
public sealed record VatLine(decimal Rate, Xaf Amount, Xaf Vat)
{
    public Xaf WithoutVat => Amount - Vat;
}

/// <summary>The till: products and Wi-Fi codes sold together on one receipt.</summary>
public sealed class SalesService(
    HotspotDbContext db, CashService cash, VoucherService vouchers, AuditService audit, TimeProvider clock)
{
    public const int MaxPlanQuantity = 50;
    public const int MaxProductQuantity = 1000;

    /// <summary>
    /// Sells the cart in one database transaction: stock goes down, Wi-Fi codes are created and sold, every line gets
    /// the same receipt number — all or nothing. With <paramref name="shiftsEnabled"/>, cashiers need an open shift.
    /// </summary>
    /// <returns>The receipt number.</returns>
    public async Task<string> CheckoutAsync(
        IReadOnlyCollection<CartLine> cart, PaymentMethod method, Staff seller, bool shiftsEnabled, CancellationToken ct = default)
    {
        var lines = cart.Where(l => l.Quantity != 0)
            .GroupBy(l => (l.Kind, l.Id)).Select(g => new CartLine(g.Key.Kind, g.Key.Id, g.Sum(l => l.Quantity))).ToList();
        if (lines.Count == 0)
            throw new BusinessRuleException(ErrorCodes.EmptyCart, "The cart is empty.");
        if (method == PaymentMethod.AccountBalance)
            throw new BusinessRuleException(ErrorCodes.Invalid, "The till does not take account balances.");
        foreach (var line in lines)
        {
            var max = line.Kind == CartItemKind.Plan ? MaxPlanQuantity : MaxProductQuantity;
            if (line.Quantity < 1 || line.Quantity > max)
                throw new BusinessRuleException(ErrorCodes.Invalid, $"Quantity must be between 1 and {max}.");
        }
        if (shiftsEnabled && seller.Role == StaffRole.Cashier && await cash.GetOpenShiftAsync(seller.Id, ct) is null)
            throw new BusinessRuleException(ErrorCodes.ShiftRequired, "Open a shift before selling.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            return await SellLinesAsync(lines, method, seller, ct, transaction);
        }
        catch
        {
            // The database rolls back; forget the half-made changes too so a later save cannot write them.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<string> SellLinesAsync(
        List<CartLine> lines, PaymentMethod method, Staff seller, CancellationToken ct,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        var receipt = await cash.NextReceiptNumberAsync(ct);
        var total = Xaf.Zero;

        foreach (var line in lines)
        {
            if (line.Kind == CartItemKind.Product)
            {
                var product = await db.Products.FirstOrDefaultAsync(p => p.Id == line.Id && p.IsActive, ct)
                              ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Active product {line.Id} not found.");
                if (product.TrackStock)
                {
                    if (product.Stock < line.Quantity)
                        throw new BusinessRuleException(ErrorCodes.OutOfStock,
                            $"{product.Name}: {product.Stock} in stock, {line.Quantity} asked.", product.Name);
                    product.Stock -= line.Quantity;
                }
                var amount = product.Price * line.Quantity;
                total += amount;
                await cash.AddTransactionAsync(TransactionType.ProductSale, amount, method, seller, receipt, ct, t =>
                {
                    t.ProductId = product.Id;
                    t.Quantity = line.Quantity;
                    t.Description = product.Name;
                    t.VatRate = product.VatRate;
                });
            }
            else
            {
                var batch = $"Vente {clock.GetUtcNow().UtcDateTime:yyyy-MM-dd}";
                var codes = await vouchers.GenerateBatchAsync(line.Id, line.Quantity, batch, singleUse: false, seller, ct);
                total += Xaf.Sum(codes.Select(v => v.Tariff.Price));
                await vouchers.AddSaleLinesAsync(codes, method, seller, receipt, ct);
            }
        }

        audit.Record(seller, "Sale", $"Receipt {receipt}: {lines.Count} line(s), {total}");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return receipt;
    }

    /// <summary>Everything printed on a receipt, or null when the number does not exist.</summary>
    public async Task<Receipt?> GetReceiptAsync(string number, CancellationToken ct = default)
    {
        var transactions = await db.Transactions.AsNoTracking().Include(t => t.Staff)
            .Where(t => t.ReceiptNumber == number).OrderBy(t => t.Id).ToListAsync(ct);
        if (transactions.Count == 0)
            return null;

        var first = transactions[0];
        var lines = transactions
            .GroupBy(t => (t.Type, t.ProductId, Description: t.Description ?? "", Unit: UnitPrice(t), t.VatRate))
            .Select(g => new ReceiptLine(g.Key.Type, g.Key.Description, g.Sum(t => t.Quantity), g.Key.Unit,
                Xaf.Sum(g.Select(t => t.Amount)), g.Key.VatRate))
            .ToList();
        var vat = lines.Where(l => l.VatRate > 0).GroupBy(l => l.VatRate)
            .Select(g =>
            {
                var amount = Xaf.Sum(g.Select(l => l.Amount));
                return new VatLine(g.Key, amount, IncludedVat(amount, g.Key));
            })
            .OrderBy(v => v.Rate).ToList();

        return new Receipt(number, first.Timestamp, first.PaymentMethod, first.Staff?.DisplayName ?? first.Staff?.Username,
            lines, await vouchers.ListByReceiptAsync(number, ct), vat);
    }

    /// <summary>The VAT contained in a VAT-inclusive amount, rounded half away from zero to the franc.</summary>
    public static Xaf IncludedVat(Xaf amountWithVat, decimal rate) =>
        new((long)Math.Round(amountWithVat.Amount * rate / (1 + rate), MidpointRounding.AwayFromZero));

    private static Xaf UnitPrice(Transaction t) => t.Quantity > 1 ? new Xaf(t.Amount.Amount / t.Quantity) : t.Amount;
}

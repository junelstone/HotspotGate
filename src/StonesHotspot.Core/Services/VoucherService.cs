using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Rules;

namespace StonesHotspot.Core.Services;

/// <summary>One batch of vouchers with how many are in each state.</summary>
public sealed record BatchSummary(string Name, string TariffName, int Total, int New, int Sold, int Active, int Expired, int Disabled, DateTime CreatedAt);

public sealed class VoucherService(HotspotDbContext db, CashService cash, AuditService audit, TimeProvider clock)
{
    public const int MaxBatchSize = 1000;

    public Task<Voucher?> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        var normalized = VoucherCodeGenerator.Normalize(code);
        return db.Vouchers.Include(v => v.Tariff).FirstOrDefaultAsync(v => v.Code == normalized, ct);
    }

    public Task<List<Voucher>> ListBatchAsync(string batchName, CancellationToken ct = default) =>
        db.Vouchers.Include(v => v.Tariff).Where(v => v.BatchName == batchName).OrderBy(v => v.Id).ToListAsync(ct);

    public async Task<List<BatchSummary>> ListBatchesAsync(CancellationToken ct = default)
    {
        var groups = await db.Vouchers.Where(v => v.Source == VoucherSource.Staff)
            .GroupBy(v => new { v.BatchName, v.TariffId })
            .Select(g => new
            {
                g.Key.BatchName,
                g.Key.TariffId,
                Total = g.Count(),
                New = g.Count(v => v.Status == VoucherStatus.New),
                Sold = g.Count(v => v.Status == VoucherStatus.Sold),
                Active = g.Count(v => v.Status == VoucherStatus.Active),
                Expired = g.Count(v => v.Status == VoucherStatus.Expired),
                Disabled = g.Count(v => v.Status == VoucherStatus.Disabled),
                CreatedAt = g.Min(v => v.CreatedAt),
            })
            .ToListAsync(ct);
        var tariffNames = await db.Tariffs.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return groups
            .Select(g => new BatchSummary(g.BatchName, tariffNames.GetValueOrDefault(g.TariffId, "?"), g.Total, g.New, g.Sold, g.Active, g.Expired, g.Disabled, g.CreatedAt))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    /// <summary>Vouchers sold on one receipt (for printing the customer's ticket).</summary>
    public Task<List<Voucher>> ListByReceiptAsync(string receiptNumber, CancellationToken ct = default) =>
        db.Vouchers.Include(v => v.Tariff)
            .Where(v => db.Transactions.Any(t => t.ReceiptNumber == receiptNumber && t.VoucherId == v.Id))
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

    /// <summary>
    /// Cashier sale: creates <paramref name="quantity"/> codes for the plan and sells them at once (one receipt).
    /// </summary>
    public async Task<(string Receipt, List<Voucher> Vouchers)> SellNewAsync(
        int tariffId, int quantity, PaymentMethod method, Staff seller, CancellationToken ct = default)
    {
        if (quantity is < 1 or > 50)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Quantity must be between 1 and 50.");
        if (method == PaymentMethod.AccountBalance)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Vouchers cannot be paid from an account balance.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var batch = $"Vente {clock.GetUtcNow().UtcDateTime:yyyy-MM-dd}";
        var vouchers = await GenerateBatchAsync(tariffId, quantity, batch, singleUse: false, seller, ct);
        var receipt = await SellAsync(vouchers.Select(v => v.Id).ToList(), method, seller, ct);
        await transaction.CommitAsync(ct);
        return (receipt, vouchers);
    }

    /// <summary>Sells a pre-printed card by its code. Returns the receipt number.</summary>
    public async Task<string> SellCodeAsync(string code, PaymentMethod method, Staff seller, CancellationToken ct = default)
    {
        var voucher = await FindByCodeAsync(code, ct) ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Code '{code}' not found.");
        return await SellAsync([voucher.Id], method, seller, ct);
    }

    /// <summary>Disables the vouchers of a batch that were not used yet (lost or stolen cards).</summary>
    public async Task<int> DisableUnusedInBatchAsync(string batchName, Staff? actor, CancellationToken ct = default)
    {
        var vouchers = await db.Vouchers.Where(v => v.BatchName == batchName && (v.Status == VoucherStatus.New || v.Status == VoucherStatus.Sold)).ToListAsync(ct);
        foreach (var voucher in vouchers)
            voucher.Status = VoucherStatus.Disabled;
        audit.Record(actor, "Vouchers.Disabled", $"{vouchers.Count} unused voucher(s) of batch '{batchName}'");
        await db.SaveChangesAsync(ct);
        return vouchers.Count;
    }

    /// <summary>
    /// A voucher made by the portal for a free, password or SMS login (or Mobile Money): not part of any batch shown
    /// to staff, not audited one by one. Saved.
    /// </summary>
    internal async Task<Voucher> CreateForDeviceAsync(Tariff tariff, VoucherSource source, int? deviceId, string? phone, CancellationToken ct)
    {
        string code;
        do
            code = VoucherCodeGenerator.Next();
        while (await db.Vouchers.AnyAsync(v => v.Code == code, ct));

        var voucher = new Voucher
        {
            Code = code,
            BatchName = $"({source})",
            TariffId = tariff.Id,
            Tariff = tariff,
            Source = source,
            IssuedToDeviceId = deviceId,
            Phone = phone,
            SingleUse = false,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync(ct);
        return voucher;
    }

    /// <summary>Creates <paramref name="count"/> new vouchers with unique random codes.</summary>
    public async Task<List<Voucher>> GenerateBatchAsync(
        int tariffId, int count, string batchName, bool singleUse, Staff? actor, CancellationToken ct = default)
    {
        if (count is < 1 or > MaxBatchSize)
            throw new BusinessRuleException(ErrorCodes.Invalid, $"Batch size must be between 1 and {MaxBatchSize}.");
        var tariff = await db.Tariffs.FirstOrDefaultAsync(t => t.Id == tariffId && t.IsActive, ct)
                     ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Active tariff {tariffId} not found.");

        var now = clock.GetUtcNow().UtcDateTime;
        if (string.IsNullOrWhiteSpace(batchName))
            batchName = $"{tariff.Name} {now:yyyy-MM-dd HH:mm}";
        batchName = batchName.Trim();

        var codes = new HashSet<string>();
        while (codes.Count < count)
        {
            var candidates = Enumerable.Range(0, count - codes.Count).Select(_ => VoucherCodeGenerator.Next())
                .Where(c => !codes.Contains(c)).Distinct().ToList();
            var taken = await db.Vouchers.Where(v => candidates.Contains(v.Code)).Select(v => v.Code).ToListAsync(ct);
            codes.UnionWith(candidates.Except(taken));
        }

        var vouchers = codes.Select(code => new Voucher
        {
            Code = code,
            BatchName = batchName,
            TariffId = tariff.Id,
            Tariff = tariff,
            SingleUse = singleUse,
            CreatedAt = now,
        }).ToList();

        db.Vouchers.AddRange(vouchers);
        audit.Record(actor, "Vouchers.Generated", $"{count} × {tariff.Name}, batch '{batchName}'");
        await db.SaveChangesAsync(ct);
        return vouchers;
    }

    /// <summary>
    /// Sells new vouchers at their tariff price. All lines share one receipt number, which is returned.
    /// </summary>
    public async Task<string> SellAsync(IReadOnlyCollection<int> voucherIds, PaymentMethod method, Staff seller, CancellationToken ct = default)
    {
        var vouchers = await db.Vouchers.Include(v => v.Tariff).Where(v => voucherIds.Contains(v.Id)).ToListAsync(ct);
        if (vouchers.Count != voucherIds.Distinct().Count())
            throw new BusinessRuleException(ErrorCodes.NotFound, "One or more vouchers were not found.");
        if (vouchers.FirstOrDefault(v => v.Status != VoucherStatus.New) is { } notNew)
            throw new BusinessRuleException(ErrorCodes.VoucherNotSellable, $"Voucher {notNew.Code} is {notNew.Status}, not New.");

        var receipt = await cash.NextReceiptNumberAsync(ct);
        await AddSaleLinesAsync(vouchers, method, seller, receipt, ct);
        audit.Record(seller, "Vouchers.Sold", $"{vouchers.Count} voucher(s), receipt {receipt}");
        await db.SaveChangesAsync(ct);
        return receipt;
    }

    /// <summary>Marks new vouchers (with their tariff loaded) as sold on <paramref name="receipt"/> and adds their transactions (not saved).</summary>
    internal async Task AddSaleLinesAsync(IEnumerable<Voucher> vouchers, PaymentMethod method, Staff seller, string receipt, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var voucher in vouchers)
        {
            voucher.Status = VoucherStatus.Sold;
            voucher.SoldAt = now;
            voucher.SoldByStaffId = seller.Id;
            await cash.AddTransactionAsync(TransactionType.VoucherSale, voucher.Tariff.Price, method, seller, receipt, ct,
                t => { t.VoucherId = voucher.Id; t.Description = voucher.Tariff.Name; });
        }
    }

    public async Task DisableAsync(int voucherId, Staff? actor, CancellationToken ct = default)
    {
        var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == voucherId, ct)
                      ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Voucher {voucherId} not found.");
        voucher.Status = VoucherStatus.Disabled;
        audit.Record(actor, "Voucher.Disabled", voucher.Code);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes vouchers that were never sold or used; others are kept for the reports.</summary>
    public async Task<int> DeleteUnusedAsync(IReadOnlyCollection<int> voucherIds, Staff? actor, CancellationToken ct = default)
    {
        var vouchers = await db.Vouchers.Where(v => voucherIds.Contains(v.Id) && v.Status == VoucherStatus.New).ToListAsync(ct);
        db.Vouchers.RemoveRange(vouchers);
        audit.Record(actor, "Vouchers.Deleted", $"{vouchers.Count} unused voucher(s)");
        await db.SaveChangesAsync(ct);
        return vouchers.Count;
    }
}

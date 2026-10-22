using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Rules;

namespace StonesHotspot.Core.Services;

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

        var now = clock.GetUtcNow().UtcDateTime;
        var receipt = await cash.NextReceiptNumberAsync(ct);
        foreach (var voucher in vouchers)
        {
            voucher.Status = VoucherStatus.Sold;
            voucher.SoldAt = now;
            voucher.SoldByStaffId = seller.Id;
            await cash.AddTransactionAsync(TransactionType.VoucherSale, voucher.Tariff.Price, method, seller, receipt, ct,
                t => { t.VoucherId = voucher.Id; t.Description = voucher.Tariff.Name; });
        }

        audit.Record(seller, "Vouchers.Sold", $"{vouchers.Count} voucher(s), receipt {receipt}");
        await db.SaveChangesAsync(ct);
        return receipt;
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

using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Services;

/// <param name="ExpectedCash">Opening cash + all cash transactions of the shift.</param>
/// <param name="Difference">Counted − expected; negative means cash is missing.</param>
public sealed record ShiftReport(
    Shift Shift,
    Xaf OpeningCash,
    Xaf CashTransactionsTotal,
    Xaf ExpectedCash,
    Xaf? CountedCash,
    Xaf? Difference,
    IReadOnlyDictionary<PaymentMethod, Xaf> TotalsByPaymentMethod,
    IReadOnlyDictionary<TransactionType, Xaf> TotalsByType,
    int TransactionCount);

/// <summary>Shifts and money movements.</summary>
public sealed class CashService(HotspotDbContext db, AuditService audit, ReceiptNumbers receipts, TimeProvider clock)
{
    public Task<Shift?> GetOpenShiftAsync(int staffId, CancellationToken ct = default) =>
        db.Shifts.FirstOrDefaultAsync(s => s.StaffId == staffId && s.ClosedAt == null, ct);

    public Task<List<Transaction>> ListShiftTransactionsAsync(int shiftId, CancellationToken ct = default) =>
        db.Transactions.AsNoTracking().Include(t => t.Voucher).Where(t => t.ShiftId == shiftId)
            .OrderByDescending(t => t.Timestamp).ToListAsync(ct);

    /// <summary>Most recent shifts, of one staff member or of everyone.</summary>
    public Task<List<Shift>> ListShiftsAsync(int? staffId, int take = 20, CancellationToken ct = default) =>
        db.Shifts.AsNoTracking().Include(s => s.Staff).Where(s => staffId == null || s.StaffId == staffId)
            .OrderByDescending(s => s.OpenedAt).Take(take).ToListAsync(ct);

    public Task<Shift?> FindShiftAsync(int shiftId, CancellationToken ct = default) =>
        db.Shifts.AsNoTracking().Include(s => s.Staff).FirstOrDefaultAsync(s => s.Id == shiftId, ct);

    /// <summary>Total of the transactions of one staff member (or everyone) since <paramref name="since"/>, without account charges.</summary>
    public async Task<(Xaf Total, int Count)> SalesSinceAsync(DateTime since, int? staffId, CancellationToken ct = default)
    {
        var amounts = await db.Transactions.AsNoTracking()
            .Where(t => t.Timestamp >= since && t.Type != TransactionType.AccountCharge && (staffId == null || t.StaffId == staffId))
            .Select(t => t.Amount).ToListAsync(ct);
        return (Xaf.Sum(amounts), amounts.Count);
    }

    public async Task<Shift> OpenShiftAsync(Staff staff, Xaf openingCash, CancellationToken ct = default)
    {
        if (openingCash.IsNegative)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Opening cash cannot be negative.");
        if (await GetOpenShiftAsync(staff.Id, ct) is not null)
            throw new BusinessRuleException(ErrorCodes.ShiftAlreadyOpen, $"{staff.Username} already has an open shift.");

        var shift = new Shift { StaffId = staff.Id, OpenedAt = Now, OpeningCash = openingCash };
        db.Shifts.Add(shift);
        audit.Record(staff, "Shift.Opened", $"Opening cash {openingCash}");
        await db.SaveChangesAsync(ct);
        return shift;
    }

    /// <summary>Closes a shift with the cash counted in the drawer. Cashiers can only close their own shift.</summary>
    public async Task<ShiftReport> CloseShiftAsync(int shiftId, Xaf countedCash, Staff actor, string? note = null, CancellationToken ct = default)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct)
                    ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Shift {shiftId} not found.");
        if (actor.Role != StaffRole.Admin && shift.StaffId != actor.Id)
            throw new BusinessRuleException(ErrorCodes.NotAllowed, "Cashiers can only close their own shift.");
        if (!shift.IsOpen)
            throw new BusinessRuleException(ErrorCodes.ShiftClosed, $"Shift {shiftId} is already closed.");
        if (countedCash.IsNegative)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Counted cash cannot be negative.");

        var report = await BuildReportAsync(shift, ct);
        shift.ClosedAt = Now;
        shift.ExpectedClosingCash = report.ExpectedCash;
        shift.CountedClosingCash = countedCash;
        shift.Note = note;

        audit.Record(actor, "Shift.Closed",
            $"Shift {shift.Id}: expected {report.ExpectedCash}, counted {countedCash}, difference {countedCash - report.ExpectedCash}");
        await db.SaveChangesAsync(ct);
        return await BuildReportAsync(shift, ct);
    }

    public async Task<ShiftReport> GetShiftReportAsync(int shiftId, CancellationToken ct = default)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct)
                    ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Shift {shiftId} not found.");
        return await BuildReportAsync(shift, ct);
    }

    /// <summary>
    /// Adds a transaction (not saved). Transactions by a staff member with an open shift are linked to it.
    /// </summary>
    internal async Task<Transaction> AddTransactionAsync(
        TransactionType type, Xaf amount, PaymentMethod method, Staff? staff, string receiptNumber,
        CancellationToken ct, Action<Transaction>? configure = null)
    {
        var shift = staff is null ? null : await GetOpenShiftAsync(staff.Id, ct);
        var transaction = new Transaction
        {
            Type = type,
            Amount = amount,
            PaymentMethod = method,
            Timestamp = Now,
            ReceiptNumber = receiptNumber,
            StaffId = staff?.Id,
            ShiftId = shift?.Id,
        };
        configure?.Invoke(transaction);
        db.Transactions.Add(transaction);
        return transaction;
    }

    internal Task<string> NextReceiptNumberAsync(CancellationToken ct) => receipts.NextAsync(ct);

    private async Task<ShiftReport> BuildReportAsync(Shift shift, CancellationToken ct)
    {
        // Amounts are summed in memory: Xaf is a converted type that SQLite cannot SUM directly.
        var transactions = await db.Transactions.Where(t => t.ShiftId == shift.Id).AsNoTracking().ToListAsync(ct);
        var cash = Xaf.Sum(transactions.Where(t => t.PaymentMethod == PaymentMethod.Cash).Select(t => t.Amount));
        var expected = shift.OpeningCash + cash;

        return new ShiftReport(
            shift,
            shift.OpeningCash,
            cash,
            expected,
            shift.CountedClosingCash,
            shift.CountedClosingCash is { } counted ? counted - expected : null,
            transactions.GroupBy(t => t.PaymentMethod).ToDictionary(g => g.Key, g => Xaf.Sum(g.Select(t => t.Amount))),
            transactions.GroupBy(t => t.Type).ToDictionary(g => g.Key, g => Xaf.Sum(g.Select(t => t.Amount))),
            transactions.Count);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}

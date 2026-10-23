using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Services;

/// <summary>Days from <see cref="From"/> to <see cref="To"/> included, in the PC's local time (Congo: UTC+1).</summary>
public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

public sealed record PlanSalesRow(string Plan, int Count, Xaf Amount);

/// <param name="PeakHours">For each hour of the day (0–23, local): sessions active during that hour, averaged over the days of the range.</param>
public sealed record SessionsReport(
    int Sessions,
    int Devices,
    long BytesUp,
    long BytesDown,
    TimeSpan AverageDuration,
    IReadOnlyDictionary<LoginMethod, int> ByMethod,
    IReadOnlyList<double> PeakHours);

public sealed record ShiftRow(
    Shift Shift, string Staff, Xaf OpeningCash, Xaf CashIn, Xaf ExpectedCash, Xaf? CountedCash, Xaf? Difference, Xaf Total, int Count);

/// <param name="Receipts">Number of distinct receipts.</param>
public sealed record StaffSalesRow(string Staff, int Receipts, IReadOnlyDictionary<PaymentMethod, Xaf> ByMethod, Xaf Total);

/// <param name="Period">First day of the day or month.</param>
public sealed record TurnoverRow(DateOnly Period, IReadOnlyDictionary<TransactionType, Xaf> ByType, Xaf Total, int Count);

public sealed record TransactionFilter(TransactionType? Type = null, PaymentMethod? Method = null, int? StaffId = null);

/// <summary>
/// Business reports over a date range. Turnover and sales per staff leave out account charges (money already
/// counted when the account was topped up). Amounts are summed in memory: SQLite cannot SUM the Xaf type.
/// </summary>
public sealed class ReportService(HotspotDbContext db, TimeProvider clock)
{
    public const int MaxTransactions = 5000;

    public async Task<List<PlanSalesRow>> VoucherSalesByPlanAsync(DateRange range, CancellationToken ct = default)
    {
        var (from, to) = ToUtc(range);
        var sales = await db.Transactions.AsNoTracking()
            .Where(t => t.Type == TransactionType.VoucherSale && t.Timestamp >= from && t.Timestamp < to)
            .Select(t => new { Plan = t.Voucher != null ? t.Voucher.Tariff.Name : t.Description, t.Amount })
            .ToListAsync(ct);
        return sales.GroupBy(s => s.Plan ?? "?")
            .Select(g => new PlanSalesRow(g.Key, g.Count(), Xaf.Sum(g.Select(s => s.Amount))))
            .OrderByDescending(r => r.Amount).ThenBy(r => r.Plan).ToList();
    }

    /// <summary>Sessions started in the range (peak hours also count the part of a session inside the range).</summary>
    public async Task<SessionsReport> SessionsAsync(DateRange range, CancellationToken ct = default)
    {
        var (from, to) = ToUtc(range);
        var now = clock.GetUtcNow().UtcDateTime;
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.StartedAt < to && (s.EndedAt == null || s.EndedAt >= from))
            .Select(s => new { s.DeviceId, s.Method, s.StartedAt, s.EndedAt, s.BytesUp, s.BytesDown })
            .ToListAsync(ct);

        var started = sessions.Where(s => s.StartedAt >= from).ToList();
        var durations = started.Select(s => (s.EndedAt ?? now) - s.StartedAt).ToList();

        var hours = new double[24];
        var zone = clock.LocalTimeZone;
        foreach (var s in sessions)
        {
            var start = s.StartedAt < from ? from : s.StartedAt;
            var end = Min(s.EndedAt ?? now, to);
            // One count per local hour slot the session touches (end excluded: 10:15–11:00 is the 10 h slot only).
            var slot = TruncateToHour(TimeZoneInfo.ConvertTimeFromUtc(start, zone));
            var last = TruncateToHour(TimeZoneInfo.ConvertTimeFromUtc(end > start ? end.AddTicks(-1) : start, zone));
            for (; slot <= last; slot = slot.AddHours(1))
                hours[slot.Hour]++;
        }

        return new SessionsReport(
            started.Count,
            started.Select(s => s.DeviceId).Distinct().Count(),
            started.Sum(s => s.BytesUp),
            started.Sum(s => s.BytesDown),
            durations.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(durations.Average(d => d.TotalSeconds)),
            started.GroupBy(s => s.Method).ToDictionary(g => g.Key, g => g.Count()),
            hours.Select(h => h / range.Days).ToArray());
    }

    /// <summary>Shifts opened in the range, newest first.</summary>
    public async Task<List<ShiftRow>> ShiftsAsync(DateRange range, CancellationToken ct = default)
    {
        var (from, to) = ToUtc(range);
        var shifts = await db.Shifts.AsNoTracking().Include(s => s.Staff)
            .Where(s => s.OpenedAt >= from && s.OpenedAt < to).OrderByDescending(s => s.OpenedAt).ToListAsync(ct);
        var ids = shifts.Select(s => s.Id).ToList();
        var transactions = await db.Transactions.AsNoTracking().Where(t => t.ShiftId != null && ids.Contains(t.ShiftId.Value))
            .Select(t => new { ShiftId = t.ShiftId!.Value, t.Amount, t.PaymentMethod }).ToListAsync(ct);
        var byShift = transactions.ToLookup(t => t.ShiftId);

        return shifts.Select(s =>
        {
            var lines = byShift[s.Id].ToList();
            var cashIn = Xaf.Sum(lines.Where(t => t.PaymentMethod == PaymentMethod.Cash).Select(t => t.Amount));
            var expected = s.OpeningCash + cashIn;
            return new ShiftRow(s, StaffName(s.Staff), s.OpeningCash, cashIn, expected, s.CountedClosingCash,
                s.CountedClosingCash is { } counted ? counted - expected : null, Xaf.Sum(lines.Select(t => t.Amount)), lines.Count);
        }).ToList();
    }

    public async Task<List<StaffSalesRow>> SalesByStaffAsync(DateRange range, CancellationToken ct = default)
    {
        var (from, to) = ToUtc(range);
        var transactions = await db.Transactions.AsNoTracking().Include(t => t.Staff)
            .Where(t => t.Timestamp >= from && t.Timestamp < to && t.Type != TransactionType.AccountCharge)
            .ToListAsync(ct);
        return transactions.GroupBy(t => t.StaffId)
            .Select(g => new StaffSalesRow(
                g.Key is null ? "" : StaffName(g.First().Staff),
                g.Select(t => t.ReceiptNumber).Distinct().Count(),
                g.GroupBy(t => t.PaymentMethod).ToDictionary(m => m.Key, m => Xaf.Sum(m.Select(t => t.Amount))),
                Xaf.Sum(g.Select(t => t.Amount))))
            .OrderByDescending(r => r.Total).ToList();
    }

    /// <summary>Transactions of the range, newest first, at most <see cref="MaxTransactions"/>.</summary>
    public Task<List<Transaction>> TransactionsAsync(DateRange range, TransactionFilter filter, CancellationToken ct = default)
    {
        var (from, to) = ToUtc(range);
        return db.Transactions.AsNoTracking().Include(t => t.Staff).Include(t => t.Voucher)
            .Where(t => t.Timestamp >= from && t.Timestamp < to)
            .Where(t => filter.Type == null || t.Type == filter.Type)
            .Where(t => filter.Method == null || t.PaymentMethod == filter.Method)
            .Where(t => filter.StaffId == null || t.StaffId == filter.StaffId)
            .OrderByDescending(t => t.Timestamp).ThenByDescending(t => t.Id)
            .Take(MaxTransactions).ToListAsync(ct);
    }

    /// <summary>Money taken per local day (or month), by transaction type, oldest first.</summary>
    public async Task<List<TurnoverRow>> TurnoverAsync(DateRange range, bool byMonth, CancellationToken ct = default)
    {
        var (from, to) = ToUtc(range);
        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.Timestamp >= from && t.Timestamp < to && t.Type != TransactionType.AccountCharge)
            .Select(t => new { t.Timestamp, t.Type, t.Amount }).ToListAsync(ct);
        var zone = clock.LocalTimeZone;
        return transactions
            .GroupBy(t =>
            {
                var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(t.Timestamp, zone));
                return byMonth ? new DateOnly(day.Year, day.Month, 1) : day;
            })
            .OrderBy(g => g.Key)
            .Select(g => new TurnoverRow(g.Key,
                g.GroupBy(t => t.Type).ToDictionary(x => x.Key, x => Xaf.Sum(x.Select(t => t.Amount))),
                Xaf.Sum(g.Select(t => t.Amount)), g.Count()))
            .ToList();
    }

    /// <summary>The local day today, for default date ranges.</summary>
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, clock.LocalTimeZone));

    /// <summary>UTC bounds [from, to) of the local days of <paramref name="range"/>.</summary>
    public (DateTime From, DateTime To) ToUtc(DateRange range)
    {
        var zone = clock.LocalTimeZone;
        return (ToUtc(range.From, zone), ToUtc(range.To.AddDays(1), zone));
    }

    private static DateTime ToUtc(DateOnly day, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zone);

    private static DateTime TruncateToHour(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind);
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static string StaffName(Staff? staff) => staff?.DisplayName ?? staff?.Username ?? "";
}

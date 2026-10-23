using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

/// <summary>
/// Records who did what. Entries are added to the current unit of work and saved together with the change
/// they describe, so a change is never saved without its audit entry.
/// </summary>
public sealed class AuditService(HotspotDbContext db, TimeProvider clock)
{
    public const string SystemActor = "system";

    public void Record(Staff? actor, string action, string? details = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = clock.GetUtcNow().UtcDateTime,
            StaffId = actor?.Id,
            Actor = actor?.Username ?? SystemActor,
            Action = action,
            Details = details,
        });

    public const int MaxQueryRows = 2000;

    /// <summary>
    /// Entries between <paramref name="fromUtc"/> (included) and <paramref name="toUtc"/> (excluded), newest first, at most
    /// <see cref="MaxQueryRows"/>. <paramref name="text"/> is searched in the action and the details, ignoring case.
    /// </summary>
    public async Task<List<AuditLog>> QueryAsync(DateTime fromUtc, DateTime toUtc, int? staffId, string? text, CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking()
            .Where(a => a.Timestamp >= fromUtc && a.Timestamp < toUtc && (staffId == null || a.StaffId == staffId));
        if (string.IsNullOrWhiteSpace(text))
            return await query.OrderByDescending(a => a.Id).Take(MaxQueryRows).ToListAsync(ct);

        // Filtered in memory: SQLite's LIKE only ignores the case of ASCII letters ("Opéré" vs "opéré").
        var needle = text.Trim();
        var found = new List<AuditLog>();
        await foreach (var entry in query.OrderByDescending(a => a.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            if (entry.Action.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || entry.Actor.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || entry.Details?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true)
            {
                found.Add(entry);
                if (found.Count == MaxQueryRows)
                    break;
            }
        }
        return found;
    }
}

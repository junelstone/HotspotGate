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
}

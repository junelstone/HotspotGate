using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

public sealed class TariffService(HotspotDbContext db, AuditService audit, TimeProvider clock)
{
    public const int MaxDevicesLimit = 50;

    public Task<List<Tariff>> ListAsync(bool includeInactive = false, CancellationToken ct = default) =>
        db.Tariffs.Where(t => includeInactive || t.IsActive).OrderBy(t => t.Price).ThenBy(t => t.Name).ToListAsync(ct);

    public Task<Tariff?> FindAsync(int id, CancellationToken ct = default) => db.Tariffs.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Tariff> CreateAsync(Tariff tariff, Staff? actor, CancellationToken ct = default)
    {
        tariff.Name = tariff.Name.Trim();
        await ValidateAsync(tariff, ct);
        tariff.CreatedAt = clock.GetUtcNow().UtcDateTime;
        db.Tariffs.Add(tariff);
        audit.Record(actor, "Tariff.Created", $"{tariff.Name} ({tariff.Price})");
        await db.SaveChangesAsync(ct);
        return tariff;
    }

    /// <summary>Copies the editable fields of <paramref name="changes"/> onto tariff <paramref name="id"/>.</summary>
    public async Task<Tariff> UpdateAsync(int id, Tariff changes, Staff? actor, CancellationToken ct = default)
    {
        var tariff = await FindAsync(id, ct) ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Tariff {id} not found.");
        changes.Id = id;
        changes.Name = changes.Name.Trim();
        await ValidateAsync(changes, ct);

        tariff.Name = changes.Name;
        tariff.Price = changes.Price;
        tariff.Type = changes.Type;
        tariff.Quota = changes.Quota;
        tariff.ValidityHours = changes.ValidityHours;
        tariff.ValidUntil = changes.ValidUntil;
        tariff.CountdownMode = changes.CountdownMode;
        tariff.DownloadKbps = changes.DownloadKbps;
        tariff.UploadKbps = changes.UploadKbps;
        tariff.MaxDevices = changes.MaxDevices;
        tariff.IdleTimeoutMinutes = changes.IdleTimeoutMinutes;
        tariff.IsActive = changes.IsActive;

        audit.Record(actor, "Tariff.Updated", tariff.Name);
        await db.SaveChangesAsync(ct);
        return tariff;
    }

    /// <summary>Deletes a tariff that was never used; otherwise refuses (deactivate it instead).</summary>
    public async Task DeleteAsync(int id, Staff? actor, CancellationToken ct = default)
    {
        var tariff = await FindAsync(id, ct) ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Tariff {id} not found.");
        var used = await db.Vouchers.AnyAsync(v => v.TariffId == id, ct)
                   || await db.UserAccounts.AnyAsync(a => a.TariffId == id, ct)
                   || await db.Sessions.AnyAsync(s => s.TariffId == id, ct);
        if (used)
            throw new BusinessRuleException(ErrorCodes.TariffInUse, $"Tariff '{tariff.Name}' is in use; deactivate it instead.");

        db.Tariffs.Remove(tariff);
        audit.Record(actor, "Tariff.Deleted", tariff.Name);
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidateAsync(Tariff t, CancellationToken ct)
    {
        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new BusinessRuleException(ErrorCodes.Invalid, message);
        }

        Require(t.Name.Length is > 0 and <= 100, "Name is required (max 100 characters).");
        Require(!t.Price.IsNegative, "Price cannot be negative.");
        Require(t.Type == TariffType.Unlimited || t.Quota > 0, "Quota must be greater than zero.");
        Require(t.ValidityHours is null or > 0, "Validity must be greater than zero.");
        Require(t.DownloadKbps is null or > 0 && t.UploadKbps is null or > 0, "Speed limits must be greater than zero.");
        Require(t.MaxDevices is >= 1 and <= MaxDevicesLimit, $"Max devices must be between 1 and {MaxDevicesLimit}.");
        Require(t.IdleTimeoutMinutes is null or > 0, "Idle timeout must be greater than zero.");
        Require(t.Type != TariffType.Unlimited || t.ValidityHours is not null || t.ValidUntil is not null,
            "An unlimited tariff needs a validity period or an end date.");

        if (await db.Tariffs.AnyAsync(x => x.Id != t.Id && x.IsActive && x.Name == t.Name, ct))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"A tariff named '{t.Name}' already exists.");
    }
}

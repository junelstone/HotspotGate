using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Security;

namespace StonesHotspot.Core.Services;

public sealed class StaffService(HotspotDbContext db, IPasswordHasher hasher, AuditService audit, TimeProvider clock)
{
    public const int MinPasswordLength = 8;

    public Task<Staff?> FindAsync(int id, CancellationToken ct = default) => db.Staff.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<Staff>> ListAsync(CancellationToken ct = default) => db.Staff.OrderBy(s => s.Username).ToListAsync(ct);

    public async Task<Staff> CreateAsync(string username, string password, StaffRole role, string? displayName, Staff? actor, CancellationToken ct = default)
    {
        username = username.Trim();
        if (username.Length is 0 or > 64)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Username is required (max 64 characters).");
        CheckPassword(password);
        if (await db.Staff.AnyAsync(s => s.Username == username, ct))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Username '{username}' is taken.");

        var staff = new Staff
        {
            Username = username,
            DisplayName = displayName,
            Role = role,
            PasswordHash = hasher.Hash(password),
            MustChangePassword = true,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Staff.Add(staff);
        audit.Record(actor, "Staff.Created", $"{username} ({role})");
        await db.SaveChangesAsync(ct);
        return staff;
    }

    /// <summary>Enables or disables a staff account. The last enabled admin cannot be disabled, nor can you disable yourself.</summary>
    public async Task SetEnabledAsync(int staffId, bool enabled, Staff actor, CancellationToken ct = default)
    {
        var staff = await FindAsync(staffId, ct) ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Staff {staffId} not found.");
        if (!enabled)
        {
            if (staff.Id == actor.Id)
                throw new BusinessRuleException(ErrorCodes.NotAllowed, "You cannot disable your own account.");
            if (staff.Role == StaffRole.Admin && !await db.Staff.AnyAsync(s => s.Id != staffId && s.Role == StaffRole.Admin && s.Enabled, ct))
                throw new BusinessRuleException(ErrorCodes.NotAllowed, "The last administrator cannot be disabled.");
        }
        staff.Enabled = enabled;
        audit.Record(actor, enabled ? "Staff.Enabled" : "Staff.Disabled", staff.Username);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Returns the staff member if the username/password are right and the account is enabled.</summary>
    public async Task<Staff?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        var staff = await db.Staff.FirstOrDefaultAsync(s => s.Username == username.Trim(), ct);
        if (staff is null || !staff.Enabled || !hasher.Verify(password, staff.PasswordHash))
            return null;

        staff.LastLoginAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return staff;
    }

    public async Task ChangePasswordAsync(int staffId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var staff = await FindAsync(staffId, ct) ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Staff {staffId} not found.");
        if (!hasher.Verify(currentPassword, staff.PasswordHash))
            throw new BusinessRuleException(ErrorCodes.WrongPassword, "Current password is wrong.");
        CheckPassword(newPassword);
        if (newPassword == currentPassword)
            throw new BusinessRuleException(ErrorCodes.WeakPassword, "The new password must be different.");

        staff.PasswordHash = hasher.Hash(newPassword);
        staff.MustChangePassword = false;
        audit.Record(staff, "Staff.PasswordChanged", staff.Username);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Admin sets a temporary password; the staff member must change it at next login.</summary>
    public async Task ResetPasswordAsync(int staffId, string temporaryPassword, Staff actor, CancellationToken ct = default)
    {
        var staff = await FindAsync(staffId, ct) ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Staff {staffId} not found.");
        CheckPassword(temporaryPassword);
        staff.PasswordHash = hasher.Hash(temporaryPassword);
        staff.MustChangePassword = true;
        audit.Record(actor, "Staff.PasswordReset", staff.Username);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Last resort when no administrator can sign in (run on the PC by an Administrator of Windows): the <c>admin</c>
    /// account gets a new random temporary password and is enabled again. Returns that password.
    /// </summary>
    public async Task<string> ResetAdminOfflineAsync(CancellationToken ct = default)
    {
        var admin = await db.Staff.FirstOrDefaultAsync(s => s.Username == DatabaseInitializer.AdminUsername, ct)
                    ?? throw new BusinessRuleException(ErrorCodes.NotFound, "Admin account not found.");
        var password = PasswordGenerator.Generate();
        admin.PasswordHash = hasher.Hash(password);
        admin.MustChangePassword = true;
        admin.Enabled = true;
        admin.Role = StaffRole.Admin;
        audit.Record(null, "Staff.PasswordReset", $"{admin.Username} (offline reset on the PC)");
        await db.SaveChangesAsync(ct);
        return password;
    }

    private static void CheckPassword(string password)
    {
        if (password.Length < MinPasswordLength)
            throw new BusinessRuleException(ErrorCodes.WeakPassword, $"Password must have at least {MinPasswordLength} characters.");
    }
}

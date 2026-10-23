using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Security;

namespace StonesHotspot.Core.Services;

/// <summary>Whether the first-run wizard is finished, cached for the request filter (one value per installation).</summary>
public sealed class SetupState
{
    private volatile int _completed = -1; // -1 unknown, 0 no, 1 yes

    public bool? Completed => _completed < 0 ? null : _completed == 1;

    internal void Set(bool completed) => _completed = completed ? 1 : 0;
}

/// <summary>
/// The first-run wizard's steps: language, business and Wi-Fi name, administrator password, first plan prices, done.
/// (The network step uses the network setup service directly.) A new database starts "not completed"; a database
/// from before the wizard existed counts as completed.
/// </summary>
public sealed class SetupService(
    HotspotDbContext db, SettingsService settings, IPasswordHasher hasher, AuditService audit, SetupState state, AppPaths paths)
{
    public async Task<bool> IsCompletedAsync(CancellationToken ct = default)
    {
        if (state.Completed is { } known)
            return known;
        var completed = await settings.GetBoolAsync(SettingKeys.SetupCompleted, ct);
        state.Set(completed);
        return completed;
    }

    public async Task SaveLanguageAsync(string language, CancellationToken ct = default)
    {
        await EnsureNotCompletedAsync(ct);
        await settings.SetAsync(SettingKeys.DefaultLanguage, language == "en" ? "en" : "fr", null, ct);
    }

    public async Task SaveBusinessAsync(string businessName, string? wifiName, CancellationToken ct = default)
    {
        await EnsureNotCompletedAsync(ct);
        businessName = businessName.Trim();
        if (businessName.Length is 0 or > 80)
            throw new BusinessRuleException(ErrorCodes.Invalid, "The business name must have 1 to 80 characters.");
        await settings.SetAsync(SettingKeys.BusinessName, businessName, null, ct);
        await settings.SetAsync(SettingKeys.WifiName, (wifiName ?? "").Trim(), null, ct);
    }

    /// <summary>Replaces the random first password of the <c>admin</c> account; the paper/file copy is deleted.</summary>
    public async Task SetAdminPasswordAsync(string password, string confirmation, CancellationToken ct = default)
    {
        await EnsureNotCompletedAsync(ct);
        if (password != confirmation)
            throw new BusinessRuleException(ErrorCodes.Invalid, "The two passwords are different.");
        if (password.Length < StaffService.MinPasswordLength)
            throw new BusinessRuleException(ErrorCodes.WeakPassword, $"Password must have at least {StaffService.MinPasswordLength} characters.");
        var admin = await db.Staff.FirstOrDefaultAsync(s => s.Username == DatabaseInitializer.AdminUsername, ct)
                    ?? throw new BusinessRuleException(ErrorCodes.NotFound, "Admin account not found.");
        admin.PasswordHash = hasher.Hash(password);
        admin.MustChangePassword = false;
        admin.Enabled = true;
        audit.Record(admin, "Setup.AdminPassword", admin.Username);
        await db.SaveChangesAsync(ct);
        File.Delete(Path.Combine(paths.Root, AppPaths.InitialAdminPasswordFileName));
    }

    public Task<List<Tariff>> TariffsAsync(CancellationToken ct = default) =>
        db.Tariffs.Where(t => t.IsActive).OrderBy(t => t.Id).ToListAsync(ct);

    /// <summary>New prices for the starting plans (FCFA, whole numbers); plans not listed keep their price.</summary>
    public async Task SavePricesAsync(IReadOnlyDictionary<int, long> prices, CancellationToken ct = default)
    {
        await EnsureNotCompletedAsync(ct);
        if (prices.Values.Any(p => p is < 0 or > 10_000_000))
            throw new BusinessRuleException(ErrorCodes.Invalid, "A price must be between 0 and 10 000 000 FCFA.");
        foreach (var tariff in await db.Tariffs.Where(t => prices.Keys.Contains(t.Id)).ToListAsync(ct))
        {
            var price = new Xaf(prices[tariff.Id]);
            if (tariff.Price == price)
                continue;
            audit.Record(null, "Setup.TariffPrice", $"{tariff.Name}: {tariff.Price.Amount} → {price.Amount}");
            tariff.Price = price;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ends the wizard. Needs the administrator password to have been chosen.</summary>
    public async Task CompleteAsync(CancellationToken ct = default)
    {
        if (await IsCompletedAsync(ct))
            return;
        if (await db.Staff.AnyAsync(s => s.Username == DatabaseInitializer.AdminUsername && s.MustChangePassword, ct))
            throw new BusinessRuleException(ErrorCodes.NotAllowed, "Choose the administrator password first.");
        await settings.SetAsync(SettingKeys.SetupCompleted, "true", null, ct);
        state.Set(true);
    }

    private async Task EnsureNotCompletedAsync(CancellationToken ct)
    {
        if (await IsCompletedAsync(ct))
            throw new BusinessRuleException(ErrorCodes.NotAllowed, "The first-run setup is already completed.");
    }
}

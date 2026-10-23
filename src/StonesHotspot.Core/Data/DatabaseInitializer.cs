using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Core.Data;

/// <param name="InitialAdminPassword">
/// Set only when the admin account was just created: the random password to hand to the owner once.
/// </param>
public sealed record DatabaseInitResult(string? InitialAdminPassword);

/// <summary>Applies migrations and adds first-run data. Safe to run at every start.</summary>
public sealed class DatabaseInitializer(HotspotDbContext db, IPasswordHasher hasher, SettingsService settings, AuditService audit, TimeProvider clock)
{
    public const string AdminUsername = "admin";

    public async Task<DatabaseInitResult> InitializeAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        // WAL lets the dashboard read while the session engine writes every second.
        if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);

        var now = clock.GetUtcNow().UtcDateTime;
        string? adminPassword = null;

        if (!await db.Staff.AnyAsync(ct))
        {
            adminPassword = PasswordGenerator.Generate();
            db.Staff.Add(new Staff
            {
                Username = AdminUsername,
                DisplayName = "Administrateur",
                Role = StaffRole.Admin,
                PasswordHash = hasher.Hash(adminPassword),
                MustChangePassword = true,
                CreatedAt = now,
            });
            audit.Record(null, "Database.Seeded", "Admin account created");
            db.Settings.Add(new Setting { Key = SettingKeys.SetupCompleted, Value = "false" }); // new installation: first-run wizard
        }
        else if (!await db.Settings.AnyAsync(s => s.Key == SettingKeys.SetupCompleted, ct))
        {
            db.Settings.Add(new Setting { Key = SettingKeys.SetupCompleted, Value = "true" }); // in use before the wizard existed
        }

        if (!await db.Tariffs.AnyAsync(ct))
            db.Tariffs.AddRange(SampleTariffs(now));

        await settings.AddMissingDefaultsAsync(ct);
        await db.SaveChangesAsync(ct);
        return new DatabaseInitResult(adminPassword);
    }

    /// <summary>Starting tariffs; prices and limits are meant to be edited in the dashboard.</summary>
    public static IEnumerable<Tariff> SampleTariffs(DateTime now) =>
    [
        new()
        {
            Name = "30 minutes", Price = new Xaf(100), Type = TariffType.TimeBased, Quota = 30,
            CountdownMode = CountdownMode.UsageOnly, ValidityHours = 7 * 24,
            DownloadKbps = 1024, UploadKbps = 512, MaxDevices = 1, CreatedAt = now,
        },
        new()
        {
            Name = "1 heure", Price = new Xaf(200), Type = TariffType.TimeBased, Quota = 60,
            CountdownMode = CountdownMode.UsageOnly, ValidityHours = 7 * 24,
            DownloadKbps = 1024, UploadKbps = 512, MaxDevices = 1, CreatedAt = now,
        },
        new()
        {
            Name = "Pass 24 heures", Price = new Xaf(1000), Type = TariffType.Unlimited, Quota = 0,
            CountdownMode = CountdownMode.WallClock, ValidityHours = 24,
            DownloadKbps = 2048, UploadKbps = 1024, MaxDevices = 2, CreatedAt = now,
        },
    ];
}

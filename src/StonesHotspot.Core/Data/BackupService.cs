using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Core.Data;

public sealed record BackupFile(string Name, long Size, DateTime CreatedAt);

/// <summary>
/// Copies of the database in <c>%ProgramData%\StonesHotspot\backups</c>. Uses SQLite's online backup, so the
/// service keeps running while a copy is made or restored.
/// </summary>
public sealed class BackupService(HotspotDbContext db, AppPaths paths, AuditService audit, TimeProvider clock)
{
    public const string Prefix = "stoneshotspot-";
    public const string Extension = ".db";
    public const string AutomaticSuffix = "-auto";
    public const int KeepAutomatic = 14;

    public IReadOnlyList<BackupFile> List() =>
        Directory.Exists(paths.Backups)
            ? new DirectoryInfo(paths.Backups).GetFiles($"{Prefix}*{Extension}")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => new BackupFile(f.Name, f.Length, f.CreationTimeUtc))
                .ToList()
            : [];

    /// <summary>Full path of a backup by its file name; null for anything that is not one of our backups.</summary>
    public string? PathOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)
            || !name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(Extension, StringComparison.Ordinal))
            return null;
        var path = Path.Combine(paths.Backups, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Makes a consistent copy of the live database and returns its file name.</summary>
    public async Task<string> CreateAsync(Staff? actor, string? suffix = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.Backups);
        var stamp = clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var name = $"{Prefix}{stamp}{suffix}{Extension}";
        var target = Path.Combine(paths.Backups, name);
        for (var i = 2; File.Exists(target); i++)
        {
            name = $"{Prefix}{stamp}{suffix}-{i}{Extension}";
            target = Path.Combine(paths.Backups, name);
        }

        audit.Record(actor, "Database.Backup", name);
        await db.SaveChangesAsync(ct); // the audit entry is part of the copy

        var live = (SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var destination = new SqliteConnection($"Data Source={target};Pooling=False");
            await destination.OpenAsync(ct);
            live.BackupDatabase(destination);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
        return name;
    }

    /// <summary>
    /// The daily automatic backup: skipped when the latest automatic one is less than 20 hours old (service restarts),
    /// then only the <see cref="KeepAutomatic"/> latest automatic backups are kept. Manual backups are never deleted.
    /// </summary>
    /// <returns>The new backup's name, or null when none was due.</returns>
    public async Task<string?> CreateAutomaticAsync(CancellationToken ct = default)
    {
        var automatic = AutomaticFiles();
        if (automatic.FirstOrDefault() is { } latest && clock.GetUtcNow().UtcDateTime - Stamp(latest) < TimeSpan.FromHours(20))
            return null;
        var name = await CreateAsync(null, AutomaticSuffix, ct);
        foreach (var old in AutomaticFiles().Skip(KeepAutomatic))
            File.Delete(Path.Combine(paths.Backups, old));
        return name;
    }

    /// <summary>Automatic backups, newest first (by the time in their name).</summary>
    private List<string> AutomaticFiles() =>
        Directory.Exists(paths.Backups)
            ? Directory.GetFiles(paths.Backups, $"{Prefix}*{AutomaticSuffix}{Extension}").Select(Path.GetFileName).OfType<string>()
                .OrderByDescending(n => n, StringComparer.Ordinal).ToList()
            : [];

    private static DateTime Stamp(string name) =>
        DateTime.TryParseExact(name.AsSpan(Prefix.Length, 15), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var stamp) ? stamp : DateTime.MinValue;

    /// <summary>
    /// Replaces the database with an uploaded backup. The file is checked first and the current database is
    /// backed up (file name returned) before anything is overwritten.
    /// </summary>
    public async Task<string> RestoreAsync(Stream upload, Staff actor, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.Backups);
        var uploaded = Path.Combine(paths.Backups, $"upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = File.Create(uploaded))
                await upload.CopyToAsync(file, ct);

            await using (var check = new SqliteConnection($"Data Source={uploaded};Mode=ReadOnly;Pooling=False"))
            {
                try
                {
                    await check.OpenAsync(ct);
                    if (!await IsHotspotDatabaseAsync(check, ct))
                        throw new BusinessRuleException(ErrorCodes.Invalid, "This file is not a Stone's Hotspot database.");
                }
                catch (SqliteException ex)
                {
                    throw new BusinessRuleException(ErrorCodes.Invalid, $"This file is not a readable database ({ex.SqliteErrorCode}).");
                }
            }

            var safety = await CreateAsync(actor, "-before-restore", ct);

            var live = (SqliteConnection)db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var source = new SqliteConnection($"Data Source={uploaded};Mode=ReadOnly;Pooling=False");
                await source.OpenAsync(ct);
                source.BackupDatabase(live);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }

            db.ChangeTracker.Clear();
            await db.Database.MigrateAsync(ct); // an older backup gets the newer tables
            audit.Record(actor, "Database.Restored", $"Previous database saved as {safety}");
            await db.SaveChangesAsync(ct);
            return safety;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(uploaded);
        }
    }

    private static async Task<bool> IsHotspotDatabaseAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA quick_check;";
        if (await integrity.ExecuteScalarAsync(ct) as string != "ok")
            return false;

        await using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('__EFMigrationsHistory','Vouchers','Tariffs','Staff','Sessions')";
        return Convert.ToInt32(await tables.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 5;
    }
}

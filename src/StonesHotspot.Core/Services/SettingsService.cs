using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

public static class SettingKeys
{
    public const string BusinessName = "Business.Name";
    public const string DefaultLanguage = "Ui.DefaultLanguage";
    public const string DefaultIdleTimeoutMinutes = "Sessions.DefaultIdleTimeoutMinutes";
    public const string LastReceiptNumber = "Receipts.LastNumber";

    /// <summary>Values written on first start; existing values are never overwritten.</summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>
    {
        [BusinessName] = "Stone's Hotspot",
        [DefaultLanguage] = "fr",
        [DefaultIdleTimeoutMinutes] = "10",
    };
}

/// <summary>Key/value application settings stored in the database.</summary>
public sealed class SettingsService(HotspotDbContext db, AuditService audit)
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        (await db.Settings.FindAsync([key], ct))?.Value ?? SettingKeys.Defaults.GetValueOrDefault(key);

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default) =>
        int.TryParse(await GetAsync(key, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public async Task SetAsync(string key, string value, Staff? actor, CancellationToken ct = default)
    {
        var setting = await db.Settings.FindAsync([key], ct);
        var old = setting?.Value;
        if (setting is null)
            db.Settings.Add(new Setting { Key = key, Value = value });
        else
            setting.Value = value;

        audit.Record(actor, "Setting.Changed", $"{key}: '{old}' → '{value}'");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds missing default settings (does not save).</summary>
    internal async Task AddMissingDefaultsAsync(CancellationToken ct)
    {
        var existing = await db.Settings.Select(s => s.Key).ToListAsync(ct);
        foreach (var (key, value) in SettingKeys.Defaults.Where(d => !existing.Contains(d.Key)))
            db.Settings.Add(new Setting { Key = key, Value = value });
    }
}

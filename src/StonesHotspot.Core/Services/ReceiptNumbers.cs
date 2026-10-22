using System.Globalization;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

/// <summary>Sequential receipt numbers per year: 2026-000001, 2026-000002 … (restart at 1 each January).</summary>
public sealed class ReceiptNumbers(HotspotDbContext db, TimeProvider clock)
{
    /// <summary>Reserves the next number. Saved with the caller's SaveChanges.</summary>
    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var year = clock.GetUtcNow().Year;
        var setting = await db.Settings.FindAsync([SettingKeys.LastReceiptNumber], ct);
        if (setting is null)
        {
            setting = new Setting { Key = SettingKeys.LastReceiptNumber };
            db.Settings.Add(setting);
        }

        var next = 1;
        var parts = setting.Value.Split('-');
        if (parts.Length == 2 && parts[0] == year.ToString(CultureInfo.InvariantCulture)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var last))
            next = last + 1;

        setting.Value = string.Create(CultureInfo.InvariantCulture, $"{year}-{next:D6}");
        return setting.Value;
    }
}

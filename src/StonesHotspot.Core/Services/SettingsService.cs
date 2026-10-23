using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

public static class SettingKeys
{
    public const string BusinessName = "Business.Name";
    public const string WifiName = "Business.WifiName";
    public const string DefaultLanguage = "Ui.DefaultLanguage";
    public const string DefaultIdleTimeoutMinutes = "Sessions.DefaultIdleTimeoutMinutes";
    public const string LastReceiptNumber = "Receipts.LastNumber";
    public const string PortalWelcomeUrl = "Portal.WelcomeUrl";
    public const string PortalAccentColor = "Portal.AccentColor";
    public const string PortalTermsFr = "Portal.TermsFr";
    public const string PortalTermsEn = "Portal.TermsEn";
    public const string FairShareEnabled = "Bandwidth.FairShare";
    public const string FairShareTotalDownKbps = "Bandwidth.TotalDownKbps";
    public const string FairShareTotalUpKbps = "Bandwidth.TotalUpKbps";
    public const string FairShareMinKbps = "Bandwidth.MinKbps";
    public const string FairShareMaxKbps = "Bandwidth.MaxKbps";
    public const string FilterFamilyDns = "Filter.FamilyDns";
    public const string NetworkMode = "Network.Mode";
    public const string NetworkWanAdapterId = "Network.WanAdapterId";
    public const string NetworkLanAdapterId = "Network.LanAdapterId";
    public const string PrinterConnection = "Printer.Connection";
    public const string PrinterPaperWidth = "Printer.PaperWidthMm";
    public const string PrinterCharset = "Printer.Charset";
    public const string PrinterQr = "Printer.Qr";
    public const string PrinterAutoSale = "Printer.AutoPrintSale";
    public const string PrinterAutoShift = "Printer.AutoPrintShift";
    public const string ReceiptHeader = "Receipts.Header";
    public const string ReceiptFooter = "Receipts.Footer";

    /// <summary>Values written on first start; existing values are never overwritten.</summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>
    {
        [BusinessName] = "Stone's Hotspot",
        [DefaultLanguage] = "fr",
        [DefaultIdleTimeoutMinutes] = "10",
        [FairShareEnabled] = "false",
        [FairShareMinKbps] = "256",
        [FairShareMaxKbps] = "4096",
        [FilterFamilyDns] = "false",
        [PrinterPaperWidth] = "80",
        [PrinterCharset] = "pc850",
        [PrinterQr] = "true",
        [PrinterAutoSale] = "false",
        [PrinterAutoShift] = "false",
    };
}

/// <summary>Key/value application settings stored in the database.</summary>
public sealed class SettingsService(HotspotDbContext db, AuditService audit)
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        (await db.Settings.FindAsync([key], ct))?.Value ?? SettingKeys.Defaults.GetValueOrDefault(key);

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default) =>
        int.TryParse(await GetAsync(key, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default) =>
        bool.TryParse(await GetAsync(key, ct), out var value) && value;

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

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

/// <summary>Blocked and free-access (walled garden) domains and IP ranges.</summary>
public sealed partial class WebFilterService(HotspotDbContext db, AuditService audit, TimeProvider clock)
{
    public Task<List<WebFilterRule>> ListAsync(CancellationToken ct = default) =>
        db.WebFilterRules.OrderBy(r => r.Kind).ThenBy(r => r.Pattern).ToListAsync(ct);

    public Task<List<WebFilterRule>> ListEnabledAsync(CancellationToken ct = default) =>
        db.WebFilterRules.AsNoTracking().Where(r => r.Enabled).ToListAsync(ct);

    public async Task<WebFilterRule> AddAsync(string pattern, WebFilterKind kind, string? category, Staff? actor, CancellationToken ct = default)
    {
        var normalized = Normalize(pattern)
                         ?? throw new BusinessRuleException(ErrorCodes.Invalid, $"'{pattern}' is neither a domain nor an IPv4 address/range.");
        if (await db.WebFilterRules.AnyAsync(r => r.Kind == kind && r.Pattern == normalized, ct))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"'{normalized}' is already in the list.");

        var rule = new WebFilterRule
        {
            Pattern = normalized,
            Kind = kind,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.WebFilterRules.Add(rule);
        audit.Record(actor, "WebFilter.Added", $"{kind}: {normalized}");
        await db.SaveChangesAsync(ct);
        return rule;
    }

    /// <summary>Adds a ready-made list (see <see cref="FilterLists"/>) as blocked rules; entries already present are skipped.</summary>
    public async Task<int> AddListAsync(string list, Staff? actor, CancellationToken ct = default)
    {
        var domains = FilterLists.Domains(list) ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Unknown list '{list}'.");
        var existing = (await db.WebFilterRules.Where(r => r.Kind == WebFilterKind.Block).Select(r => r.Pattern).ToListAsync(ct)).ToHashSet();
        var now = clock.GetUtcNow().UtcDateTime;
        var added = 0;
        foreach (var domain in domains.Where(d => !existing.Contains(d)))
        {
            db.WebFilterRules.Add(new WebFilterRule { Pattern = domain, Kind = WebFilterKind.Block, Category = list, CreatedAt = now });
            added++;
        }
        audit.Record(actor, "WebFilter.ListAdded", $"{list}: {added} domain(s)");
        await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>Removes every rule of a ready-made list.</summary>
    public async Task<int> RemoveListAsync(string list, Staff? actor, CancellationToken ct = default)
    {
        var rules = await db.WebFilterRules.Where(r => r.Category == list).ToListAsync(ct);
        db.WebFilterRules.RemoveRange(rules);
        audit.Record(actor, "WebFilter.ListRemoved", $"{list}: {rules.Count} rule(s)");
        await db.SaveChangesAsync(ct);
        return rules.Count;
    }

    public async Task RemoveAsync(int id, Staff? actor, CancellationToken ct = default)
    {
        var rule = await db.WebFilterRules.FirstOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Rule {id} not found.");
        db.WebFilterRules.Remove(rule);
        audit.Record(actor, "WebFilter.Removed", $"{rule.Kind}: {rule.Pattern}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// "Facebook.com.", "*.facebook.com", "https://www.facebook.com/page" → "facebook.com" / "www.facebook.com";
    /// "1.2.3.4", "10.0.0.0/8" kept as is. Null when invalid.
    /// </summary>
    public static string? Normalize(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;
        var p = pattern.Trim().ToLowerInvariant();

        var parts = p.Split('/');
        if (parts.Length <= 2 && IPAddress.TryParse(parts[0], out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            if (parts.Length == 1)
                return ip.ToString();
            return int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) && prefix is >= 1 and <= 32
                ? $"{ip}/{prefix}"
                : null;
        }

        if (p.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(p, UriKind.Absolute, out var uri))
            p = uri.Host;
        p = p.TrimEnd('.');
        if (p.StartsWith("*.", StringComparison.Ordinal))
            p = p[2..];
        return DomainRegex().IsMatch(p) ? p : null;
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,61}[a-z0-9]$")]
    private static partial Regex DomainRegex();
}

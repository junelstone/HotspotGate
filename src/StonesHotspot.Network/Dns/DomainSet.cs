namespace StonesHotspot.Network.Dns;

/// <summary>
/// Domains matched with their subdomains: "facebook.com" matches "facebook.com" and "m.facebook.com", not
/// "notfacebook.com".
/// </summary>
public sealed class DomainSet
{
    private readonly HashSet<string> _domains;

    public static DomainSet Empty { get; } = new([]);

    public DomainSet(IEnumerable<string> domains) =>
        _domains = domains.Select(Normalize).Where(d => d.Length > 0).ToHashSet(StringComparer.Ordinal);

    public int Count => _domains.Count;

    public bool Matches(string name)
    {
        if (_domains.Count == 0)
            return false;
        var candidate = Normalize(name);
        while (candidate.Length > 0)
        {
            if (_domains.Contains(candidate))
                return true;
            var dot = candidate.IndexOf('.');
            if (dot < 0)
                return false;
            candidate = candidate[(dot + 1)..];
        }
        return false;
    }

    public static string Normalize(string name)
    {
        var n = name.Trim().TrimEnd('.').ToLowerInvariant();
        return n.StartsWith("*.", StringComparison.Ordinal) ? n[2..] : n;
    }
}

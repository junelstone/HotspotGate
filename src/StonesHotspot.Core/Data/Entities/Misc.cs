namespace StonesHotspot.Core.Data.Entities;

public class WebFilterRule
{
    public int Id { get; set; }

    /// <summary>Domain (<c>facebook.com</c> also matches subdomains) or IPv4 address/range (<c>1.2.3.0/24</c>).</summary>
    public string Pattern { get; set; } = "";

    public WebFilterKind Kind { get; set; }
    public string? Category { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int? StaffId { get; set; }

    /// <summary>Username at the time (kept even if the staff account is deleted later), or "system".</summary>
    public string Actor { get; set; } = "";

    public string Action { get; set; } = "";
    public string? Details { get; set; }
}

using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Data.Entities;

public class UserAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }

    public Xaf Balance { get; set; }

    /// <summary>How far below zero the balance may go when buying a package.</summary>
    public Xaf AllowedOverdraft { get; set; }

    public int TariffId { get; set; }
    public Tariff Tariff { get; set; } = null!;

    /// <summary>The account cannot be used after this date (UTC). Null = no end.</summary>
    public DateTime? ExpiresAt { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>The package currently being used.</summary>
    public Allowance Allowance { get; set; } = new();

    public DateTime CreatedAt { get; set; }
}

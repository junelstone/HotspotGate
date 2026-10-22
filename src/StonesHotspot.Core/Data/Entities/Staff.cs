namespace StonesHotspot.Core.Data.Entities;

public class Staff
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public StaffRole Role { get; set; }
    public bool Enabled { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

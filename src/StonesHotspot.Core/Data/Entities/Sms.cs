namespace StonesHotspot.Core.Data.Entities;

/// <summary>A login code sent by SMS. Only a hash of the code is stored.</summary>
public class SmsChallenge
{
    public long Id { get; set; }
    public string Phone { get; set; } = "";
    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? UsedAt { get; set; }
}

/// <summary>One SMS the hotspot tried to send (the text is not kept: it may contain a code).</summary>
public class SmsMessage
{
    public long Id { get; set; }
    public string Phone { get; set; } = "";
    public SmsPurpose Purpose { get; set; }
    public int? DeviceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Sent { get; set; }
    public string? Error { get; set; }
}

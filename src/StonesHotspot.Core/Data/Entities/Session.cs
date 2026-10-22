namespace StonesHotspot.Core.Data.Entities;

public class Session
{
    public long Id { get; set; }
    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public LoginMethod Method { get; set; }

    public int? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public int? UserAccountId { get; set; }
    public UserAccount? UserAccount { get; set; }

    /// <summary>Tariff in force for this session (speed limits, idle timeout).</summary>
    public int TariffId { get; set; }
    public Tariff Tariff { get; set; } = null!;

    public string? Ip { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public SessionEndReason? EndReason { get; set; }

    public DateTime LastActivityAt { get; set; }

    /// <summary>Up to when <see cref="SecondsUsed"/> has been counted.</summary>
    public DateTime LastAccountedAt { get; set; }

    public long BytesUp { get; set; }
    public long BytesDown { get; set; }
    public long SecondsUsed { get; set; }

    public bool IsActive => EndedAt is null;
}

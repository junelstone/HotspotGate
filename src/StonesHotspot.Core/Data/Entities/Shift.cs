using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Data.Entities;

public class Shift
{
    public int Id { get; set; }
    public int StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Xaf OpeningCash { get; set; }

    /// <summary>Opening cash + cash transactions, computed when the shift is closed.</summary>
    public Xaf? ExpectedClosingCash { get; set; }

    /// <summary>Cash counted by the staff member at closing.</summary>
    public Xaf? CountedClosingCash { get; set; }

    public string? Note { get; set; }

    public bool IsOpen => ClosedAt is null;
}

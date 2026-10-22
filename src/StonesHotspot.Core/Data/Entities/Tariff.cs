using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Data.Entities;

public class Tariff
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Xaf Price { get; set; }
    public TariffType Type { get; set; }

    /// <summary>Minutes for <see cref="TariffType.TimeBased"/>, MB for <see cref="TariffType.DataBased"/>, unused otherwise.</summary>
    public long Quota { get; set; }

    /// <summary>Validity window starting at first login, in hours. Null = no limit.</summary>
    public int? ValidityHours { get; set; }

    /// <summary>Fixed last day of use (UTC). Null = none.</summary>
    public DateTime? ValidUntil { get; set; }

    public CountdownMode CountdownMode { get; set; } = CountdownMode.UsageOnly;

    /// <summary>kbit/s; null = no limit.</summary>
    public int? DownloadKbps { get; set; }

    /// <summary>kbit/s; null = no limit.</summary>
    public int? UploadKbps { get; set; }

    public int MaxDevices { get; set; } = 1;

    /// <summary>Minutes without traffic before a session ends. Null = use the default setting.</summary>
    public int? IdleTimeoutMinutes { get; set; }

    /// <summary>Inactive tariffs are hidden from sale but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}

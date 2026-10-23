namespace StonesHotspot.Core.Data.Entities;

public class Voucher
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string BatchName { get; set; } = "";
    public int TariffId { get; set; }
    public Tariff Tariff { get; set; } = null!;
    public VoucherStatus Status { get; set; } = VoucherStatus.New;
    public bool SingleUse { get; set; }

    public int? SoldByStaffId { get; set; }
    public Staff? SoldByStaff { get; set; }
    public DateTime? SoldAt { get; set; }
    public DateTime? FirstUsedAt { get; set; }

    public Allowance Allowance { get; set; } = new();

    public VoucherSource Source { get; set; } = VoucherSource.Staff;

    /// <summary>For portal-made vouchers: the device it was given to.</summary>
    public int? IssuedToDeviceId { get; set; }
    public Device? IssuedToDevice { get; set; }

    /// <summary>For SMS logins (and, later, Mobile Money): the customer's number, +242…</summary>
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; }
}

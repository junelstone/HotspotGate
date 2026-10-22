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
    public DateTime CreatedAt { get; set; }
}

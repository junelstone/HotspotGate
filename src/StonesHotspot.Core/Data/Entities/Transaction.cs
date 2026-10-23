using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Data.Entities;

/// <summary>One money movement. Refunds are negative. Lines sold together share a receipt number.</summary>
public class Transaction
{
    public long Id { get; set; }
    public TransactionType Type { get; set; }
    public Xaf Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public DateTime Timestamp { get; set; }
    public string ReceiptNumber { get; set; } = "";

    public int? StaffId { get; set; }
    public Staff? Staff { get; set; }
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public int? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public int? UserAccountId { get; set; }
    public UserAccount? UserAccount { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; } = 1;

    /// <summary>VAT included in <see cref="Amount"/>, as a fraction (0.189 = 18.9 %), as it was at the sale.</summary>
    public decimal VatRate { get; set; }

    public string? Description { get; set; }

    /// <summary>Mobile Money transaction ID or other outside reference.</summary>
    public string? ExternalReference { get; set; }
}

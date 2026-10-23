using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Data.Entities;

public enum MobilePaymentStatus
{
    /// <summary>Asked to the operator; the customer has not approved or refused yet.</summary>
    Pending,

    /// <summary>Claimed by one worker that is creating the code (a short moment).</summary>
    Processing,

    Successful,
    Failed,

    /// <summary>Not approved in time. Can still turn successful if the operator says so later.</summary>
    TimedOut,
}

/// <summary>A Wi-Fi plan bought on the portal with MTN MoMo or Airtel Money.</summary>
public class MobilePayment
{
    public long Id { get; set; }

    /// <summary>Our reference (UUID) given to the operator: MTN X-Reference-Id, Airtel transaction id.</summary>
    public string Reference { get; set; } = "";

    public PaymentMethod Method { get; set; }
    public string Phone { get; set; } = "";
    public int TariffId { get; set; }
    public Tariff Tariff { get; set; } = null!;
    public Xaf Amount { get; set; }
    public int? DeviceId { get; set; }
    public Device? Device { get; set; }

    public MobilePaymentStatus Status { get; set; }

    /// <summary>The operator's own transaction id (on its statements), once paid.</summary>
    public string? OperatorTransactionId { get; set; }

    /// <summary>Why it failed (operator reason or error), for staff.</summary>
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public int? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public string? ReceiptNumber { get; set; }
}

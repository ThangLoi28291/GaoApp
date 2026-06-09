namespace GaoApp.Application.DTOs.POS;

public sealed class OrderReceiptDto
{
    public int OrderId { get; set; }
    public string? OrderNumber { get; set; }

    public string Status { get; set; } = default!;
    public string PaymentStatus { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }

    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }

    public string? CashierName { get; set; }
    public string? ShiftCode { get; set; }

    public string? Note { get; set; }

    public string? StoreName { get; set; }
    public string? StoreAddress { get; set; }
    public string? StorePhone { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public decimal PaidTotal { get; set; }
    public decimal BalanceDue { get; set; }
    public decimal ChangeDue { get; set; }

    public int TotalLines { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal RefundedTotal { get; set; }
    public decimal RefundableRemaining { get; set; }
    public decimal VoucherDiscountTotal { get; set; }

    public List<OrderRewardVoucherDto> RewardVouchers { get; set; } = new();

    public List<OrderLineDto> Lines { get; set; } = new();
    public List<OrderPaymentDto> Payments { get; set; } = new();
}
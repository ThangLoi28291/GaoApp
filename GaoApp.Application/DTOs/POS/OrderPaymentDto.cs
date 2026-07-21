

namespace GaoApp.Application.DTOs.POS;

public sealed class OrderPaymentDto
{
    public int PaymentId { get; set; }
    public string Method { get; set; } = default!;
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
}

namespace GaoApp.Application.DTOs.Returns;

public sealed class OrderReturnEligibilityDto
{
    public int OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidTotal { get; set; }
    public decimal RefundedTotal { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<OrderReturnEligibilityLineDto> Lines { get; set; } = new();
}


using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

public class POSShiftSummaryOrderDto
{
    public int Id { get; set; }
    public string? OrderNumber { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }

    public decimal GrandTotal { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
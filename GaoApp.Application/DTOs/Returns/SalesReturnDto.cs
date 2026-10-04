using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Returns;

public sealed class SalesReturnDto
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public SalesReturnType Type { get; set; }
    public SalesReturnStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal ReturnSubtotal { get; set; }
    public decimal RefundTotal { get; set; }
    public decimal DepositRestoredTotal { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool HasPendingRestock { get; set; }

    public List<SalesReturnLineDto> Lines { get; set; } = new();
    public List<SalesReturnPaymentDto> Payments { get; set; } = new();
}

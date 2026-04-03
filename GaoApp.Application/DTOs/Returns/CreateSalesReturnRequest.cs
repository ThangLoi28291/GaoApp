using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Returns;

public sealed class CreateSalesReturnRequest
{
    public int OrderId { get; set; }
    public int POSShiftId { get; set; }
    public SalesReturnType Type { get; set; }
    public string Reason { get; set; } = default!;
    public string? Note { get; set; }

    public List<CreateSalesReturnLineRequest> Lines { get; set; } = new();
    public List<CreateSalesReturnPaymentRequest> Payments { get; set; } = new();
}
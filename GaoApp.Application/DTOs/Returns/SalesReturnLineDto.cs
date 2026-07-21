using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Returns;

public sealed class SalesReturnLineDto
{
    public int Id { get; set; }
    public int OrderLineId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal ReturnQuantity { get; set; }
    public decimal ReturnBaseQuantity { get; set; }
    public decimal RefundUnitAmount { get; set; }
    public decimal RefundLineTotal { get; set; }
    public SalesReturnLineAction Action { get; set; }
}
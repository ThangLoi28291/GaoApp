namespace GaoApp.Application.DTOs.Returns;

public sealed class PendingReturnRestockDto
{
    public int ReturnId { get; set; }
    public int OrderId { get; set; }
    public string ReturnNumber { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; }
    public decimal RefundedAmount { get; set; }
    public bool CanComplete { get; set; } = true;
    public List<PendingReturnRestockItemDto> Items { get; set; } = [];
}

public sealed class PendingReturnRestockItemDto
{
    public string ItemName { get; set; } = "";
    public string WarehouseName { get; set; } = "";
    public decimal BaseQuantity { get; set; }
    public string? BlockReason { get; set; }
}

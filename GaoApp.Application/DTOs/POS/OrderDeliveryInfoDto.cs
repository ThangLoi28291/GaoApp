namespace GaoApp.Application.DTOs.POS;

/// <summary>Read-only links between a delivery, its original cart and its settled POS sale.</summary>
public sealed class OrderDeliveryInfoDto
{
    public int DeliveryOrderId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = "pending";
    public int SourceOrderId { get; set; }
    public int? SaleOrderId { get; set; }
    public string? SaleOrderNumber { get; set; }
    public bool IsSettlementSale { get; set; }
    public bool IsPendingSettlement => SettlementStatus == "pending";
    public bool IsCancelled => SettlementStatus == "cancelled";
    public string DisplayStatus => SettlementStatus switch
    {
        "settled" => "DeliverySettled",
        "cancelled" => "DeliveryCancelled",
        _ => "DeliveryPending"
    };
    public string BillUrl => $"/admin/deliveries/{DeliveryOrderId}/bill";
}

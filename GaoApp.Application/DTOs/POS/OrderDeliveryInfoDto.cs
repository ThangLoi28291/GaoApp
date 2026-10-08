namespace GaoApp.Application.DTOs.POS;

/// <summary>Read-only overlay for an archived POS cart linked to a same-store delivery.</summary>
public sealed class OrderDeliveryInfoDto
{
    public int DeliveryOrderId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = "pending";
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

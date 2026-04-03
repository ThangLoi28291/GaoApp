namespace GaoApp.Domain.Constants;

/// <summary>
/// Danh sách action chuẩn dùng cho POSAuditLog.
/// Tạm dùng string constant để không phải đổi database ngay.
/// </summary>
public static class POSAuditActions
{
    public const string OrderCreated = "ORDER_CREATED";
    public const string OrderUpdated = "ORDER_UPDATED";
    public const string OrderFinalized = "ORDER_FINALIZED";
    public const string OrderVoided = "ORDER_VOIDED";
    public const string OrderCancelled = "ORDER_CANCELLED";
    public const string OrderRefunded = "ORDER_REFUNDED";
    public const string PaymentAdded = "PAYMENT_ADDED";
    public const string PaymentRemoved = "PAYMENT_REMOVED";
}
namespace GaoApp.Domain.Enums;

/// <summary>
/// Vòng đời của yêu cầu mua nội bộ. Yêu cầu mua không phải là đơn đặt hàng:
/// nó chỉ ghi nhận nhu cầu và được chuyển thành một hoặc nhiều PurchaseOrder.
/// </summary>
public enum PurchaseRequestStatus
{
    Draft = 1,
    PendingApproval = 2,
    ReturnedForRevision = 3,
    Rejected = 4,
    Approved = 5,
    PartiallyConverted = 6,
    Converted = 7,
    Cancelled = 8
}

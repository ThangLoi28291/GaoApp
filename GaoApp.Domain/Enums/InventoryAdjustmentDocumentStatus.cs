namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái phiếu điều chỉnh kho.
/// </summary>
public enum InventoryAdjustmentDocumentStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

/// <summary>
/// Các invariant của vòng đời đơn đặt hàng. Mọi endpoint ghi phải đi qua
/// policy này; UI chỉ phản ánh lại quyền và trạng thái, không phải lớp bảo vệ.
/// </summary>
public static class PurchaseOrderWorkflowPolicy
{
    public static bool IsCommerciallyEditable(PurchaseOrderStatus status)
        => status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.ReturnedForRevision;

    public static void EnsureCommerciallyEditable(PurchaseOrderStatus status)
    {
        if (!IsCommerciallyEditable(status))
            throw new InvalidOperationException(
                "Chỉ được sửa đơn nháp hoặc đơn được trả về chỉnh sửa.");
    }

    public static void EnsureCanSubmit(PurchaseOrderStatus status)
    {
        if (!IsCommerciallyEditable(status))
            throw new InvalidOperationException(
                "Chỉ đơn nháp hoặc đơn được trả về mới có thể gửi duyệt.");
    }

    public static void EnsureCanReview(
        PurchaseOrderStatus status,
        int? reviewingUserId)
    {
        if (status != PurchaseOrderStatus.PendingApproval)
            throw new InvalidOperationException("Đơn không ở trạng thái chờ duyệt.");
        if (!reviewingUserId.HasValue)
            throw new InvalidOperationException("Không xác định được người duyệt đơn.");
    }

    public static void EnsureCanCancel(PurchaseOrderStatus status, bool hasReceivedGoods)
    {
        if (hasReceivedGoods)
            throw new InvalidOperationException("Không thể hủy đơn đã phát sinh nhận hàng.");
        if (status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Rejected or
            PurchaseOrderStatus.FullyReceived or PurchaseOrderStatus.ShortClosed)
            throw new InvalidOperationException("Trạng thái đơn không cho phép hủy.");
    }

    public static void EnsureCanCloseOutstanding(PurchaseOrderStatus status)
    {
        if (!CanCloseOutstanding(status))
            throw new InvalidOperationException(
                "Chỉ đơn đã duyệt, đã gửi nhà cung cấp hoặc đang nhận hàng mới được đóng phần thiếu.");
    }

    public static bool CanCloseOutstanding(PurchaseOrderStatus status)
        => status is PurchaseOrderStatus.Approved or
            PurchaseOrderStatus.SentToSupplier or PurchaseOrderStatus.PartiallyReceived;

    public static void EnsureCanReopenOutstanding(PurchaseOrderStatus status)
    {
        if (!CanReopenOutstanding(status))
            throw new InvalidOperationException(
                "Trạng thái đơn không cho phép mở lại phần đã đóng.");
    }

    public static bool CanReopenOutstanding(PurchaseOrderStatus status)
        => status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval or
            PurchaseOrderStatus.ReturnedForRevision or PurchaseOrderStatus.Rejected or
            PurchaseOrderStatus.FullyReceived or PurchaseOrderStatus.Cancelled);
}

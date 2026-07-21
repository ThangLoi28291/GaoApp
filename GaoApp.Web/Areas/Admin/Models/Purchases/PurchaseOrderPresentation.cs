using GaoApp.Domain.Enums;

namespace GaoApp.Web.Areas.Admin.Models.Purchases;

public static class PurchaseOrderPresentation
{
    public static string StatusLabel(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "Đơn nháp",
        PurchaseOrderStatus.PendingApproval => "Chờ duyệt",
        PurchaseOrderStatus.ReturnedForRevision => "Cần chỉnh sửa",
        PurchaseOrderStatus.Rejected => "Đã từ chối",
        PurchaseOrderStatus.Approved => "Đã duyệt",
        PurchaseOrderStatus.SentToSupplier => "Đã gửi NCC",
        PurchaseOrderStatus.PartiallyReceived => "Đang nhận hàng",
        PurchaseOrderStatus.FullyReceived => "Đã nhận đủ",
        PurchaseOrderStatus.ShortClosed => "Đã đóng thiếu",
        PurchaseOrderStatus.Cancelled => "Đã hủy",
        _ => status.ToString()
    };

    public static string StatusClass(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "bg-label-secondary",
        PurchaseOrderStatus.PendingApproval => "bg-label-warning",
        PurchaseOrderStatus.ReturnedForRevision => "bg-label-info",
        PurchaseOrderStatus.Rejected => "bg-label-danger",
        PurchaseOrderStatus.Approved => "bg-label-success",
        PurchaseOrderStatus.SentToSupplier => "bg-label-primary",
        PurchaseOrderStatus.PartiallyReceived => "bg-label-primary",
        PurchaseOrderStatus.FullyReceived => "bg-label-success",
        PurchaseOrderStatus.ShortClosed => "bg-label-warning",
        PurchaseOrderStatus.Cancelled => "bg-label-dark",
        _ => "bg-label-secondary"
    };

    public static string ActionLabel(PurchaseOrderActionType action) => action switch
    {
        PurchaseOrderActionType.Created => "Tạo đơn",
        PurchaseOrderActionType.Updated => "Cập nhật đơn",
        PurchaseOrderActionType.Submitted => "Gửi duyệt",
        PurchaseOrderActionType.Approved => "Duyệt đơn",
        PurchaseOrderActionType.Rejected => "Từ chối đơn",
        PurchaseOrderActionType.ReturnedForRevision => "Trả về chỉnh sửa",
        PurchaseOrderActionType.SentToSupplier => "Đánh dấu đã gửi NCC",
        PurchaseOrderActionType.ReceiptCreated => "Tạo phiếu nhập",
        PurchaseOrderActionType.ReceiptApproved => "Duyệt phiếu nhập",
        PurchaseOrderActionType.ShortClosed => "Đóng phần giao thiếu",
        PurchaseOrderActionType.Cancelled => "Hủy đơn",
        _ => action.ToString()
    };
}

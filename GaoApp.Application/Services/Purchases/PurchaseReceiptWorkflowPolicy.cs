using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public static class PurchaseReceiptWorkflowPolicy
{
    public static bool CanEditPhysicalFields(StockDocumentStatus status)
        => status is StockDocumentStatus.Draft or StockDocumentStatus.Rejected;

    public static void EnsurePhysicalFieldsEditable(StockDocumentStatus status)
    {
        if (!CanEditPhysicalFields(status))
        {
            throw new BusinessRuleException(
                "Chỉ phiếu nháp hoặc phiếu đã được trả về chỉnh sửa mới được sửa thông tin nhận hàng.");
        }
    }

    public static bool CanManageInputInvoiceMapping(StockDocumentStatus status)
        => status == StockDocumentStatus.PendingApproval;

    public static void EnsureInputInvoiceMappingEditable(StockDocumentStatus status)
    {
        if (!CanManageInputInvoiceMapping(status))
        {
            throw new BusinessRuleException(
                "Chỉ quản lý được cập nhật mapping hóa đơn XML khi phiếu đang chờ duyệt.");
        }
    }
}

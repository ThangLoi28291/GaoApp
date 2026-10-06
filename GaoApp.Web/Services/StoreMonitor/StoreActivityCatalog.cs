using GaoApp.Application.Common.Security;

namespace GaoApp.Web.Services.StoreMonitor;

public static class StoreActivityCatalog
{
    public static readonly IReadOnlySet<string> Modules = new HashSet<string>(["receipt", "pos", "warehouse", "label", "review"], StringComparer.Ordinal);

    public static string? PageModule(string controller) => controller switch
    {
        "WarehouseReceiving" or "PurchaseReceiving" => "receipt",
        "StockDocumentManagement" => "review",
        "POS" => "pos",
        "StockCountPages" or "StockTransfer" or "InventoryAdjustmentDocuments" => "warehouse",
        "LabelPrinting" => "label",
        _ => null
    };
    public static string[] Permissions(string module) => module switch
    {
        "receipt" or "review" => [PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Create,
            PermissionCodes.Inventory.StockDocument.Update, PermissionCodes.Inventory.StockDocument.Approve,
            PermissionCodes.Purchase.Receipt.View, PermissionCodes.Purchase.Receipt.Update, PermissionCodes.Purchase.Receipt.Approve],
        "pos" => [PermissionCodes.Pos.Order.View],
        "warehouse" => [PermissionCodes.Inventory.StockCount.View, PermissionCodes.Inventory.StockTransfer.View, PermissionCodes.Inventory.Adjustment.View],
        "label" => [PermissionCodes.System.ProductLabel.Print],
        _ => []
    };
    // Only successful, known writes enter the event rail. Lookup, preview, refresh and QR reads do not.
    public static (string Module, string Text)? Mutation(string controller, string action) => controller switch
    {
        "WarehouseReceiving" when action == "CreateReceipt" => ("receipt", "vừa tạo phiếu nhập"),
        "ReceiptIntake" when action is "Capture" or "Known" or "Quantity" or "Remove" => ("receipt", "vừa cập nhật mặt hàng nhập"),
        "ReceiptIntake" when action == "Review" => ("review", "vừa duyệt mặt hàng nhập"),
        "StockDocuments" when action == "CreateReceipt" => ("receipt", "vừa tạo phiếu nhập"),
        "StockDocuments" when action == "AddLine" => ("receipt", "vừa thêm mặt hàng vào phiếu nhập"),
        "StockDocuments" when action == "AddLineByBarcode" => ("receipt", "vừa quét hàng vào phiếu nhập"),
        "StockDocuments" when action == "UpdateLine" => ("receipt", "vừa sửa số lượng hoặc giá dòng nhập"),
        "StockDocuments" when action == "DeleteLine" => ("receipt", "vừa bỏ mặt hàng khỏi phiếu nhập"),
        "StockDocuments" when action == "SubmitApproval" => ("review", "vừa gửi phiếu nhập chờ duyệt"),
        "StockDocuments" when action is "Approve" or "ApproveCommercial" => ("review", "vừa duyệt phiếu nhập"),
        "StockDocuments" when action == "Reject" => ("review", "vừa từ chối phiếu nhập"),
        "StockDocuments" when action is "SavePriceDraft" or "SavePricingAllocation" or "ApplyPricingAllocation" => ("review", "vừa cập nhật giá phiếu nhập"),
        "StockDocumentManagement" when action is "UpdateHeader" or "UpdateFreight" => ("receipt", "vừa cập nhật phiếu nhập"),
        "POS" => PosMutation(action),
        "StockCounts" when action == "Create" => ("warehouse", "vừa tạo phiếu kiểm kê"),
        "StockCounts" when action == "AddLine" => ("warehouse", "vừa thêm mặt hàng kiểm kê"),
        "StockCounts" when action == "UpdateLine" => ("warehouse", "vừa ghi số lượng kiểm kê"),
        "StockCounts" when action == "DeleteLine" => ("warehouse", "vừa bỏ dòng kiểm kê"),
        "StockCounts" when action == "UpdateHeader" => ("warehouse", "vừa sửa thông tin phiếu kiểm kê"),
        "StockCounts" when action == "SubmitForApproval" => ("review", "vừa gửi kiểm kê chờ duyệt"),
        "StockCounts" when action == "Confirm" => ("review", "vừa xác nhận kiểm kê"),
        "StockCounts" when action == "Reject" => ("review", "vừa từ chối kiểm kê"),
        "StockTransfers" when action == "Create" => ("warehouse", "vừa tạo phiếu chuyển kho"),
        "StockTransfers" when action == "AddLine" => ("warehouse", "vừa thêm hàng chuyển kho"),
        "StockTransfers" when action == "UpdateLine" => ("warehouse", "vừa sửa số lượng chuyển kho"),
        "StockTransfers" when action == "DeleteLine" => ("warehouse", "vừa bỏ dòng chuyển kho"),
        "StockTransfers" when action == "UpdateHeader" => ("warehouse", "vừa sửa thông tin chuyển kho"),
        "StockTransfers" when action == "Submit" => ("review", "vừa gửi chuyển kho chờ duyệt"),
        "StockTransfers" when action == "Reject" => ("review", "vừa từ chối chuyển kho"),
        "StockTransfers" when action is "Confirm" or "Approve" => ("warehouse", "vừa xác nhận chuyển kho"),
        "LabelPrinting" when action == "Print" => ("label", "vừa gửi lệnh in tem"),
        "LabelPrinting" when action == "Confirm" => ("label", "vừa xác nhận tem đã in"),
        "LabelPrinting" when action == "Complete" => ("label", "vừa hoàn tất phiếu tem"),
        "LabelPrinting" when action == "Cancel" => ("label", "vừa hủy lệnh in tem"),
        "LabelPrinting" when action == "Plan" => ("label", "vừa cập nhật kế hoạch in tem"),
        _ => null
    };
    private static (string, string)? PosMutation(string action) => action switch
    {
        "CreateDraft" or "CreateAndSwitchNewCart" => ("pos", "vừa tạo đơn bán hàng"),
        "AddItem" => ("pos", "vừa thêm mặt hàng vào giỏ"),
        "AddByBarcode" or "ScanToCurrentCart" => ("pos", "vừa quét mã hàng vào giỏ"),
        "UpdateQty" => ("pos", "vừa sửa số lượng mặt hàng"),
        "RemoveLine" => ("pos", "vừa bỏ mặt hàng khỏi giỏ"),
        "ClearCurrentCartLines" => ("pos", "vừa dọn các mặt hàng trong giỏ"),
        "AddPayment" or "AddPaymentToCurrentCart" or "AddPaymentAndMaybeFinalizeCurrentCart" => ("pos", "vừa ghi nhận thanh toán"),
        "Finalize" or "FinalizeCurrentCart" or "FinalizeCredit" => ("pos", "vừa chốt đơn bán hàng"),
        "HoldOrder" or "HoldCurrentCart" => ("pos", "vừa giữ đơn chờ xử lý"),
        "ResumeHeldOrder" => ("pos", "vừa mở lại đơn giữ"),
        "Cancel" or "CancelCurrentCart" => ("pos", "vừa hủy đơn"),
        "VoidOrder" or "RefundOrder" => ("pos", "vừa xử lý hoàn / hủy đơn"),
        _ => null
    };
}

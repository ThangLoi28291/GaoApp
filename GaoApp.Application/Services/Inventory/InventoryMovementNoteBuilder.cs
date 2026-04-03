using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Build note chuẩn hóa cho movement kho.
/// 
/// Nguyên tắc:
/// - dễ đọc với người quản lý
/// - đủ thông tin để audit nhanh
/// - không nhồi quá nhiều dữ liệu kỹ thuật khó nhìn
/// - ưu tiên format nhất quán giữa các nghiệp vụ
/// </summary>
public class InventoryMovementNoteBuilder : IInventoryMovementNoteBuilder
{
    public string BuildPurchaseReceiptNote(string documentNo, int lineNo)
    {
        documentNo = CleanText(documentNo, "N/A");
        return $"Nhập kho từ phiếu {documentNo}, dòng {lineNo}.";
    }

    public string BuildAdjustmentNote(
        string? userNote,
        string? unitName,
        decimal inputQuantity,
        decimal factor,
        decimal baseQuantity)
    {
        unitName = CleanText(unitName, "N/A");

        var systemNote =
            $"Điều chỉnh thủ công. Đơn vị: {unitName}. SL nhập: {inputQuantity:0.###}. " +
            $"Hệ số quy đổi: {factor:0.###}. SL gốc: {baseQuantity:0.###}.";

        if (string.IsNullOrWhiteSpace(userNote))
            return systemNote;

        var cleanedUserNote = userNote.Trim().TrimEnd('.', ';', ',');
        return $"{cleanedUserNote}. {systemNote}";
    }

    public string BuildSaleFinalizeNote(
        int orderId,
        int lineId,
        string? itemName,
        decimal qtyBase)
    {
        itemName = CleanText(itemName, "N/A");
        return $"Xuất kho do chốt đơn POS. Đơn #{orderId}, dòng {lineId}, SP: {itemName}, SL gốc: {qtyBase:0.###}.";
    }

    public string BuildSaleVoidNote(string reason)
    {
        reason = CleanText(reason, "Không có lý do");
        return $"Nhập lại kho do hủy đơn sau khi chốt. Lý do: {reason}.";
    }

    public string BuildSaleRefundNote(string reason)
    {
        reason = CleanText(reason, "Không có lý do");
        return $"Nhập lại kho do trả hàng / hoàn tiền. Lý do: {reason}.";
    }

    public string BuildStockCountGainNote(string documentNo, int lineNo, decimal differenceQtyBase)
    {
        documentNo = CleanText(documentNo, "N/A");
        return $"Kiểm kê tăng tồn từ phiếu {documentNo}, dòng {lineNo}. Chênh lệch: +{Math.Abs(differenceQtyBase):0.###}.";
    }

    public string BuildStockCountLossNote(string documentNo, int lineNo, decimal differenceQtyBase)
    {
        documentNo = CleanText(documentNo, "N/A");
        return $"Kiểm kê giảm tồn từ phiếu {documentNo}, dòng {lineNo}. Chênh lệch: -{Math.Abs(differenceQtyBase):0.###}.";
    }

    public string BuildTransferOutNote(StockTransferDocument document, StockTransferLine line)
    {
        var documentNo = CleanText(document.DocumentNo, "N/A");
        var productName = CleanText(line.ProductNameSnapshot, $"Variant #{line.ProductVariantId}");

        return $"Xuất chuyển kho từ phiếu {documentNo}, dòng {line.LineNo}. " +
               $"Từ kho {document.FromWarehouseId} sang kho {document.ToWarehouseId}. " +
               $"SP: {productName}. SL gốc: {line.BaseQuantity:0.###}.";
    }

    public string BuildTransferInNote(StockTransferDocument document, StockTransferLine line)
    {
        var documentNo = CleanText(document.DocumentNo, "N/A");
        var productName = CleanText(line.ProductNameSnapshot, $"Variant #{line.ProductVariantId}");

        return $"Nhập chuyển kho từ phiếu {documentNo}, dòng {line.LineNo}. " +
               $"Từ kho {document.FromWarehouseId} sang kho {document.ToWarehouseId}. " +
               $"SP: {productName}. SL gốc: {line.BaseQuantity:0.###}.";
    }

    private static string CleanText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
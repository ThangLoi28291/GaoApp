using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Service chuyên build note chuẩn hóa cho inventory movement.
///
/// Mục tiêu:
/// - tránh hard-code note rải rác khắp nơi
/// - giúp ledger dễ đọc
/// - thống nhất format note toàn hệ thống
/// </summary>
public interface IInventoryMovementNoteBuilder
{
    string BuildPurchaseReceiptNote(string documentNo, int lineNo);

    string BuildAdjustmentNote(
        string? userNote,
        string? unitName,
        decimal inputQuantity,
        decimal factor,
        decimal baseQuantity);

    string BuildSaleFinalizeNote(
        int orderId,
        int lineId,
        string? itemName,
        decimal qtyBase);

    string BuildSaleVoidNote(string reason);

    string BuildSaleRefundNote(string reason);

    string BuildStockCountGainNote(string documentNo, int lineNo, decimal differenceQtyBase);

    string BuildStockCountLossNote(string documentNo, int lineNo, decimal differenceQtyBase);

    string BuildTransferOutNote(StockTransferDocument document, StockTransferLine line);

    string BuildTransferInNote(StockTransferDocument document, StockTransferLine line);
}
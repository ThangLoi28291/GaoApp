using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Service thực thi movement tồn kho.
/// 
/// Vai trò:
/// - nhận CreateInventoryMovementRequest đã được factory chuẩn hóa
/// - kiểm tra policy âm kho / duplicate / qty
/// - ghi InventoryTransaction + balance + log âm kho
/// - dùng chung cho purchase receipt / adjustment / sale / transfer / stock count...
/// </summary>
public interface IInventoryMovementService
{
    /// <summary>
    /// Tạo 1 movement tồn kho thực sự:
    /// - cập nhật balance
    /// - ghi InventoryTransaction
    /// - ghi NegativeInventoryLog nếu cần
    /// </summary>
    Task<InventoryMovementResultDto> CreateAsync(
        CreateInventoryMovementRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Chỉ đọc / ước tính unit cost outbound hiện tại của 1 variant trong 1 warehouse
    /// theo đúng costing engine đang có (FIFO / provisional / revaluation),
    /// nhưng KHÔNG ghi InventoryTransaction.
    ///
    /// Dùng cho các nghiệp vụ cần snapshot cost trước khi tạo movement thật,
    /// ví dụ: TransferOut -> lưu UnitCostSnapshot lên StockTransferLine.
    /// </summary>
    Task<decimal> PeekOutboundUnitCostAsync(
        int warehouseId,
        int productVariantId,
        decimal quantity,
        CancellationToken ct = default);
}
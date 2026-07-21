using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Request chuẩn hóa cho 1 lần ghi biến động kho.
/// Đây là input nội bộ dùng cho InventoryMovementService.
///
/// Quy ước:
/// - QuantityChange > 0  => tăng tồn
/// - QuantityChange < 0  => giảm tồn
/// - luôn tính theo đơn vị gốc
///
/// Lưu ý:
/// - request này mô tả một movement đơn lẻ
/// - không tự cập nhật balance
/// - không tự ghi ledger
/// - việc xử lý thực tế thuộc InventoryMovementService
/// </summary>
public class CreateInventoryMovementRequest
{
    public int WarehouseId { get; set; }

    public int ProductVariantId { get; set; }

    /// <summary>
    /// Dương = tăng tồn, âm = giảm tồn.
    /// Giá trị này luôn tính theo đơn vị gốc.
    /// </summary>
    public decimal QuantityChange { get; set; }

    public InventoryTransactionType TransactionType { get; set; }

    public InventoryReferenceType ReferenceType { get; set; }

    public string? ReferenceId { get; set; }

    public int? ReferenceLineId { get; set; }

    public DateTime? OccurredAtUtc { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Nếu true thì movement service sẽ kiểm tra transaction trùng theo
    /// ReferenceType + ReferenceId + ReferenceLineId + TransactionType.
    /// Nếu đã tồn tại thì bỏ qua để đảm bảo idempotent.
    /// </summary>
    public bool SkipIfExists { get; set; } = true;
    /// <summary>
    /// Cost đầu vào cho movement nhập.
    /// Có thể dùng cho một số adjustment increase.
    /// </summary>
    public decimal? UnitCost { get; set; }

    /// <summary>
    /// Cost tạm dùng cho phần qty âm.
    /// </summary>
    public decimal? ProvisionalUnitCost { get; set; }

    /// <summary>
    /// Cho phép ép tạo provisional cho movement âm.
    /// Thường service tự quyết dựa trên afterQty < 0.
    /// </summary>
    public bool ForceProvisionalWhenNegative { get; set; } = true;

    /// <summary>
    /// Khóa phụ để phân biệt nhiều movement part trong cùng 1 reference line.
    /// Rất quan trọng cho void/refund mức 2 khi mirror nhiều valuation entry.
    /// </summary>
    public string? ReferenceSubKey { get; set; }
    /// <summary>
    /// Nếu movement này là reverse/mirror từ 1 source valuation entry,
    /// field này trỏ về valuation entry nguồn.
    /// 
    /// Ví dụ:
    /// - CustomerReturnIn reverse từ SaleIssue fragment gốc
    /// - TransferIn mirror từ TransferOut fragment gốc
    /// </summary>
    public int? SourceValuationEntryId { get; set; }

    /// <summary>
    /// Snapshot subkey của source fragment.
    /// Hữu ích cho audit/debug nhanh.
    /// </summary>
    public string? SourceReferenceSubKey { get; set; }
    /// <summary>
    /// Cho phép movement vẫn được ghi ngay cả khi afterQty < 0
    /// dù warehouse đang cấu hình không cho âm.
    /// 
    /// Dùng riêng cho các flow business được phép complete trước,
    /// rồi chuyển sang pending manager approval.
    /// 
    /// Ví dụ:
    /// - POS finalize mức 2
    /// </summary>
    public bool AllowNegativeBalance { get; set; } = false;
}
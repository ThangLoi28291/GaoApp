using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Ledger / sổ cái phát sinh biến động kho.
/// 
/// Mỗi lần nhập / xuất / điều chỉnh / kiểm kê / chuyển kho hợp lệ sẽ ghi 1 dòng.
/// Đây là nguồn dữ liệu audit quan trọng nhất để truy vết lịch sử tồn kho.
/// 
/// Nguyên tắc quan trọng:
/// - InventoryTransaction là ledger bất biến về mặt nghiệp vụ.
/// - Không sửa ý nghĩa transaction đã ghi chỉ để "điều chỉnh số".
/// - Nếu nghiệp vụ trước ghi sai, phải tạo transaction ngược chiều (reverse),
///   hoặc tạo transaction điều chỉnh mới để bù trừ.
/// 
/// Nói cách khác:
/// - sai thì ghi bút toán mới để sửa
/// - không rewrite lịch sử cũ
/// </summary>
public class InventoryTransaction : BaseStoreEntity
{
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }

    /// <summary>
    /// SHA-256 key for the canonical durable posting identity.
    /// Null is reserved for legacy or explicitly non-idempotent movements.
    /// </summary>
    public byte[]? IdempotencyKey { get; set; }

    /// <summary>
    /// Loại biến động kho.
    /// Ví dụ: nhập mua, bán hàng, trả hàng, kiểm kê tăng/giảm, chuyển kho...
    /// </summary>
    public InventoryTransactionType TransactionType { get; set; }

    /// <summary>
    /// Loại chứng từ/nghiệp vụ nguồn sinh ra transaction.
    /// Ví dụ: Order, StockDocument, StockCount, StockTransfer, Adjustment...
    /// </summary>
    public InventoryReferenceType ReferenceType { get; set; }



    /// <summary>
    /// Id chứng từ nguồn, ví dụ OrderId, StockCountId, StockTransferId...
    /// Lưu dạng string để linh hoạt cho nhiều loại nguồn khác nhau.
    /// </summary>
    public string? ReferenceId { get; set; }

    /// <summary>
    /// Id dòng chứng từ nguồn, ví dụ OrderLineId, StockCountLineId...
    /// 
    /// Dùng để:
    /// - chống ghi trùng transaction cùng 1 line
    /// - hỗ trợ idempotent cho các nghiệp vụ confirm/finalize
    /// - hỗ trợ truy vết theo từng dòng chi tiết
    /// </summary>
    public int? ReferenceLineId { get; set; }

    /// <summary>
    /// Lượng thay đổi tồn kho.
    /// 
    /// Quy ước:
    /// - Dương  = tăng tồn
    /// - Âm     = giảm tồn
    /// </summary>
    public decimal QuantityChange { get; set; }

    /// <summary>
    /// Tồn trước khi ghi transaction này.
    /// </summary>
    public decimal BeforeQty { get; set; }

    /// <summary>
    /// Tồn sau khi ghi transaction này.
    /// </summary>
    public decimal AfterQty { get; set; }

    /// <summary>
    /// Đơn giá vốn snapshot dùng cho transaction này.
    /// 
    /// Ví dụ:
    /// - giao dịch bán ra: là cost tại thời điểm issue
    /// - giao dịch nhập vào: là cost nhập thực tế
    /// - giao dịch provisional: là cost tạm
    /// </summary>
    public decimal UnitCostSnapshot { get; set; }
    /// <summary>
    /// Tổng giá trị tác động của transaction này.
    /// 
    /// Quy ước:
    /// - nhập kho: Amount dương
    /// - xuất kho: Amount âm
    /// 
    /// Ví dụ:
    /// QuantityChange = -2
    /// UnitCostSnapshot = 10,000
    /// TotalCost = -20,000
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Giá trị tồn kho trước giao dịch.
    /// </summary>
    public decimal BeforeInventoryValue { get; set; }

    /// <summary>
    /// Giá trị tồn kho sau giao dịch.
    /// </summary>
    public decimal AfterInventoryValue { get; set; }
    /// <summary>
    /// Moving average sau khi transaction này được áp dụng.
    /// 
    /// Field này rất có ích để audit vì không cần tính lại cả sổ.
    /// </summary>
    public decimal RunningAverageUnitCostAfter { get; set; }

    /// <summary>
    /// Nguồn lấy cost cho transaction này.
    /// </summary>
    public InventoryCostSourceType CostSourceType { get; set; }

    /// <summary>
    /// Đánh dấu giao dịch này đang dùng giá vốn tạm.
    /// 
    /// Ví dụ:
    /// - bán ra khi âm kho
    /// - xuất kho nhưng cost phải fallback từ last inbound/default cost
    /// </summary>
    public bool IsProvisionalCost { get; set; }

    /// <summary>
    /// Nếu transaction từng provisional mà sau này đã được xác nhận/
    /// xử lý revaluation xong thì ghi thời điểm finalize cost.
    /// </summary>
    public DateTime? CostFinalizedAtUtc { get; set; }


    /// <summary>
    /// Thời điểm nghiệp vụ được ghi nhận xảy ra.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ghi chú diễn giải nghiệp vụ.
    /// Nên đủ rõ để người đọc log hiểu transaction đến từ đâu.
    /// </summary>
    [StringLength(1000)]
    public string? Note { get; set; }

    public Warehouse Warehouse { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;

    [StringLength(100)]
    public string? ReferenceSubKey { get; set; }
    /// <summary>
    /// 1 inventory transaction có thể sinh 1 hoặc nhiều valuation entry.
    /// Ví dụ:
    /// - 1 sale issue -> 1 outbound entry
    /// - 1 provisional issue sau này -> có thêm 1 revaluation entry riêng
    /// </summary>
    public ICollection<InventoryValuationEntry> ValuationEntries { get; set; } = new List<InventoryValuationEntry>();
}

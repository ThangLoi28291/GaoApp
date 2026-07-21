using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Nhật ký phát sinh âm kho.
/// Dùng để quản lý, cảnh báo và truy vết các giao dịch làm tồn kho xuống dưới 0.
/// </summary>
public class NegativeInventoryLog : BaseStoreEntity
{
    /// <summary>
    /// Kho phát sinh âm kho.
    /// </summary>
    public int WarehouseId { get; set; }

    /// <summary>
    /// Biến thể sản phẩm bị âm kho.
    /// </summary>
    public int ProductVariantId { get; set; }

    /// <summary>
    /// Tồn trước khi phát sinh giao dịch.
    /// </summary>
    public decimal BeforeQty { get; set; }

    /// <summary>
    /// Số lượng thay đổi.
    /// Ví dụ: -5.
    /// </summary>
    public decimal QuantityChange { get; set; }

    /// <summary>
    /// Tồn sau khi giao dịch hoàn tất.
    /// Nếu nhỏ hơn 0 tức là âm kho.
    /// </summary>
    public decimal AfterQty { get; set; }

    /// <summary>
    /// Loại giao dịch gây ra âm kho.
    /// Ví dụ: AdjustmentDecrease, SaleIssue...
    /// </summary>
    public InventoryTransactionType TransactionType { get; set; }

    /// <summary>
    /// Loại chứng từ nguồn.
    /// </summary>
    public InventoryReferenceType ReferenceType { get; set; }

    /// <summary>
    /// Id chứng từ nguồn.
    /// </summary>
    public string? ReferenceId { get; set; }

    /// <summary>
    /// Id dòng chứng từ nguồn nếu có.
    /// </summary>
    public int? ReferenceLineId { get; set; }

    /// <summary>
    /// Ghi chú nghiệp vụ.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Thời điểm phát sinh âm kho.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public Warehouse Warehouse { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;
}
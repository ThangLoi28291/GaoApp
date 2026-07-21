using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Sổ valuation chi tiết cho costing/revaluation.
///
/// Tại sao cần bảng này dù đã có InventoryTransaction?
///
/// Vì InventoryTransaction thiên về biến động số lượng.
/// InventoryValuationEntry thiên về biến động GIÁ TRỊ / giá vốn.
///
/// Ví dụ:
/// - 1 sale issue: có transaction giảm qty và 1..n outbound valuation entry
/// - sau này revalue: KHÔNG rewrite transaction cũ,
///   mà thêm 1 valuation entry type = Revaluation, quantity = 0, amount = +/-
///
/// Đây là nền tảng chuẩn cho:
/// - provisional cost
/// - FIFO layer-based costing
/// - revaluation adjustment
/// - return / void mirror theo fragment
/// </summary>
public class InventoryValuationEntry : BaseStoreEntity
{
    [Range(1, int.MaxValue)]
    public int InventoryTransactionId { get; set; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductVariantId { get; set; }

    public InventoryValuationEntryType EntryType { get; set; }

    public InventoryReferenceType ReferenceType { get; set; }

    [StringLength(64)]
    public string ReferenceId { get; set; } = null!;

    public int? ReferenceLineId { get; set; }

    /// <summary>
    /// Khóa phụ để tách nhiều fragment valuation trong cùng 1 reference line.
    ///
    /// Ví dụ:
    /// - sale line mixed cost có thể sinh nhiều fragment:
    ///   + layer 1
    ///   + layer 2
    ///   + provisional
    ///
    /// Return/Void/Transfer mức 2 sẽ dựa vào subkey này để trace và dedupe.
    /// </summary>
    [StringLength(100)]
    public string? ReferenceSubKey { get; set; }

    /// <summary>
    /// Số lượng ảnh hưởng valuation entry.
    ///
    /// Quy ước:
    /// - inbound: dương
    /// - outbound: âm
    /// - revaluation: thường = 0
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Đơn giá vốn được ghi nhận ở entry này.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Giá trị tác động của entry.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Running qty sau entry này.
    /// </summary>
    public decimal RunningQtyAfter { get; set; }

    /// <summary>
    /// Running inventory value sau entry này.
    /// </summary>
    public decimal RunningValueAfter { get; set; }

    /// <summary>
    /// Running moving average cost sau entry này.
    ///
    /// Lưu ý:
    /// - field này vẫn được giữ để snapshot/report/debug
    /// - KHÔNG phải source of truth cho outbound FIFO
    /// </summary>
    public decimal RunningAverageUnitCostAfter { get; set; }

    /// <summary>
    /// Nguồn cost của entry này.
    /// </summary>
    public InventoryCostSourceType CostSourceType { get; set; }

    /// <summary>
    /// Entry này có đang là provisional cost hay không.
    /// </summary>
    public bool IsProvisional { get; set; }

    /// <summary>
    /// Nếu entry từng provisional và đã được finalize sau revaluation,
    /// lưu lại thời điểm finalize.
    /// </summary>
    public DateTime? CostFinalizedAtUtc { get; set; }

    /// <summary>
    /// Nếu đây là 1 entry revaluation, field này trỏ về entry gốc bị revalue.
    /// </summary>
    public int? RevaluationOfEntryId { get; set; }

    /// <summary>
    /// Nếu đây là entry reverse/mirror từ 1 fragment nguồn
    /// (ví dụ return mirror sale issue gốc),
    /// field này trỏ tới valuation entry nguồn.
    /// </summary>
    public int? SourceValuationEntryId { get; set; }

    /// <summary>
    /// Snapshot subkey của fragment nguồn.
    ///
    /// Không bắt buộc về mặt relational,
    /// nhưng rất hữu ích để debug/audit nhanh.
    /// </summary>
    [StringLength(100)]
    public string? SourceReferenceSubKey { get; set; }

    /// <summary>
    /// Layer liên quan trực tiếp tới entry này.
    ///
    /// Quy ước:
    /// - inbound: thường trỏ tới layer được tạo
    /// - outbound actual: có thể trỏ tới layer đang consume
    /// - provisional outbound: thường null
    /// - revaluation: có thể trỏ tới layer inbound dùng để resolve
    /// </summary>
    public int? InventoryCostLayerId { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    // =========================
    // Navigation
    // =========================

    public InventoryTransaction InventoryTransaction { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;

    /// <summary>
    /// Layer được gắn trực tiếp với entry này nếu có.
    /// </summary>
    public InventoryCostLayer? InventoryCostLayer { get; set; }

    /// <summary>
    /// Allocation layer-based phát sinh từ entry này.
    /// Dùng để trace outbound consume / provisional open / reverse fragment.
    /// </summary>
    public ICollection<InventoryCostLayerAllocation> CostLayerAllocations { get; set; }
        = new List<InventoryCostLayerAllocation>();

    public InventoryValuationEntry? RevaluationOfEntry { get; set; }

    public ICollection<InventoryValuationEntry> RevaluationChildren { get; set; }
        = new List<InventoryValuationEntry>();

    public InventoryValuationEntry? SourceValuationEntry { get; set; }

    public ICollection<InventoryValuationEntry> ReverseChildren { get; set; }
        = new List<InventoryValuationEntry>();
}
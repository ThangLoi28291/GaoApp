using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Allocation giữa outbound/revaluation và inbound cost layer.
///
/// Đây là bảng quan trọng nhất để:
/// - biết outbound đã ăn vào layer nào
/// - biết provisional còn bao nhiêu chưa resolve
/// - biết inbound layer resolve cho outbound nào
/// - hỗ trợ return/void mirror đúng fragment gốc
///
/// Source of truth cho việc trace cost theo FIFO.
/// </summary>
public class InventoryCostLayerAllocation : BaseStoreEntity
{
    /// <summary>
    /// Valuation entry (outbound / revaluation / reverse) được allocate.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int InventoryValuationEntryId { get; set; }

    public InventoryValuationEntry InventoryValuationEntry { get; set; } = null!;

    /// <summary>
    /// Layer inbound thực tế được consume.
    /// Null nếu đây là provisional outbound chưa resolve.
    /// </summary>
    public int? InventoryCostLayerId { get; set; }

    public InventoryCostLayer? InventoryCostLayer { get; set; }

    /// <summary>
    /// Nếu đây là allocation reverse (void / return),
    /// thì trỏ về allocation gốc.
    /// </summary>
    public int? ReverseOfAllocationId { get; set; }

    public InventoryCostLayerAllocation? ReverseOfAllocation { get; set; }

    public ICollection<InventoryCostLayerAllocation> ReverseChildren { get; set; }
        = new List<InventoryCostLayerAllocation>();

    /// <summary>
    /// Số lượng được allocate.
    /// Luôn là số dương (abs).
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Đơn giá áp dụng cho allocation này.
    /// - Với actual allocation: là giá thật của layer đã consume.
    /// - Với provisional allocation chưa resolve: là provisional unit cost tại thời điểm outbound.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Giá trị của allocation.
    /// Thông thường = Quantity * UnitCost.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Có phải allocation provisional không.
    /// </summary>
    public bool IsProvisional { get; set; }

    /// <summary>
    /// Allocation này đã resolve xong hoàn toàn hay chưa.
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// Số lượng đã được inbound layer thật resolve.
    /// Dùng để hỗ trợ resolve từng phần.
    /// </summary>
    public decimal ResolvedQuantity { get; set; }

    /// <summary>
    /// Giá trị đã resolve theo cost thật.
    /// Dùng để trace và chuẩn bị cho revaluation/đối soát sau này.
    /// </summary>
    public decimal ResolvedAmount { get; set; }

    /// <summary>
    /// Thời điểm resolve xong hoặc resolve lần gần nhất.
    /// </summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>
    /// Layer inbound đã dùng để resolve allocation này lần gần nhất / lần hoàn tất.
    /// (dùng để audit nhanh, không cần join sâu)
    /// </summary>
    public int? ResolvedByInventoryCostLayerId { get; set; }

    public InventoryCostLayer? ResolvedByInventoryCostLayer { get; set; }

    /// <summary>
    /// Ghi chú nội bộ.
    /// </summary>
    [StringLength(1000)]
    public string? Note { get; set; }
}
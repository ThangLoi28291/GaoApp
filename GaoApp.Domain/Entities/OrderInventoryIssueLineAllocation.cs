using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Bảng trace cho workflow Inventory Issue.
/// 
/// Lưu vết issue line đã được hệ thống map/cấp phát bởi inbound source nào.
/// 
/// Lưu ý quan trọng:
/// - Đây KHÔNG phải source of truth cho costing FIFO
/// - Source of truth cho cost là:
///   + InventoryCostLayer
///   + InventoryCostLayerAllocation
/// - Bảng này chỉ phục vụ:
///   + workflow issue
///   + màn hình detail
///   + audit nghiệp vụ
/// </summary>
[Table("OrderInventoryIssueLineAllocations")]
public class OrderInventoryIssueLineAllocation : BaseStoreEntity
{
    [Range(1, int.MaxValue)]
    public int OrderInventoryIssueId { get; set; }

    public OrderInventoryIssue OrderInventoryIssue { get; set; } = default!;

    [Range(1, int.MaxValue)]
    public int OrderInventoryIssueLineId { get; set; }

    public OrderInventoryIssueLine OrderInventoryIssueLine { get; set; } = default!;

    /// <summary>
    /// Loại chứng từ inbound được hệ thống dùng để bù line âm.
    /// Ví dụ: StockDocument, Adjustment, CustomerReturn...
    /// </summary>
    public InventoryReferenceType SourceReferenceType { get; set; }

    /// <summary>
    /// Id chứng từ nguồn.
    /// Hiện hệ thống của bạn đang dùng int thực tế nên giữ int.
    /// </summary>
    public int SourceReferenceId { get; set; }

    /// <summary>
    /// Dòng chứng từ nguồn nếu có.
    /// </summary>
    public int? SourceReferenceLineId { get; set; }

    /// <summary>
    /// FIFO cost layer thực tế đã dùng để cover line âm.
    /// Đây mới là link đúng bản chất ở mức 2.
    /// </summary>
    public int? InventoryCostLayerId { get; set; }

    public InventoryCostLayer? InventoryCostLayer { get; set; }

    /// <summary>
    /// Allocation layer-based thực tế liên quan đến line issue này.
    /// Dùng để trace sâu hơn tới fragment cost đã resolve.
    /// </summary>
    public int? InventoryCostLayerAllocationId { get; set; }

    public InventoryCostLayerAllocation? InventoryCostLayerAllocation { get; set; }

    /// <summary>
    /// Giữ InventoryTransactionId để debug / UI / audit thuận tiện.
    /// 
    /// Lưu ý:
    /// - Không còn là source of truth
    /// - Có thể null nếu trace được bằng layer/allocation là đủ
    /// </summary>
    public int? InventoryTransactionId { get; set; }

    public InventoryTransaction? InventoryTransaction { get; set; }

    /// <summary>
    /// Số lượng được allocate cho issue line này.
    /// </summary>
    public decimal AllocatedQuantity { get; set; }

    /// <summary>
    /// Ghi chú kỹ thuật / audit.
    /// </summary>
    [StringLength(500)]
    public string? Note { get; set; }
}
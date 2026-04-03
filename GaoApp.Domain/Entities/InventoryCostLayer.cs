using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// FIFO cost layer cho từng lần nhập kho thực tế.
/// 
/// Mỗi inbound thực tế tạo ra 1 layer riêng:
/// - OriginalQuantity: số lượng gốc của layer
/// - RemainingQuantity: số lượng còn lại chưa bị consume
/// - UnitCost: giá vốn của layer đó
/// 
/// Đây là source of truth cho operational FIFO costing.
/// Không dùng InventoryTransaction hay AverageUnitCost để thay thế layer này.
/// </summary>
public class InventoryCostLayer : BaseStoreEntity
{
    /// <summary>
    /// Kho phát sinh layer.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    /// <summary>
    /// Biến thể sản phẩm phát sinh layer.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ProductVariantId { get; set; }

    /// <summary>
    /// InventoryTransaction inbound đã tạo layer này.
    /// Dùng để trace về movement ledger.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int InventoryTransactionId { get; set; }

    /// <summary>
    /// InventoryValuationEntry inbound tương ứng.
    /// Dùng để trace về valuation ledger.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int InventoryValuationEntryId { get; set; }

    /// <summary>
    /// Reference business gốc tạo ra layer, ví dụ:
    /// PurchaseReceipt / StockDocument / CustomerReturn...
    /// </summary>
    public InventoryReferenceType ReferenceType { get; set; }

    /// <summary>
    /// Id chứng từ nghiệp vụ gốc.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string ReferenceId { get; set; } = string.Empty;

    /// <summary>
    /// Dòng chi tiết trong chứng từ gốc nếu có.
    /// </summary>
    public int? ReferenceLineId { get; set; }

    /// <summary>
    /// Khóa phụ để tách fragment/sub-line nếu cần.
    /// </summary>
    [StringLength(100)]
    public string? ReferenceSubKey { get; set; }

    /// <summary>
    /// Số lượng gốc của layer lúc được tạo.
    /// </summary>
    public decimal OriginalQuantity { get; set; }

    /// <summary>
    /// Số lượng còn lại chưa được consume theo FIFO.
    /// Khi = 0 nghĩa là layer đã hết.
    /// 
    /// Lưu ý:
    /// field này phản ánh lượng còn lại của layer sau khi:
    /// - consume actual outbound
    /// - resolve provisional outbound cũ
    /// </summary>
    public decimal RemainingQuantity { get; set; }

    /// <summary>
    /// Tổng số lượng provisional outbound cũ đã được layer này resolve.
    /// Dùng để audit luồng inbound bù âm.
    /// </summary>
    public decimal ResolvedProvisionalQty { get; set; }

    /// <summary>
    /// Phần còn lại của layer chưa dùng để resolve provisional.
    /// Thực tế thường = OriginalQuantity - ResolvedProvisionalQty.
    /// Giữ riêng để đọc báo cáo/debug nhanh.
    /// </summary>
    public decimal RemainingOpenProvisionalQty { get; set; }

    /// <summary>
    /// Đơn giá cost của layer.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Đánh dấu layer có phải nguồn provisional hay không.
    /// Mức 2 hiện tại thường là false, nhưng giữ field để mở rộng sau.
    /// </summary>
    public bool IsProvisionalSource { get; set; }

    /// <summary>
    /// Thời điểm phát sinh layer.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// Ghi chú nội bộ.
    /// </summary>
    [StringLength(1000)]
    public string? Note { get; set; }

    // Navigation
    public Warehouse Warehouse { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;
    public InventoryTransaction InventoryTransaction { get; set; } = null!;
    public InventoryValuationEntry InventoryValuationEntry { get; set; } = null!;

    /// <summary>
    /// Danh sách allocation đã consume hoặc resolve vào layer này.
    /// </summary>
    public ICollection<InventoryCostLayerAllocation> Allocations { get; set; }
        = new List<InventoryCostLayerAllocation>();
}
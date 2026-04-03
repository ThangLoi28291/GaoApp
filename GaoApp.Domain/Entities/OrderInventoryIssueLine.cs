using GaoApp.Domain.Common;
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Dòng chi tiết bị âm tại thời điểm finalize order.
/// Lưu snapshot để audit và làm nền cho provisional cost / revaluation.
/// </summary>
public class OrderInventoryIssueLine : BaseStoreEntity
{
    public int OrderInventoryIssueId { get; set; }

    public int OrderId { get; set; }
    public int OrderLineId { get; set; }

    public int ProductId { get; set; }
    public int ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int? BarcodeId { get; set; }

    /// <summary>
    /// Số lượng bán của dòng order.
    /// </summary>
    public decimal OrderedQty { get; set; }

    /// <summary>
    /// Tồn trước khi finalize dòng này.
    /// </summary>
    public decimal StockBefore { get; set; }

    /// <summary>
    /// Tồn sau khi ghi nhận xuất kho.
    /// Có thể là số âm.
    /// </summary>
    public decimal StockAfter { get; set; }

    /// <summary>
    /// Mức thiếu tại thời điểm phát sinh.
    /// </summary>
    public decimal NegativeQty { get; set; }

    /// <summary>
    /// Giá vốn provisional tại thời điểm finalize.
    /// Dùng để làm nền cho Phase 5.15.
    /// </summary>
    public decimal? ProvisionalUnitCost { get; set; }

    /// <summary>
    /// Tổng giá vốn provisional của phần issue hoặc toàn dòng tùy rule sau này.
    /// </summary>
    public decimal? ProvisionalCostAmount { get; set; }

    /// <summary>
    /// Chênh lệch revaluation phát sinh sau này.
    /// </summary>
    public decimal? RevaluationAmount { get; set; }

    /// <summary>
    /// Dòng issue đã được xác nhận xử lý xong hay chưa.
    /// </summary>
    public bool IsResolved { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public virtual OrderInventoryIssue OrderInventoryIssue { get; set; } = null!;
    public virtual Order Order { get; set; } = null!;
    public virtual OrderLine OrderLine { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
    public virtual ProductVariant ProductVariant { get; set; } = null!;
    public virtual ProductUnitConversion? ProductUnitConversion { get; set; }
    public virtual ProductVariantUnitBarcode? Barcode { get; set; }
    /// <summary>
    /// Tổng qty inbound hệ thống tự detect và allocate cho line này.
    /// </summary>
    public decimal AutoDetectedInboundQty { get; set; }

    /// <summary>
    /// Chứng từ inbound đã tìm thấy hay chưa.
    /// </summary>
    public bool AutoDetectedDocumentResolved { get; set; }

    /// <summary>
    /// Giá vốn/revaluation đã xong hay chưa.
    /// </summary>
    public bool AutoDetectedCostResolved { get; set; }

    /// <summary>
    /// Số tiền revaluation auto-detect từ valuation engine.
    /// </summary>
    public decimal? AutoDetectedRevaluationAmount { get; set; }

    /// <summary>
    /// Snapshot message dễ đọc để UI hiển thị.
    /// </summary>
    [StringLength(2000)]
    public string? AutoResolveNote { get; set; }

    /// <summary>
    /// Lần cuối hệ thống tính auto-resolve cho line này.
    /// </summary>
    public DateTime? LastAutoResolvedAtUtc { get; set; }
    public ICollection<OrderInventoryIssueLineAllocation> Allocations { get; set; } = new List<OrderInventoryIssueLineAllocation>();
}

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Kết quả sau khi ghi 1 movement kho.
/// 
/// Phase 5.15:
/// - vừa phản ánh qty
/// - vừa phản ánh inventory value
/// - vừa phản ánh average cost
/// - hỗ trợ provisional valuation khi movement làm âm kho
/// 
/// Thiết kế theo hướng backward-compatible:
/// giữ lại các field cũ để tránh vỡ code các phase trước.
/// </summary>
public class InventoryMovementResultDto
{
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }

    /// <summary>
    /// Snapshot qty trước movement.
    /// </summary>
    public decimal BeforeQty { get; set; }

    /// <summary>
    /// Lượng thay đổi của movement.
    /// + nhập / - xuất.
    /// </summary>
    public decimal QuantityChange { get; set; }

    /// <summary>
    /// Snapshot qty sau movement.
    /// </summary>
    public decimal AfterQty { get; set; }

    /// <summary>
    /// True nếu sau movement thì tồn kho âm.
    /// </summary>
    public bool IsNegativeAfterTransaction { get; set; }

    /// <summary>
    /// True nếu request bật SkipIfExists
    /// và movement đã tồn tại nên bị bỏ qua.
    /// </summary>
    public bool IsSkipped { get; set; }

    // =========================
    // Phase 5.15 - valuation
    // =========================

    /// <summary>
    /// Snapshot giá trị tồn trước movement.
    /// </summary>
    public decimal BeforeValue { get; set; }

    /// <summary>
    /// Giá trị thay đổi do movement sinh ra.
    /// + nhập => tăng value
    /// - xuất => giảm value
    /// </summary>
    public decimal ValueChange { get; set; }

    /// <summary>
    /// Snapshot giá trị tồn sau movement.
    /// </summary>
    public decimal AfterValue { get; set; }

    /// <summary>
    /// Giá vốn bình quân trước movement.
    /// </summary>
    public decimal BeforeAverageCost { get; set; }

    /// <summary>
    /// Giá vốn bình quân sau movement.
    /// </summary>
    public decimal AfterAverageCost { get; set; }

    /// <summary>
    /// True nếu movement có phát sinh valuation tạm
    /// cho phần qty đi vào vùng âm kho.
    /// </summary>
    public bool HasProvisionalValuation { get; set; }

    /// <summary>
    /// Số lượng thuộc phần provisional.
    /// </summary>
    public decimal ProvisionalQuantity { get; set; }

    /// <summary>
    /// Unit cost tạm áp cho phần provisional.
    /// </summary>
    public decimal ProvisionalUnitCost { get; set; }

    /// <summary>
    /// Giá trị thay đổi thuộc riêng phần provisional.
    /// Thường là số âm khi xuất âm kho.
    /// </summary>
    public decimal ProvisionalValueChange { get; set; }

    /// <summary>
    /// Id InventoryTransaction vừa tạo.
    /// Null nếu movement bị skip.
    /// </summary>
    public int? InventoryTransactionId { get; set; }

    /// <summary>
    /// Danh sách id InventoryValuationEntry vừa tạo.
    /// 1 movement có thể sinh nhiều valuation entry.
    /// </summary>
    public List<int> InventoryValuationEntryIds { get; set; } = new();

    /// <summary>
    /// True nếu movement đã được tạo thành công.
    /// </summary>
    public bool IsCreated { get; set; }

    /// <summary>
    /// Số lượng thuộc phần actual (không provisional).
    /// </summary>
    public decimal ActualQuantity { get; set; }

    /// <summary>
    /// Giá trị thay đổi thuộc riêng phần actual.
    /// Thường là số âm khi xuất bán.
    /// </summary>
    public decimal ActualValueChange { get; set; }
}
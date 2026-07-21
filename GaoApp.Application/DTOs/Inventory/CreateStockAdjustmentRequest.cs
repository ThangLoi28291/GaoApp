using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Request điều chỉnh kho.
/// 
/// Lưu ý:
/// - Quantity là số lượng user nhập theo đơn vị đang chọn
/// - backend sẽ tự resolve factor và đổi về số lượng gốc
/// </summary>
public class CreateStockAdjustmentRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "WarehouseId không hợp lệ.")]
    public int WarehouseId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "ProductVariantId không hợp lệ.")]
    public int ProductVariantId { get; set; }

    /// <summary>
    /// Đơn vị user chọn trên form.
    /// Có thể là đơn vị gốc hoặc đơn vị quy đổi.
    /// </summary>
    public int? UnitId { get; set; }

    /// <summary>
    /// Chỉ chấp nhận:
    /// - AdjustmentIncrease
    /// - AdjustmentDecrease
    /// </summary>
    public InventoryTransactionType AdjustmentType { get; set; }

    /// <summary>
    /// Số lượng nhập theo đơn vị đã chọn.
    /// Ví dụ: 2 thùng.
    /// </summary>
    [Range(typeof(decimal), "0.001", "999999999", ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public decimal Quantity { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public decimal? UnitCost { get; set; }
    public decimal? ProvisionalUnitCost { get; set; }
}
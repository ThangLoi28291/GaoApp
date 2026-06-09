using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Dòng sản phẩm trong phiếu điều chỉnh kho.
/// Quantity là số lượng người dùng nhập theo đơn vị đang chọn.
/// BaseQuantity sẽ được service tự tính lại, không tin tuyệt đối từ client.
/// </summary>
public class InventoryAdjustmentLineRequestDto
{
    public int ProductVariantId { get; set; }

    public int? UnitId { get; set; }

    public int? ProductUnitConversionId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>
    /// Giá vốn thật, bắt buộc khi phiếu là điều chỉnh tăng.
    /// </summary>
    public decimal? UnitCost { get; set; }

    /// <summary>
    /// Giá vốn tạm, dùng khi điều chỉnh giảm nếu cần.
    /// </summary>
    public decimal? ProvisionalUnitCost { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Thêm dòng kiểm kê.
/// </summary>
public class AddStockCountLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "ProductVariantId không hợp lệ.")]
    public int ProductVariantId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "UnitId không hợp lệ.")]
    public int UnitId { get; set; }

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "CountedQty không hợp lệ.")]
    public decimal CountedQty { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}
